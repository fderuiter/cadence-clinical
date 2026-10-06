using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Background;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Application.Subjects;

public sealed class SubjectBatchApplicationService(
    ISubjectBatchRepository batchRepo,
    ISubjectRepository subjectRepo,
    IDemographicsEncryptionService demographicsEncryption,
    IAuditLogWriter auditLog,
    ICurrentAuditContextAccessor auditContext,
    ISubjectBatchQueue batchQueue,
    IClock clock) : ISubjectBatchApplicationService
{
    public async Task<Result<SubjectBatchUploadResponse>> UploadBatchAsync(
        string studyId,
        Stream csvStream,
        string fileName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return DomainError.Validation("STUDY_ID_REQUIRED", "study_id is required for batch import.");
        }

        StudyCode studyCode;
        try
        {
            studyCode = new StudyCode(studyId);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_STUDY_CODE", ex.Message);
        }

        IReadOnlyList<ParsedCsvRow> parsedRows;
        try
        {
            parsedRows = await SubjectCsvParser.ParseAsync(csvStream, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return DomainError.Validation("CSV_PARSING_ERROR", ex.Message);
        }

        string batchId = $"BATCH-{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        string createdBy = auditContext.UserId;

        var batch = SubjectBatchImport.Create(
            batchId,
            studyCode,
            fileName,
            createdBy,
            clock.UtcNow);

        foreach (var parsed in parsedRows)
        {
            batch.AddRow(
                parsed.RowNumber,
                parsed.SubjectId,
                parsed.SiteCode,
                parsed.ArmId,
                parsed.Name,
                parsed.Birthdate,
                parsed.Gender,
                parsed.Race);
        }

        await batchRepo.AddAsync(batch, ct).ConfigureAwait(false);
        await batchRepo.SaveChangesAsync(ct).ConfigureAwait(false);

        await batchQueue.EnqueueAsync(batch.Id, ct).ConfigureAwait(false);

        return new SubjectBatchUploadResponse(
            batch.BatchId,
            batch.StudyCode,
            batch.FileName,
            batch.TotalRows,
            batch.Status.ToString(),
            batch.CreatedAt);
    }

    public async Task<Result<SubjectBatchDetailResponse>> GetBatchDetailAsync(
        string batchId,
        CancellationToken ct)
    {
        SubjectBatchImport? batch = null;

        if (Guid.TryParse(batchId, out var idGuid))
        {
            batch = await batchRepo.GetByIdAsync(idGuid, ct).ConfigureAwait(false);
        }

        batch ??= await batchRepo.GetByBatchIdAsync(batchId, ct).ConfigureAwait(false);

        if (batch is null)
        {
            return DomainError.NotFound("BATCH_NOT_FOUND", $"Batch '{batchId}' was not found.");
        }

        var rowResponses = batch.Rows
            .OrderBy(r => r.RowNumber)
            .Select(r => new SubjectBatchRowResponse(
                r.RowNumber,
                r.SubjectId,
                r.SiteCode,
                r.ArmId,
                r.Name,
                r.Birthdate,
                r.Gender,
                r.Race,
                r.Status.ToString(),
                r.ErrorMessage))
            .ToList();

        return new SubjectBatchDetailResponse(
            batch.BatchId,
            batch.StudyCode,
            batch.FileName,
            batch.TotalRows,
            batch.ValidRows,
            batch.InvalidRows,
            batch.Status.ToString(),
            batch.CreatedAt,
            batch.CompletedAt,
            batch.CommittedAt,
            rowResponses);
    }

    public async Task<Result<SubjectBatchCommitResponse>> CommitBatchAsync(
        string batchId,
        string changeReason,
        CancellationToken ct)
    {
        SubjectBatchImport? batch = null;

        if (Guid.TryParse(batchId, out var idGuid))
        {
            batch = await batchRepo.GetByIdAsync(idGuid, ct).ConfigureAwait(false);
        }

        batch ??= await batchRepo.GetByBatchIdAsync(batchId, ct).ConfigureAwait(false);

        if (batch is null)
        {
            return DomainError.NotFound("BATCH_NOT_FOUND", $"Batch '{batchId}' was not found.");
        }

        if (batch.Status == SubjectBatchStatus.Committed)
        {
            return DomainError.Conflict("BATCH_ALREADY_COMMITTED", $"Batch '{batchId}' has already been committed.");
        }

        var validRows = batch.Rows.Where(r => r.Status == SubjectBatchRowStatus.Valid).ToList();
        if (validRows.Count == 0)
        {
            return DomainError.Validation("NO_VALID_ROWS", $"Batch '{batchId}' has no valid rows to commit.");
        }

        var committedSubjectCodes = new List<string>();

        foreach (var row in validRows)
        {
            int enrollmentIndex = await subjectRepo.EnrollNextIndexAsync(batch.StudyCode, ct).ConfigureAwait(false);

            string? encryptedDemographics = null;
            if (!string.IsNullOrWhiteSpace(row.Name) || !string.IsNullOrWhiteSpace(row.Birthdate) ||
                !string.IsNullOrWhiteSpace(row.Gender) || !string.IsNullOrWhiteSpace(row.Race))
            {
                var demoDict = new Dictionary<string, object?>
                {
                    ["name"] = row.Name,
                    ["birthdate"] = row.Birthdate,
                    ["gender"] = row.Gender,
                    ["race"] = row.Race
                };
                encryptedDemographics = demographicsEncryption.Encrypt(demoDict);
            }

            var subjectCode = new SubjectCode(row.SubjectId);
            SiteCode? siteCode = row.SiteCode;

            var subject = ClinicalSubject.Create(
                subjectCode,
                batch.StudyCode,
                siteCode,
                enrollmentIndex,
                encryptedDemographics,
                stratFactors: null,
                treatmentGroup: row.ArmId);

            subject.ReasonForChange = changeReason;

            await subjectRepo.AddAsync(subject, ct).ConfigureAwait(false);
            await auditLog.WriteAsync(
                nameof(ClinicalSubject),
                subject.Id.ToString(),
                "ENROLL_SUBJECT",
                auditContext.UserId,
                changeReason,
                oldValues: null,
                newValues: null,
                ct).ConfigureAwait(false);

            row.MarkCommitted();
            committedSubjectCodes.Add(subjectCode.Value);
        }

        batch.MarkCommitted(clock.UtcNow);

        await subjectRepo.SaveChangesAsync(ct).ConfigureAwait(false);
        await batchRepo.SaveChangesAsync(ct).ConfigureAwait(false);

        return new SubjectBatchCommitResponse(
            batch.BatchId,
            committedSubjectCodes.Count,
            committedSubjectCodes,
            clock.UtcNow);
    }
}
