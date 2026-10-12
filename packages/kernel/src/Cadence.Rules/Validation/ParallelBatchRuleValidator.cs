using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Validation;

/// <summary>
/// Provides study-wide parallel batch compilation and validation across pinned rule packs and datasets.
/// </summary>
public sealed class ParallelBatchRuleValidator(
    IRuleEvaluator evaluator,
    BatchJobStore jobStore,
    IClock clock)
{
    /// <summary>
    /// Compiles and validates all rules across the requested study dataset in parallel.
    /// </summary>
    /// <param name="request">The batch validation request parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An aggregated diagnostic report.</returns>
    public async Task<BatchValidationReport> ValidateAsync(
        BatchValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jobId = $"job_{Guid.NewGuid():N}";
        var startedAt = clock.UtcNow;

        var packs = request.RulePacks ?? [];
        var records = request.Records ?? [];

        var isAsync = records.Count > request.AsyncThreshold;

        var syntaxErrors = new List<string>();
        var circularDeps = CircularDependencyAnalyzer.Analyze(packs);
        var conflictingRules = ConflictingConditionAnalyzer.Analyze(packs);

        if (isAsync)
        {
            var pendingReport = new BatchValidationReport(
                jobId,
                request.Study,
                IsAsync: true,
                Status: "Processing",
                Timing: new ValidationTimingMetrics(startedAt, startedAt, 0, packs.Sum(p => p.Rules.Count), records.Count, Environment.ProcessorCount),
                syntaxErrors,
                circularDeps,
                conflictingRules,
                Detections: []);

            jobStore.StoreJob(pendingReport);

            _ = Task.Run(() => ExecuteBatchAsync(jobId, request, packs, records, startedAt, syntaxErrors, circularDeps, conflictingRules, CancellationToken.None), CancellationToken.None);

            return pendingReport;
        }

        return await ExecuteBatchAsync(jobId, request, packs, records, startedAt, syntaxErrors, circularDeps, conflictingRules, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BatchValidationReport> ExecuteBatchAsync(
        string jobId,
        BatchValidationRequest request,
        IReadOnlyList<RulePack> packs,
        IReadOnlyList<RuleRecord> records,
        DateTimeOffset startedAt,
        List<string> syntaxErrors,
        IReadOnlyList<string> circularDeps,
        IReadOnlyList<string> conflictingRules,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var detections = new ConcurrentBag<ValidationDetection>();
        var context = new RuleEvaluationContext(request.Stage, request.AsOf)
        {
            Standard = request.Standard,
            StandardVersion = request.StandardVersion,
            Agency = request.Agency,
        };

        var parallelWorkers = Math.Max(1, Environment.ProcessorCount);
        var totalRules = packs.Sum(p => p.Rules.Count);

        var datasets = records.GroupBy(r => r.Dataset, StringComparer.Ordinal).ToList();

        if (datasets.Count > 0)
        {
            await Parallel.ForEachAsync(datasets, new ParallelOptions { MaxDegreeOfParallelism = parallelWorkers, CancellationToken = cancellationToken }, (datasetGroup, ct) =>
            {
                var datasetRecords = datasetGroup.ToList();
                foreach (var pack in packs)
                {
                    var violations = evaluator.Evaluate(pack, context, datasetRecords);
                    foreach (var v in violations)
                    {
                        detections.Add(ValidationRunner.Detection(pack.Title, FindingTargetKind.DatasetRecord, v));
                    }
                }
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        }

        sw.Stop();
        var completedAt = clock.UtcNow;
        var durationMs = sw.Elapsed.TotalMilliseconds;

        var timing = new ValidationTimingMetrics(
            startedAt,
            completedAt,
            durationMs,
            RulesEvaluated: totalRules,
            RecordsEvaluated: records.Count,
            ParallelWorkerCount: parallelWorkers);

        var report = new BatchValidationReport(
            jobId,
            request.Study,
            IsAsync: records.Count > request.AsyncThreshold,
            Status: "Completed",
            timing,
            syntaxErrors,
            circularDeps,
            conflictingRules,
            [.. detections]);

        jobStore.StoreJob(report);
        return report;
    }
}
