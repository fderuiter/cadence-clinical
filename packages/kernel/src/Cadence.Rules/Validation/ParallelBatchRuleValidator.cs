using System;
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

        var effectivePacks = ResolveEffectivePacks(request);
        var packs = effectivePacks.Select(p => p.Pack).ToList();
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

            _ = Task.Run(() => ExecuteBatchAsync(jobId, request, effectivePacks, records, startedAt, syntaxErrors, circularDeps, conflictingRules, CancellationToken.None), CancellationToken.None);

            return pendingReport;
        }

        return await ExecuteBatchAsync(jobId, request, effectivePacks, records, startedAt, syntaxErrors, circularDeps, conflictingRules, cancellationToken).ConfigureAwait(false);
    }

    private static List<(string PackKey, RulePack Pack)> ResolveEffectivePacks(BatchValidationRequest request)
    {
        if (request.LoadedRulePacks is { Count: > 0 } loaded)
        {
            return [.. loaded.Select(lp => (lp.Key, lp.Pack))];
        }

        if (request.LoadedRulePacks is not null && (request.RulePacks is null || request.RulePacks.Count == 0))
        {
            return [];
        }

        var rawPacks = request.RulePacks ?? [];
        return [.. rawPacks.Select(p => (p.Title, p))];
    }

    private async Task<BatchValidationReport> ExecuteBatchAsync(
        string jobId,
        BatchValidationRequest request,
        IReadOnlyList<(string PackKey, RulePack Pack)> effectivePacks,
        IReadOnlyList<RuleRecord> records,
        DateTimeOffset startedAt,
        List<string> syntaxErrors,
        IReadOnlyList<string> circularDeps,
        IReadOnlyList<string> conflictingRules,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var context = new RuleEvaluationContext(request.Stage, request.AsOf)
        {
            Standard = request.Standard,
            StandardVersion = request.StandardVersion,
            Agency = request.Agency,
        };

        var parallelWorkers = Math.Max(1, Environment.ProcessorCount);
        var totalRules = effectivePacks.Sum(p => p.Pack.Rules.Count);
        var detections = new List<ValidationDetection>();

        if (records.Count > 0 && effectivePacks.Count > 0)
        {
            var sliceSize = Math.Max(1, (records.Count + parallelWorkers - 1) / parallelWorkers);
            var slices = new List<IReadOnlyList<RuleRecord>>();
            for (var offset = 0; offset < records.Count; offset += sliceSize)
            {
                var count = Math.Min(sliceSize, records.Count - offset);
                var slice = new RuleRecord[count];
                for (var i = 0; i < count; i++)
                {
                    slice[i] = records[offset + i];
                }

                slices.Add(slice);
            }

            var sliceCount = slices.Count;
            var totalSlots = effectivePacks.Count * sliceCount;
            var buffers = new IReadOnlyList<ValidationDetection>[totalSlots];

            await Parallel.ForEachAsync(
                Enumerable.Range(0, totalSlots),
                new ParallelOptions { MaxDegreeOfParallelism = parallelWorkers, CancellationToken = cancellationToken },
                (slot, _) =>
                {
                    var packIndex = slot / sliceCount;
                    var sliceIndex = slot % sliceCount;
                    var (packKey, pack) = effectivePacks[packIndex];
                    var violations = evaluator.Evaluate(pack, context, slices[sliceIndex]);
                    if (violations.Count == 0)
                    {
                        buffers[slot] = [];
                        return ValueTask.CompletedTask;
                    }

                    var slotDetections = new ValidationDetection[violations.Count];
                    for (var i = 0; i < violations.Count; i++)
                    {
                        slotDetections[i] = ValidationRunner.Detection(packKey, FindingTargetKind.DatasetRecord, violations[i]);
                    }

                    buffers[slot] = slotDetections;
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);

            foreach (var slotBuffer in buffers)
            {
                if (slotBuffer is { Count: > 0 })
                {
                    detections.AddRange(slotBuffer);
                }
            }
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
            detections);

        jobStore.StoreJob(report);
        return report;
    }
}
