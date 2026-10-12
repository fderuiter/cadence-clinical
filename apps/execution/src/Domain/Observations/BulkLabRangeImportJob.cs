using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Observations;

/// <summary>
/// Domain model for bulk laboratory reference range import jobs.
/// Records status, row metrics, GxP change reason, and Part 11 audit trails.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-121
/// </summary>
public sealed class BulkLabRangeImportJob : IGxPAuditable
{
    public Guid Id { get; private set; }
    public string JobId { get; private set; } = string.Empty;
    public string StudyId { get; private set; } = string.Empty;
    public string? SiteId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string FileType { get; private set; } = "CSV"; // "CSV" or "XLSX"
    public string Status { get; private set; } = "COMPLETED"; // "PENDING", "PROCESSING", "COMPLETED", "COMPLETED_WITH_ERRORS", "FAILED"
    public int TotalRows { get; private set; }
    public int SuccessRows { get; private set; }
    public int ErrorRows { get; private set; }
    public string? ErrorSummaryJson { get; private set; }
    public string? PreviewSummaryJson { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    // IGxPAuditable properties
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public long VersionIndex { get; set; } = 1;

    private BulkLabRangeImportJob() { } // EF Core constructor

    public BulkLabRangeImportJob(
        Guid id,
        string jobId,
        string studyId,
        string? siteId,
        string fileName,
        string fileType,
        string status,
        int totalRows,
        int successRows,
        int errorRows,
        string? errorSummaryJson,
        string? previewSummaryJson,
        string submittedBy,
        string reasonForChange,
        DateTimeOffset submittedAt,
        DateTimeOffset? completedAt = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        JobId = string.IsNullOrWhiteSpace(jobId) ? Id.ToString() : jobId.Trim();
        StudyId = studyId?.Trim() ?? string.Empty;
        SiteId = string.IsNullOrWhiteSpace(siteId) ? null : siteId.Trim();
        FileName = fileName?.Trim() ?? string.Empty;
        FileType = string.IsNullOrWhiteSpace(fileType) ? "CSV" : fileType.Trim().ToUpperInvariant();
        Status = string.IsNullOrWhiteSpace(status) ? "COMPLETED" : status.Trim().ToUpperInvariant();
        TotalRows = totalRows;
        SuccessRows = successRows;
        ErrorRows = errorRows;
        ErrorSummaryJson = errorSummaryJson;
        PreviewSummaryJson = previewSummaryJson;
        SubmittedAt = submittedAt;
        CompletedAt = completedAt ?? submittedAt;
        CreatedBy = submittedBy?.Trim() ?? string.Empty;
        ReasonForChange = reasonForChange?.Trim() ?? string.Empty;
        CreatedAt = submittedAt.UtcDateTime;
        VersionIndex = 1;
    }

    public void MarkCompleted(int successCount, int errorCount, string? errorSummaryJson, DateTimeOffset completedAt)
    {
        SuccessRows = successCount;
        ErrorRows = errorCount;
        ErrorSummaryJson = errorSummaryJson;
        CompletedAt = completedAt;
        if (errorCount > 0)
        {
            Status = successCount > 0 ? "COMPLETED_WITH_ERRORS" : "FAILED";
        }
        else
        {
            Status = "COMPLETED";
        }
        VersionIndex++;
    }
}
