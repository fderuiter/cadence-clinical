using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Database.Temporal;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Knowledge.Application;

public interface ITrainingDelegationService
{
    Task<IResult> ListDelegationsAsync(
        string? studyId,
        string? siteId,
        string? status,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null);

    Task<IResult> BulkSignOffDelegationsAsync(
        BulkDelegationSignOffRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

public sealed class TrainingDelegationService(
    KnowledgeDbContext dbContext,
    IKnowledgeUnitOfWork unitOfWork,
    IClock clock,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayAuthentication) : ITrainingDelegationService
{
    public async Task<IResult> ListDelegationsAsync(
        string? studyId,
        string? siteId,
        string? status,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null)
    {
        if (!ClinicalScopeGuard.ResolveStudyAndSiteFilters(httpContext, studyId, siteId, out var studies, out _, out var denied))
        {
            return denied!;
        }

        if (validAt.HasValue || knownAt.HasValue)
        {
            DateTimeOffset effectiveValidAt = validAt ?? knownAt!.Value;
            DateTimeOffset effectiveKnownAt = knownAt ?? validAt!.Value;
            var periodQuery = dbContext.DelegationRecordPeriods.AsNoTracking();
            if (studies != null) periodQuery = periodQuery.Where(d => studies.Contains(d.StudyCode));
            if (!string.IsNullOrEmpty(studyId)) periodQuery = periodQuery.Where(d => d.StudyCode == studyId);
            if (!string.IsNullOrEmpty(siteId)) periodQuery = periodQuery.Where(d => d.SiteCode == siteId);
            if (!string.IsNullOrEmpty(status)) periodQuery = periodQuery.Where(d => d.Status == status);

            var periods = (await periodQuery.QueryAsOfAsync(dbContext, effectiveValidAt, effectiveKnownAt, cancellationToken))
                .Where(d => ClinicalScopeGuard.CanAccessSite(httpContext, d.SiteCode))
                .ToList();

            var periodDtos = periods.Select(d => new DelegationSummaryDto(
                d.DelegationRecordId, d.StudyCode, d.SiteCode, d.StaffUserId, d.StaffName, d.Role, d.TaskCode, d.Duties,
                d.Status, d.SignedOff, d.PiSignatureHash, d.PiApprovedAt, d.BatchTransactionId)).ToList();

            return TypedResults.Ok(periodDtos);
        }

        var query = dbContext.DelegationRecords.AsNoTracking();
        if (studies != null) query = query.Where(d => studies.Contains(d.StudyId));
        if (!string.IsNullOrEmpty(studyId)) query = query.Where(d => d.StudyId == studyId);
        if (!string.IsNullOrEmpty(siteId)) query = query.Where(d => d.SiteId == siteId);
        if (!string.IsNullOrEmpty(status)) query = query.Where(d => d.Status == status);

        var list = (await query.ToListAsync(cancellationToken))
            .Where(d => ClinicalScopeGuard.CanAccessSite(httpContext, d.SiteId))
            .ToList();

        var dtos = list.Select(d => new DelegationSummaryDto(
            d.Id, d.StudyId, d.SiteId, d.StaffUserId, d.StaffName, d.Role, d.TaskCode, d.Duties,
            d.Status, d.SignedOff, d.PiSignatureHash, d.PiApprovedAt, d.BatchTransactionId)).ToList();

        return TypedResults.Ok(dtos);
    }

    public async Task<IResult> BulkSignOffDelegationsAsync(
        BulkDelegationSignOffRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.DelegationIds == null || request.DelegationIds.Count == 0)
        {
            return TypedResults.Problem("At least one delegation record ID is required for bulk sign-off.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Limit Guardrail check (200 limit)
        if (request.DelegationIds.Count > TrainingCurriculumService.MaxBatchLimit)
        {
            return TypedResults.Problem(
                $"Synchronous batch API endpoints enforce a limit of {TrainingCurriculumService.MaxBatchLimit} target items per request. Requested: {request.DelegationIds.Count}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The filter has verified and consumed the token for this caller and these ids.
        if (httpContext.VerifiedSigToken() is null
            || CallerScope.Principal(httpContext) is not { } signer)
        {
            return TypedResults.Problem(SigTokenVerifier.ReauthenticationRequired, statusCode: StatusCodes.Status401Unauthorized);
        }

        string secret = gatewayAuthentication.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret
            is { Length: > 0 } configured
            ? configured
            : throw new InvalidOperationException("GATEWAY_SECRET is not configured; delegation sign-offs cannot be sealed.");

        var ids = request.DelegationIds.Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var records = await dbContext.DelegationRecords
            .Where(d => ids.Contains(d.Id))
            .ToListAsync(cancellationToken);
        var missing = ids.Except(records.Select(r => r.Id), StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            return TypedResults.Problem(
                $"No delegation record exists for: {string.Join(", ", missing)}. Nothing was signed.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (records.Exists(r => !StudyAccessEvaluator.CanAccessStudy(signer, r.StudyId) || !SiteAccessEvaluator.CanAccessSite(signer, r.SiteId)))
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        var now = clock.UtcNow;
        string batchTxId = Guid.NewGuid().ToString();
        string signerRole = string.Join(",", signer.Roles);

        string sealInput = $"{batchTxId}|{string.Join(",", ids.Order(StringComparer.Ordinal))}|{signer.UserId}|{signerRole}|{request.SignaturePayload?.Meaning}|{now:O}";
        string seal = HmacSigner.ComputeSignature(sealInput, secret);
        string reasonForChange = request.SignaturePayload?.ReasonForChange ?? "21 CFR Part 11 Principal Investigator bulk delegation sign-off";

        var existingPeriods = await dbContext.DelegationRecordPeriods
            .Where(p => ids.Contains(p.DelegationRecordId))
            .ToListAsync(cancellationToken);

        var signedList = new List<DelegationSummaryDto>();
        foreach (var record in records.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            string priorStatus = record.Status;
            bool priorSignedOff = record.SignedOff;
            string? priorHash = record.PiSignatureHash;
            DateTimeOffset? priorApprovedAt = record.PiApprovedAt;
            string? priorBatchTxId = record.BatchTransactionId;

            var recordPeriods = existingPeriods
                .Where(p => p.DelegationRecordId == record.Id)
                .ToList();
            var activeOpen = recordPeriods
                .Where(p => p.RecordedPeriod.UpperBoundInfinite && p.ValidPeriod.UpperBoundInfinite)
                .ToList();

            DateTimeOffset recordedCutover = now;
            foreach (var open in activeOpen)
            {
                var closed = StandaloneBitemporalModelExtensions.CloseRecordedAt(open.RecordedPeriod, now);
                open.RecordedPeriod = closed;
                if (closed.UpperBound > recordedCutover)
                {
                    recordedCutover = closed.UpperBound;
                }
            }

            var openRecorded = StandaloneBitemporalModelExtensions.CreateOpenPeriod(recordedCutover);
            if (activeOpen.Count == 0 && record.CreatedAt < recordedCutover)
            {
                dbContext.DelegationRecordPeriods.Add(new DelegationRecordPeriod
                {
                    DelegationRecordId = record.Id,
                    StudyCode = record.StudyId,
                    SiteCode = record.SiteId,
                    StaffUserId = record.StaffUserId,
                    StaffName = record.StaffName,
                    Role = record.Role,
                    TaskCode = record.TaskCode,
                    Duties = record.Duties,
                    Status = priorStatus,
                    SignedOff = priorSignedOff,
                    PiSignatureHash = priorHash,
                    PiApprovedAt = priorApprovedAt,
                    BatchTransactionId = priorBatchTxId,
                    CreatedBy = string.IsNullOrWhiteSpace(record.CreatedBy) ? signer.UserId : record.CreatedBy,
                    ReasonForChange = "Initial delegation period",
                    ValidPeriod = StandaloneBitemporalModelExtensions.CreateClosedPeriod(record.CreatedAt, recordedCutover),
                    RecordedPeriod = openRecorded,
                });
            }
            else
            {
                foreach (var prior in activeOpen.Where(p => p.ValidPeriod.LowerBound < recordedCutover))
                {
                    dbContext.DelegationRecordPeriods.Add(new DelegationRecordPeriod
                    {
                        DelegationRecordId = prior.DelegationRecordId,
                        StudyCode = prior.StudyCode,
                        SiteCode = prior.SiteCode,
                        StaffUserId = prior.StaffUserId,
                        StaffName = prior.StaffName,
                        Role = prior.Role,
                        TaskCode = prior.TaskCode,
                        Duties = prior.Duties,
                        Status = prior.Status,
                        SignedOff = prior.SignedOff,
                        PiSignatureHash = prior.PiSignatureHash,
                        PiApprovedAt = prior.PiApprovedAt,
                        BatchTransactionId = prior.BatchTransactionId,
                        CreatedBy = prior.CreatedBy,
                        ReasonForChange = prior.ReasonForChange,
                        ValidPeriod = StandaloneBitemporalModelExtensions.CreateClosedPeriod(prior.ValidPeriod.LowerBound, recordedCutover),
                        RecordedPeriod = openRecorded,
                    });
                }
            }

            record.Status = "COMPLETED";
            record.SignedOff = true;
            record.PiSignatureHash = seal;
            record.PiApprovedAt = now;
            record.BatchTransactionId = batchTxId;

            dbContext.DelegationRecordPeriods.Add(new DelegationRecordPeriod
            {
                DelegationRecordId = record.Id,
                StudyCode = record.StudyId,
                SiteCode = record.SiteId,
                StaffUserId = record.StaffUserId,
                StaffName = record.StaffName,
                Role = record.Role,
                TaskCode = record.TaskCode,
                Duties = record.Duties,
                Status = record.Status,
                SignedOff = record.SignedOff,
                PiSignatureHash = record.PiSignatureHash,
                PiApprovedAt = record.PiApprovedAt,
                BatchTransactionId = record.BatchTransactionId,
                CreatedBy = signer.UserId,
                ReasonForChange = reasonForChange,
                ValidPeriod = StandaloneBitemporalModelExtensions.CreateOpenPeriod(recordedCutover),
                RecordedPeriod = openRecorded,
            });

            signedList.Add(new DelegationSummaryDto(
                record.Id, record.StudyId, record.SiteId, record.StaffUserId, record.StaffName,
                record.Role, record.TaskCode, record.Duties, record.Status, record.SignedOff,
                record.PiSignatureHash, record.PiApprovedAt, record.BatchTransactionId));
        }

        dbContext.OrgAuditLogs.Add(new OrgAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = signer.UserId,
            ActorRole = signerRole,
            Action = "training.delegation.bulk_signoff",
            BatchTransactionId = batchTxId,
            Details = $"Executed Part 11 bulk delegation sign-off for {signedList.Count} delegation log records. Routed to eTMF Zone 5 (Artifact 05.02.04). Seal HMAC-SHA256: {seal}",
            ReasonForChange = request.SignaturePayload?.ReasonForChange ?? "21 CFR Part 11 Principal Investigator bulk delegation sign-off"
        });

        dbContext.TrainingCertificates.Add(new TrainingCertificate
        {
            Id = Guid.NewGuid().ToString(),
            AssignmentId = signedList[0].Id,
            BatchTransactionId = batchTxId,
            SignerName = signer.UserId,
            SignerRole = signerRole,
            Meaning = request.SignaturePayload?.Meaning ?? string.Empty,
            Sha256Seal = seal,
            EtmfArtifactId = "05.02.04",
            CreatedAt = now
        });

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(new BulkDelegationSignOffResponse(
            batchTxId,
            ids.Count,
            signedList.Count,
            0,
            signedList,
            [],
            seal,
            "05.02.04",
            now));
    }
}
