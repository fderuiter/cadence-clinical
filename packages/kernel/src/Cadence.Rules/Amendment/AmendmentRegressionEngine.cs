using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Rules.Validation;

namespace Cadence.Rules.Amendment;

/// <summary>
/// Protocol amendment regression suite that compares two rule pack versions and highlights diffs, broken AST references, new violations, and resolved findings.
/// </summary>
public sealed class AmendmentRegressionEngine(IRuleEvaluator evaluator, IClock clock)
{
    /// <summary>
    /// Executes protocol amendment regression testing between a baseline and target rule pack version.
    /// </summary>
    /// <param name="baselinePack">The baseline rule pack.</param>
    /// <param name="targetPack">The target rule pack after amendment.</param>
    /// <param name="stage">Validation stage.</param>
    /// <param name="asOf">Effective date.</param>
    /// <param name="records">Records dataset for evaluation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An amendment regression report.</returns>
    public async Task<AmendmentRegressionReport> CompareAsync(
        LoadedRulePack baselinePack,
        LoadedRulePack targetPack,
        ValidationStage stage,
        DateOnly asOf,
        IReadOnlyList<RuleRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baselinePack);
        ArgumentNullException.ThrowIfNull(targetPack);
        ArgumentNullException.ThrowIfNull(records);

        var startedAt = clock.UtcNow;
        var sw = Stopwatch.StartNew();

        var baselineRules = baselinePack.Pack.Rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var targetRules = targetPack.Pack.Rules.ToDictionary(r => r.Id, StringComparer.Ordinal);

        var addedRules = targetRules.Where(kv => !baselineRules.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        var removedRules = baselineRules.Where(kv => !targetRules.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        var modifiedRules = targetRules
            .Where(kv => baselineRules.TryGetValue(kv.Key, out var b) && (b.Version != kv.Value.Version || !string.Equals(b.Condition.ToString(), kv.Value.Condition.ToString(), StringComparison.Ordinal)))
            .Select(kv => kv.Value)
            .ToList();

        var diffSummary = new RuleDiffSummary(addedRules, removedRules, modifiedRules);

        var knownVariables = records.SelectMany(r => r.Values.Keys).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var brokenRefs = new List<BrokenAstReference>();

        foreach (var rule in targetPack.Pack.Rules)
        {
            var vars = RuleExpressionJson.Variables(rule.Condition);
            foreach (var v in vars)
            {
                if (knownVariables.Count > 0 && !knownVariables.Contains(v))
                {
                    brokenRefs.Add(new BrokenAstReference(
                        rule.Id,
                        rule.Version,
                        v,
                        $"Variable '{v}' referenced in rule '{rule.Id}' condition is not present in target dataset schema."));
                }
            }
        }

        var context = new RuleEvaluationContext(stage, asOf);

        var baselineViolations = evaluator.Evaluate(baselinePack.Pack, context, records);
        var targetViolations = evaluator.Evaluate(targetPack.Pack, context, records);

        var baselineDetections = baselineViolations.Select(v => ValidationRunner.Detection(baselinePack.Key, FindingTargetKind.DatasetRecord, v)).ToList();
        var targetDetections = targetViolations.Select(v => ValidationRunner.Detection(targetPack.Key, FindingTargetKind.DatasetRecord, v)).ToList();

        var baselineKeys = baselineDetections.Select(KeyOf).ToHashSet(StringComparer.Ordinal);
        var targetKeys = targetDetections.Select(KeyOf).ToHashSet(StringComparer.Ordinal);

        var newViolations = targetDetections.Where(d => !baselineKeys.Contains(KeyOf(d))).ToList();
        var resolvedFindings = baselineDetections.Where(d => !targetKeys.Contains(KeyOf(d))).ToList();

        sw.Stop();
        var completedAt = clock.UtcNow;

        var timing = new ValidationTimingMetrics(
            startedAt,
            completedAt,
            sw.Elapsed.TotalMilliseconds,
            RulesEvaluated: baselinePack.Pack.Rules.Count + targetPack.Pack.Rules.Count,
            RecordsEvaluated: records.Count,
            ParallelWorkerCount: Environment.ProcessorCount);

        return await Task.FromResult(new AmendmentRegressionReport(
            $"v{baselinePack.Version}",
            $"v{targetPack.Version}",
            diffSummary,
            brokenRefs,
            newViolations,
            resolvedFindings,
            timing)).ConfigureAwait(false);
    }

    private static string KeyOf(ValidationDetection d) =>
        $"{d.Target.Dataset}|{d.Target.Key}|{d.RuleId}";
}
