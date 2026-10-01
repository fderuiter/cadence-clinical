using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Application.Exports;

public interface IExportJobService
{
    Task<ExportJobResponse> CreateJobAsync(ExportJobCreateRequest request, AuthenticatedCaller caller, CancellationToken ct = default);
    ExportJobResponse? GetJob(string jobId);
    IReadOnlyList<ExportJobResponse> ListJobs(string? studyId = null);
    ExportArtifact? GetArtifact(string jobId);
    Task StoreArtifactAndCompleteJobAsync(string jobId, ExportArtifact artifact, CancellationToken ct = default);
    Task FailJobAsync(string jobId, string errorDetails, CancellationToken ct = default);
    Task UpdateJobProgressAsync(string jobId, int percent, string stepText, CancellationToken ct = default);
}

public sealed class ExportJobService : IExportJobService
{
    private static readonly ConcurrentDictionary<string, ExportJob> Jobs = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ExportArtifact> Artifacts = new(StringComparer.Ordinal);

    private readonly IExportJobQueue _queue;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IClock _clock;

    public ExportJobService(IExportJobQueue queue, IAuditLogWriter auditLogWriter, IClock clock)
    {
        _queue = queue;
        _auditLogWriter = auditLogWriter;
        _clock = clock;
    }

    public async Task<ExportJobResponse> CreateJobAsync(ExportJobCreateRequest request, AuthenticatedCaller caller, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        string jobId = $"export_job_{Guid.NewGuid():N}";
        string userId = caller.UserId ?? "system.export";

        var job = new ExportJob
        {
            JobId = jobId,
            StudyId = request.StudyId,
            Format = request.Format,
            Domains = request.Domains ?? Array.Empty<string>(),
            Datasets = request.Datasets ?? Array.Empty<string>(),
            Sites = request.Sites ?? Array.Empty<string>(),
            Cohorts = request.Cohorts ?? Array.Empty<string>(),
            PrivacyProfile = request.PrivacyProfile ?? "SAFE_HARBOR",
            HmacSalt = request.HmacSalt ?? string.Empty,
            RecordLockFilter = request.RecordLockFilter ?? "all",
            IncludeAuditTrail = request.IncludeAuditTrail ?? true,
            IncludeUnmapped = request.IncludeUnmapped ?? true,
            Status = ExportJobStatus.Queued,
            ProgressPercent = 0,
            CurrentStepText = "Queued for background compilation",
            CreatedAt = now,
            ExpiresAt = now.AddHours(24),
            CreatedBy = userId,
        };

        Jobs[jobId] = job;

        await _auditLogWriter.WriteAsync(
            entityName: "ExportJob",
            recordId: jobId,
            action: "SUBMIT_EXPORT_JOB",
            userId: userId,
            changeReason: $"Export job submitted for study {request.StudyId} format {request.Format}",
            oldValues: null,
            newValues: new Dictionary<string, object?>
            {
                ["JobId"] = jobId,
                ["StudyId"] = request.StudyId,
                ["Format"] = request.Format,
                ["Status"] = job.Status.ToString()
            },
            ct: ct).ConfigureAwait(false);

        await _queue.EnqueueAsync(job, ct).ConfigureAwait(false);

        return ExportJobResponse.FromJob(job);
    }

    public ExportJobResponse? GetJob(string jobId)
    {
        return Jobs.TryGetValue(jobId, out var job) ? ExportJobResponse.FromJob(job) : null;
    }

    public IReadOnlyList<ExportJobResponse> ListJobs(string? studyId = null)
    {
        var list = Jobs.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(studyId))
        {
            list = list.Where(j => j.StudyId.Equals(studyId, StringComparison.OrdinalIgnoreCase));
        }
        return list.OrderByDescending(j => j.CreatedAt)
                   .Select(ExportJobResponse.FromJob)
                   .ToList();
    }

    public ExportArtifact? GetArtifact(string jobId)
    {
        if (Artifacts.TryGetValue(jobId, out var artifact))
        {
            if (_clock.UtcNow > artifact.ExpiresAt)
            {
                Artifacts.TryRemove(jobId, out _);
                return null;
            }
            return artifact;
        }
        return null;
    }

    public async Task StoreArtifactAndCompleteJobAsync(string jobId, ExportArtifact artifact, CancellationToken ct = default)
    {
        if (Jobs.TryGetValue(jobId, out var job))
        {
            var now = _clock.UtcNow;
            job.Status = ExportJobStatus.Completed;
            job.ProgressPercent = 100;
            job.CurrentStepText = "Export package verified and ready for download.";
            job.DownloadUrl = $"/api/v1/execution/exports/{jobId}/download";
            job.CompletedAt = now;
            job.ExpiresAt = artifact.ExpiresAt;

            Artifacts[jobId] = artifact;

            await _auditLogWriter.WriteAsync(
                entityName: "ExportJob",
                recordId: jobId,
                action: "COMPLETE_EXPORT_JOB",
                userId: job.CreatedBy,
                changeReason: $"Export job completed successfully for study {job.StudyId}",
                oldValues: new Dictionary<string, object?> { ["Status"] = "Processing" },
                newValues: new Dictionary<string, object?>
                {
                    ["Status"] = "Completed",
                    ["DownloadUrl"] = job.DownloadUrl,
                    ["CompletedAt"] = now
                },
                ct: ct).ConfigureAwait(false);
        }
    }

    public async Task FailJobAsync(string jobId, string errorDetails, CancellationToken ct = default)
    {
        if (Jobs.TryGetValue(jobId, out var job))
        {
            var now = _clock.UtcNow;
            job.Status = ExportJobStatus.Failed;
            job.ErrorDetails = errorDetails;
            job.CurrentStepText = "Export compilation failed";
            job.CompletedAt = now;

            await _auditLogWriter.WriteAsync(
                entityName: "ExportJob",
                recordId: jobId,
                action: "FAIL_EXPORT_JOB",
                userId: job.CreatedBy,
                changeReason: $"Export job failed: {errorDetails}",
                oldValues: new Dictionary<string, object?> { ["Status"] = "Processing" },
                newValues: new Dictionary<string, object?>
                {
                    ["Status"] = "Failed",
                    ["ErrorDetails"] = errorDetails
                },
                ct: ct).ConfigureAwait(false);
        }
    }

    public Task UpdateJobProgressAsync(string jobId, int percent, string stepText, CancellationToken ct = default)
    {
        if (Jobs.TryGetValue(jobId, out var job))
        {
            job.Status = ExportJobStatus.Processing;
            job.ProgressPercent = percent;
            job.CurrentStepText = stepText;
        }
        return Task.CompletedTask;
    }
}
