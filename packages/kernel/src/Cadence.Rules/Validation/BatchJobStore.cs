using System;
using System.Collections.Concurrent;

namespace Cadence.Rules.Validation;

/// <summary>
/// Thread-safe in-memory store for tracking asynchronous batch validation jobs.
/// </summary>
public sealed class BatchJobStore
{
    private readonly ConcurrentDictionary<string, BatchValidationReport> _jobs = new(StringComparer.Ordinal);

    /// <summary>
    /// Stores or updates a batch validation report.
    /// </summary>
    /// <param name="report">The report to store.</param>
    public void StoreJob(BatchValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _jobs[report.JobId] = report;
    }

    /// <summary>
    /// Retrieves a batch validation report by job ID.
    /// </summary>
    /// <param name="jobId">The unique job ID.</param>
    /// <returns>The report if found; otherwise null.</returns>
    public BatchValidationReport? GetJob(string jobId)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return _jobs.TryGetValue(jobId, out var report) ? report : null;
    }
}
