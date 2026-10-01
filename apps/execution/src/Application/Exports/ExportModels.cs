using System;
using System.Collections.Generic;

using FluentValidation;

namespace Cadence.Apps.Execution.Application.Exports;

public enum ExportJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
}

public sealed class ExportJob
{
    public required string JobId { get; init; }
    public required string StudyId { get; init; }
    public required string Format { get; init; }
    public required IReadOnlyList<string> Domains { get; init; }
    public required IReadOnlyList<string> Datasets { get; init; }
    public required IReadOnlyList<string> Sites { get; init; }
    public required IReadOnlyList<string> Cohorts { get; init; }
    public required string PrivacyProfile { get; init; }
    public required string HmacSalt { get; init; }
    public required string RecordLockFilter { get; init; }
    public required bool IncludeAuditTrail { get; init; }
    public required bool IncludeUnmapped { get; init; }

    public ExportJobStatus Status { get; set; } = ExportJobStatus.Queued;
    public int ProgressPercent { get; set; } = 0;
    public string CurrentStepText { get; set; } = "Queued for background compilation";
    public string? DownloadUrl { get; set; }
    public string? ErrorDetails { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public required string CreatedBy { get; init; }
}

public sealed record ExportJobCreateRequest(
    string StudyId,
    string Format,
    IReadOnlyList<string>? Domains,
    IReadOnlyList<string>? Datasets,
    IReadOnlyList<string>? Sites,
    IReadOnlyList<string>? Cohorts,
    string? PrivacyProfile,
    string? HmacSalt,
    string? RecordLockFilter,
    bool? IncludeAuditTrail,
    bool? IncludeUnmapped);

public sealed class ExportJobCreateRequestValidator : AbstractValidator<ExportJobCreateRequest>
{
    public ExportJobCreateRequestValidator()
    {
        RuleFor(x => x.StudyId).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Format).NotEmpty().MaximumLength(50);
    }
}

public sealed record ExportJobResponse(
    string JobId,
    string StudyId,
    string Format,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Datasets,
    IReadOnlyList<string> Sites,
    IReadOnlyList<string> Cohorts,
    string PrivacyProfile,
    string RecordLockFilter,
    string Status,
    int ProgressPercent,
    string CurrentStepText,
    string? DownloadUrl,
    string? ErrorDetails,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset ExpiresAt,
    string CreatedBy)
{
    public static ExportJobResponse FromJob(ExportJob job) => new(
        job.JobId,
        job.StudyId,
        job.Format,
        job.Domains,
        job.Datasets,
        job.Sites,
        job.Cohorts,
        job.PrivacyProfile,
        job.RecordLockFilter,
        job.Status.ToString(),
        job.ProgressPercent,
        job.CurrentStepText,
        job.DownloadUrl,
        job.ErrorDetails,
        job.CreatedAt,
        job.CompletedAt,
        job.ExpiresAt,
        job.CreatedBy);
}

public sealed record ExportArtifact(
    string JobId,
    string FileName,
    string ContentType,
    byte[] Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
