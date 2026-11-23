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

        var (baselineViolations, targetViolations) = EvaluateStageDelta(
            evaluator,
            stage,
            asOf,
            [baselinePack.Pack],
            records,
            [targetPack.Pack],
            records);

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

    /// <summary>
    /// Pure multi-pack stage evaluation shared by <see cref="CompareAsync"/> and bitemporal simulation
    /// (<c>K-SIM-2</c>, <c>K-RULE-4</c>): evaluates baseline and target rule packs over their respective records at
    /// <paramref name="asOf"/> without clock or storage side effects.
    /// </summary>
    public static (IReadOnlyList<RuleViolation> BaselineViolations, IReadOnlyList<RuleViolation> TargetViolations) EvaluateStageDelta(
        IRuleEvaluator evaluator,
        ValidationStage stage,
        DateOnly asOf,
        IReadOnlyList<RulePack> baselinePacks,
        IReadOnlyList<RuleRecord> baselineRecords,
        IReadOnlyList<RulePack> targetPacks,
        IReadOnlyList<RuleRecord> targetRecords,
        IReadOnlyList<(string Standard, string Version)>? declaredStandards = null)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(baselinePacks);
        ArgumentNullException.ThrowIfNull(baselineRecords);
        ArgumentNullException.ThrowIfNull(targetPacks);
        ArgumentNullException.ThrowIfNull(targetRecords);

        IReadOnlyList<RuleEvaluationContext> contexts = declaredStandards is { Count: > 0 }
            ? [.. declaredStandards.Select(s => new RuleEvaluationContext(stage, asOf)
            {
                Standard = s.Standard,
                StandardVersion = s.Version,
            })]
            : [new RuleEvaluationContext(stage, asOf)];

        return (
            EvaluatePacksAcrossContexts(evaluator, contexts, baselinePacks, baselineRecords),
            EvaluatePacksAcrossContexts(evaluator, contexts, targetPacks, targetRecords));
    }

    private static IReadOnlyList<RuleViolation> EvaluatePacksAcrossContexts(
        IRuleEvaluator evaluator,
        IReadOnlyList<RuleEvaluationContext> contexts,
        IReadOnlyList<RulePack> packs,
        IReadOnlyList<RuleRecord> records)
    {
        if (packs.Count == 0 || records.Count == 0)
        {
            return [];
        }

        var violations = new List<RuleViolation>();
        var seen = new HashSet<(string RuleId, int RuleVersion, string Dataset, string RecordKey)>();
        foreach (var context in contexts)
        {
            foreach (var pack in packs)
            {
                foreach (var violation in evaluator.Evaluate(pack, context, records))
                {
                    if (seen.Add((violation.RuleId, violation.RuleVersion, violation.Dataset, violation.RecordKey)))
                    {
                        violations.Add(violation);
                    }
                }
            }
        }

        return violations;
    }

    private static string KeyOf(ValidationDetection d) =>
        $"{d.Target.Dataset}|{d.Target.Key}|{d.RuleId}";
}
