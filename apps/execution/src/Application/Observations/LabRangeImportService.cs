using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Application.Observations;

/// <summary>
/// Core implementation of bulk lab reference range import workflow.
/// Conforms to PRD-SYS-121 and 21 CFR Part 11 audit requirements.
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public sealed class LabRangeImportService : ILabRangeImportService
{
    private readonly ExecutionDbContext _dbContext;
    private readonly IUcumNormalizationService _ucumNormalizationService;
    private readonly IClock _clock;

    public LabRangeImportService(
        ExecutionDbContext dbContext,
        IUcumNormalizationService ucumNormalizationService,
        IClock clock)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _ucumNormalizationService = ucumNormalizationService ?? throw new ArgumentNullException(nameof(ucumNormalizationService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<BulkLabRangeDryRunResponse> DryRunAsync(BulkLabRangeDryRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        byte[] bytes;
        if (!string.IsNullOrWhiteSpace(request.FileContentBase64))
        {
            try
            {
                bytes = Convert.FromBase64String(request.FileContentBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Uploaded file content is not valid Base64.", ex);
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.RawFileContent))
        {
            bytes = Encoding.UTF8.GetBytes(request.RawFileContent);
        }
        else
        {
            throw new ArgumentException("No file content provided in request.", nameof(request));
        }

        var parsedRows = BulkLabRangeFileParser.Parse(bytes, request.FileName);
        var rowPreviews = new List<BulkLabRangeRowPreview>();

        int validRows = 0;
        int invalidRows = 0;
        int loincMappedCount = 0;
        int unitConvertedCount = 0;

        foreach (var row in parsedRows)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(row.TestCode))
            {
                errors.Add("Test code (analyte code) is required.");
            }

            if (row.LowBound.HasValue && row.HighBound.HasValue && row.LowBound.Value > row.HighBound.Value)
            {
                errors.Add($"Lower bound ({row.LowBound.Value}) cannot exceed upper bound ({row.HighBound.Value}).");
            }

            if (row.AgeLow.HasValue && row.AgeHigh.HasValue && row.AgeLow.Value > row.AgeHigh.Value)
            {
                errors.Add($"Lower age ({row.AgeLow.Value}) cannot exceed upper age ({row.AgeHigh.Value}).");
            }

            if (row.CriticalLow.HasValue && row.LowBound.HasValue && row.CriticalLow.Value > row.LowBound.Value)
            {
                warnings.Add($"Critical low ({row.CriticalLow.Value}) is higher than lower bound ({row.LowBound.Value}).");
            }

            if (row.CriticalHigh.HasValue && row.HighBound.HasValue && row.CriticalHigh.Value < row.HighBound.Value)
            {
                warnings.Add($"Critical high ({row.CriticalHigh.Value}) is lower than upper bound ({row.HighBound.Value}).");
            }

            // LOINC code mapping
            string? loincCode = row.LoincCode;
            if (string.IsNullOrWhiteSpace(loincCode) && !string.IsNullOrWhiteSpace(row.TestCode))
            {
                loincCode = _ucumNormalizationService.MapLoincCode(row.TestCode);
                if (!string.IsNullOrWhiteSpace(loincCode))
                {
                    loincMappedCount++;
                }
            }

            // Target SI unit resolution and unit conversion
            string targetUnit = !string.IsNullOrWhiteSpace(row.NormalizedUnit)
                ? row.NormalizedUnit
                : _ucumNormalizationService.GetTargetSiUnit(row.TestCode, row.Unit);

            double? convertedLow = row.LowBound;
            double? convertedHigh = row.HighBound;
            double? factor = null;

            if (!string.IsNullOrWhiteSpace(row.Unit) && !string.IsNullOrWhiteSpace(targetUnit) &&
                !string.Equals(row.Unit, targetUnit, StringComparison.OrdinalIgnoreCase))
            {
                if (row.LowBound.HasValue)
                {
                    var (convVal, f) = _ucumNormalizationService.ConvertLabUnit(row.LowBound.Value, row.Unit, targetUnit, row.TestCode);
                    convertedLow = convVal;
                    factor = f;
                }
                if (row.HighBound.HasValue)
                {
                    var (convVal, f) = _ucumNormalizationService.ConvertLabUnit(row.HighBound.Value, row.Unit, targetUnit, row.TestCode);
                    convertedHigh = convVal;
                    factor ??= f;
                }
                if (factor.HasValue && Math.Abs(factor.Value - 1.0) > 0.0001)
                {
                    unitConvertedCount++;
                }
            }

            bool isValid = errors.Count == 0;
            if (isValid) validRows++; else invalidRows++;

            rowPreviews.Add(new BulkLabRangeRowPreview(
                RowNumber: row.RowNumber,
                IsValid: isValid,
                TestCode: row.TestCode,
                TestName: row.TestName,
                Source: row.Source,
                SiteId: row.SiteId,
                SexApplicability: row.SexApplicability,
                AgeLow: row.AgeLow,
                AgeHigh: row.AgeHigh,
                Unit: row.Unit,
                NormalizedUnit: targetUnit,
                LowBound: row.LowBound,
                HighBound: row.HighBound,
                ConvertedLowBound: convertedLow,
                ConvertedHighBound: convertedHigh,
                CriticalLow: row.CriticalLow,
                CriticalHigh: row.CriticalHigh,
                LoincCode: loincCode,
                ConversionFactor: factor,
                Errors: errors,
                Warnings: warnings
            ));
        }

        string dryRunId = Guid.NewGuid().ToString();
        bool isSuccess = invalidRows == 0;
        string summary = $"Dry-run completed for {request.FileName}: {validRows} valid rows, {invalidRows} invalid rows. LOINC mapped: {loincMappedCount}, SI unit converted: {unitConvertedCount}.";

        return Task.FromResult(new BulkLabRangeDryRunResponse(
            IsSuccess: isSuccess,
            TotalRows: parsedRows.Count,
            ValidRows: validRows,
            InvalidRows: invalidRows,
            LoincMappedCount: loincMappedCount,
            UnitConvertedCount: unitConvertedCount,
            Rows: rowPreviews,
            Summary: summary,
            DryRunId: dryRunId
        ));
    }

    public async Task<BulkLabRangeImportJobResponse> CommitImportAsync(BulkLabRangeImportCommitRequest request, string submittedBy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.FileContentBase64) && string.IsNullOrWhiteSpace(request.RawFileContent))
        {
            throw new ArgumentException("No file content provided in commit request.", nameof(request));
        }

        var dryRunResult = await DryRunAsync(new BulkLabRangeDryRunRequest(
            StudyId: request.StudyId,
            SiteId: request.SiteId,
            LabSource: request.LabSource,
            FileName: request.FileName,
            FileContentBase64: request.FileContentBase64,
            RawFileContent: request.RawFileContent
        ), ct);

        var nowUtc = _clock.UtcNow;
        int successCount = 0;
        int errorCount = 0;
        var errorSummaries = new List<string>();

        foreach (var rowPreview in dryRunResult.Rows)
        {
            if (!rowPreview.IsValid)
            {
                errorCount++;
                errorSummaries.Add($"Row {rowPreview.RowNumber} [{rowPreview.TestCode}]: {string.Join("; ", rowPreview.Errors)}");
                continue;
            }

            var entity = new LabReferenceRange(
                id: Guid.NewGuid(),
                studyId: request.StudyId,
                testCode: rowPreview.TestCode,
                testName: rowPreview.TestName,
                source: string.IsNullOrWhiteSpace(rowPreview.Source) ? request.LabSource : rowPreview.Source,
                siteId: string.IsNullOrWhiteSpace(rowPreview.SiteId) ? request.SiteId : rowPreview.SiteId,
                unit: rowPreview.Unit,
                normalizedUnit: rowPreview.NormalizedUnit,
                sexApplicability: rowPreview.SexApplicability,
                ageLow: rowPreview.AgeLow,
                ageHigh: rowPreview.AgeHigh,
                lowBound: rowPreview.ConvertedLowBound ?? rowPreview.LowBound,
                highBound: rowPreview.ConvertedHighBound ?? rowPreview.HighBound,
                criticalLow: rowPreview.CriticalLow,
                criticalHigh: rowPreview.CriticalHigh,
                loincCode: rowPreview.LoincCode,
                createdBy: submittedBy,
                reasonForChange: request.ReasonForChange,
                createdAt: nowUtc.UtcDateTime,
                isActive: true,
                isDeleted: false
            );

            _dbContext.LabReferenceRanges.Add(entity);
            successCount++;
        }

        var jobId = Guid.NewGuid().ToString();
        string fileType = Path.GetExtension(request.FileName).TrimStart('.').ToUpperInvariant();
        string status;
        if (errorCount > 0)
        {
            status = successCount > 0 ? "COMPLETED_WITH_ERRORS" : "FAILED";
        }
        else
        {
            status = "COMPLETED";
        }

        string? errorSummaryJson = errorSummaries.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(errorSummaries) : null;
        string previewSummaryJson = System.Text.Json.JsonSerializer.Serialize(dryRunResult);

        var jobEntity = new BulkLabRangeImportJob(
            id: Guid.NewGuid(),
            jobId: jobId,
            studyId: request.StudyId,
            siteId: request.SiteId,
            fileName: request.FileName,
            fileType: string.IsNullOrWhiteSpace(fileType) ? "CSV" : fileType,
            status: status,
            totalRows: dryRunResult.TotalRows,
            successRows: successCount,
            errorRows: errorCount,
            errorSummaryJson: errorSummaryJson,
            previewSummaryJson: previewSummaryJson,
            submittedBy: submittedBy,
            reasonForChange: request.ReasonForChange,
            submittedAt: nowUtc,
            completedAt: nowUtc
        );

        _dbContext.BulkLabRangeImportJobs.Add(jobEntity);

        await _dbContext.SaveChangesAsync(ct);

        return MapJobToResponse(jobEntity);
    }

    public async Task<IReadOnlyList<LabReferenceRangeResponse>> GetReferenceRangesAsync(string studyId, string? siteId = null, CancellationToken ct = default)
    {
        var query = _dbContext.LabReferenceRanges.AsNoTracking().Where(r => !r.IsDeleted && r.IsActive);
        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(r => r.StudyId == studyId);
        }
        if (!string.IsNullOrWhiteSpace(siteId))
        {
            query = query.Where(r => r.SiteId == siteId || r.SiteId == null);
        }

        var list = await query.OrderBy(r => r.TestCode).ThenBy(r => r.SexApplicability).ToListAsync(ct);
        return list.Select(MapRangeToResponse).ToList();
    }

    public async Task<LabReferenceRangeResponse?> GetReferenceRangeByIdAsync(Guid id, CancellationToken ct = default)
    {
        var range = await _dbContext.LabReferenceRanges.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct);
        return range != null ? MapRangeToResponse(range) : null;
    }

    public async Task<LabReferenceRangeResponse> CreateReferenceRangeAsync(LabReferenceRangeCreateRequest request, string createdBy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string normUnit = request.NormalizedUnit ?? _ucumNormalizationService.GetTargetSiUnit(request.TestCode, request.Unit);
        string? loinc = request.LoincCode ?? _ucumNormalizationService.MapLoincCode(request.TestCode);

        var entity = new LabReferenceRange(
            id: Guid.NewGuid(),
            studyId: request.StudyId,
            testCode: request.TestCode,
            testName: request.TestName,
            source: request.Source,
            siteId: request.SiteId,
            unit: request.Unit,
            normalizedUnit: normUnit,
            sexApplicability: request.SexApplicability,
            ageLow: request.AgeLow,
            ageHigh: request.AgeHigh,
            lowBound: request.LowBound,
            highBound: request.HighBound,
            criticalLow: request.CriticalLow,
            criticalHigh: request.CriticalHigh,
            loincCode: loinc,
            createdBy: createdBy,
            reasonForChange: request.ReasonForChange,
            createdAt: _clock.UtcNow.UtcDateTime,
            isActive: true,
            isDeleted: false
        );

        _dbContext.LabReferenceRanges.Add(entity);
        await _dbContext.SaveChangesAsync(ct);

        return MapRangeToResponse(entity);
    }

    public async Task<LabReferenceRangeResponse?> UpdateReferenceRangeAsync(Guid id, LabReferenceRangeUpdateRequest request, string updatedBy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entity = await _dbContext.LabReferenceRanges.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct);
        if (entity == null) return null;

        entity.Update(
            testName: request.TestName ?? entity.TestName,
            unit: request.Unit ?? entity.Unit,
            normalizedUnit: request.NormalizedUnit ?? entity.NormalizedUnit,
            sexApplicability: request.SexApplicability ?? entity.SexApplicability,
            ageLow: request.AgeLow ?? entity.AgeLow,
            ageHigh: request.AgeHigh ?? entity.AgeHigh,
            lowBound: request.LowBound ?? entity.LowBound,
            highBound: request.HighBound ?? entity.HighBound,
            criticalLow: request.CriticalLow ?? entity.CriticalLow,
            criticalHigh: request.CriticalHigh ?? entity.CriticalHigh,
            loincCode: request.LoincCode ?? entity.LoincCode,
            updatedBy: updatedBy,
            reasonForChange: request.ReasonForChange
        );

        await _dbContext.SaveChangesAsync(ct);

        return MapRangeToResponse(entity);
    }

    public async Task<bool> DeleteReferenceRangeAsync(Guid id, string updatedBy, string reasonForChange, CancellationToken ct = default)
    {
        var entity = await _dbContext.LabReferenceRanges.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct);
        if (entity == null) return false;

        entity.Deactivate(updatedBy, reasonForChange);
        await _dbContext.SaveChangesAsync(ct);

        return true;
    }

    public async Task<IReadOnlyList<BulkLabRangeImportJobResponse>> GetImportJobsAsync(string studyId, CancellationToken ct = default)
    {
        var query = _dbContext.BulkLabRangeImportJobs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(j => j.StudyId == studyId);
        }

        var jobs = await query.OrderByDescending(j => j.SubmittedAt).ToListAsync(ct);
        return jobs.Select(MapJobToResponse).ToList();
    }

    public async Task<BulkLabRangeImportJobResponse?> GetImportJobByIdAsync(Guid id, CancellationToken ct = default)
    {
        var job = await _dbContext.BulkLabRangeImportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id || j.JobId == id.ToString(), ct);
        if (job == null) return null;
        return MapJobToResponse(job);
    }

    public UnitConversionResponse ConvertUnit(UnitConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (convValue, factor) = _ucumNormalizationService.ConvertLabUnit(request.Value, request.FromUnit, request.ToUnit, request.TestCode);
        string? loinc = _ucumNormalizationService.MapLoincCode(request.TestCode);

        return new UnitConversionResponse(
            TestCode: request.TestCode,
            InputValue: request.Value,
            ConvertedValue: convValue,
            FromUnit: request.FromUnit,
            ToUnit: request.ToUnit,
            Factor: factor,
            LoincCode: loinc
        );
    }

    public LoincCatalogResponse GetLoincCatalog()
    {
        var items = _ucumNormalizationService.GetLoincCatalog();
        return new LoincCatalogResponse(
            Items: items,
            TotalCount: items.Count
        );
    }

    private static LabReferenceRangeResponse MapRangeToResponse(LabReferenceRange r) =>
        new LabReferenceRangeResponse(
            Id: r.Id.ToString(),
            StudyId: r.StudyId,
            TestCode: r.TestCode,
            TestName: r.TestName,
            Source: r.Source,
            SiteId: r.SiteId,
            Unit: r.Unit,
            NormalizedUnit: r.NormalizedUnit,
            SexApplicability: r.SexApplicability,
            AgeLow: r.AgeLow,
            AgeHigh: r.AgeHigh,
            LowBound: r.LowBound,
            HighBound: r.HighBound,
            CriticalLow: r.CriticalLow,
            CriticalHigh: r.CriticalHigh,
            LoincCode: r.LoincCode,
            IsActive: r.IsActive,
            Version: r.VersionIndex,
            CreatedAt: r.CreatedAt,
            CreatedBy: r.CreatedBy
        );

    private static BulkLabRangeImportJobResponse MapJobToResponse(BulkLabRangeImportJob j) =>
        new BulkLabRangeImportJobResponse(
            JobId: j.JobId,
            StudyId: j.StudyId,
            SiteId: j.SiteId,
            FileName: j.FileName,
            FileType: j.FileType,
            Status: j.Status,
            TotalRows: j.TotalRows,
            SuccessRows: j.SuccessRows,
            ErrorRows: j.ErrorRows,
            ErrorSummaryJson: j.ErrorSummaryJson,
            PreviewSummaryJson: j.PreviewSummaryJson,
            ReasonForChange: j.ReasonForChange,
            SubmittedBy: j.CreatedBy,
            SubmittedAt: j.SubmittedAt,
            CompletedAt: j.CompletedAt
        );
}
