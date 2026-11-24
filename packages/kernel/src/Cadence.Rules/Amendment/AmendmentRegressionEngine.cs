using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Standards;
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
    /// Builds a dataset-to-variables schema lookup from registered <see cref="StandardPackage"/> definitions.
    /// </summary>
    /// <param name="packages">The standard packages whose dataset schemas are combined.</param>
    /// <returns>A dictionary mapping each dataset name to its declared variable names.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildDatasetSchemas(IEnumerable<StandardPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return BuildDatasetSchemas(packages.SelectMany(p => p.Datasets));
    }

    /// <summary>
    /// Builds a dataset-to-variables schema lookup from <see cref="DatasetDefinitionData"/> definitions.
    /// </summary>
    /// <param name="datasets">The dataset definitions.</param>
    /// <returns>A dictionary mapping each dataset name to its declared variable names.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildDatasetSchemas(IEnumerable<DatasetDefinitionData> datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var ds in datasets)
        {
            if (!map.TryGetValue(ds.Name, out var vars))
            {
                vars = new HashSet<string>(StringComparer.Ordinal);
                map[ds.Name] = vars;
            }

            foreach (var variable in ds.Variables)
            {
                vars.Add(variable.Name);
            }
        }

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)[.. kv.Value.Order(StringComparer.Ordinal)], StringComparer.Ordinal);
    }

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
    public Task<AmendmentRegressionReport> CompareAsync(
        LoadedRulePack baselinePack,
        LoadedRulePack targetPack,
        ValidationStage stage,
        DateOnly asOf,
        IReadOnlyList<RuleRecord> records,
        CancellationToken cancellationToken) =>
        CompareAsync(baselinePack, targetPack, stage, asOf, records, datasetSchemas: null, cancellationToken);

    /// <summary>
    /// Executes protocol amendment regression testing between a baseline and target rule pack version, optionally validating AST variable references against explicit dataset schemas.
    /// </summary>
    /// <param name="baselinePack">The baseline rule pack.</param>
    /// <param name="targetPack">The target rule pack after amendment.</param>
    /// <param name="stage">Validation stage.</param>
    /// <param name="asOf">Effective date.</param>
    /// <param name="records">Records dataset for evaluation.</param>
    /// <param name="datasetSchemas">Optional authoritative dataset-to-variables schema map; when null, falls back to variables observed in non-empty <paramref name="records"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An amendment regression report.</returns>
    public async Task<AmendmentRegressionReport> CompareAsync(
        LoadedRulePack baselinePack,
        LoadedRulePack targetPack,
        ValidationStage stage,
        DateOnly asOf,
        IReadOnlyList<RuleRecord> records,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? datasetSchemas = null,
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
            .Where(kv => baselineRules.TryGetValue(kv.Key, out var b) && IsRuleModified(b, kv.Value))
            .Select(kv => kv.Value)
            .ToList();

        var diffSummary = new RuleDiffSummary(addedRules, removedRules, modifiedRules);
        var brokenRefs = FindBrokenAstReferences(targetPack.Pack.Rules, records, datasetSchemas);

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
    /// Pure multi-pack stage evaluation shared by <c>CompareAsync</c> and bitemporal simulation
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

    private static bool IsRuleModified(Rule baseline, Rule target) =>
        baseline.Version != target.Version
        || baseline.Authority != target.Authority
        || baseline.Citation != target.Citation
        || baseline.Applicability != target.Applicability
        || baseline.Severity != target.Severity
        || !string.Equals(baseline.Condition.ToString(), target.Condition.ToString(), StringComparison.Ordinal)
        || !string.Equals(baseline.Explanation, target.Explanation, StringComparison.Ordinal)
        || !string.Equals(baseline.RemediationGuidance, target.RemediationGuidance, StringComparison.Ordinal)
        || baseline.Effective != target.Effective;

    private static List<BrokenAstReference> FindBrokenAstReferences(
        IReadOnlyList<Rule> targetRules,
        IReadOnlyList<RuleRecord> records,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? datasetSchemas)
    {
        var brokenRefs = new List<BrokenAstReference>();
        if (datasetSchemas is not null)
        {
            var schemaByDataset = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var allSchemaVariables = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (dataset, variables) in datasetSchemas)
            {
                var set = new HashSet<string>(variables, StringComparer.Ordinal);
                schemaByDataset[dataset] = set;
                allSchemaVariables.UnionWith(set);
            }

            foreach (var rule in targetRules)
            {
                foreach (var variable in RuleExpressionJson.Variables(rule.Condition))
                {
                    if (!IsKnownSchemaVariable(rule.Applicability.Dataset, variable, schemaByDataset, allSchemaVariables))
                    {
                        brokenRefs.Add(new BrokenAstReference(
                            rule.Id,
                            rule.Version,
                            variable,
                            $"Referenced variable '{variable}' was not found in dataset schema."));
                    }
                }
            }

            return brokenRefs;
        }

        if (records.Count == 0)
        {
            return brokenRefs;
        }

        var knownVariables = records.SelectMany(r => r.Values.Keys).ToHashSet(StringComparer.Ordinal);
        foreach (var rule in targetRules)
        {
            foreach (var variable in RuleExpressionJson.Variables(rule.Condition))
            {
                if (!knownVariables.Contains(variable))
                {
                    brokenRefs.Add(new BrokenAstReference(
                        rule.Id,
                        rule.Version,
                        variable,
                        $"Referenced variable '{variable}' was not found in dataset schema."));
                }
            }
        }

        return brokenRefs;
    }

    private static bool IsKnownSchemaVariable(
        string? ruleDataset,
        string referencedVariable,
        Dictionary<string, HashSet<string>> schemaByDataset,
        HashSet<string> allSchemaVariables)
    {
        if (ruleDataset is not null)
        {
            if (schemaByDataset.TryGetValue(ruleDataset, out var datasetVariables) && datasetVariables.Contains(referencedVariable))
            {
                return true;
            }
        }
        else if (allSchemaVariables.Contains(referencedVariable))
        {
            return true;
        }

        if (!referencedVariable.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var (datasetName, variables) in schemaByDataset)
        {
            if (referencedVariable.Length > datasetName.Length + 1
                && referencedVariable.StartsWith(datasetName, StringComparison.Ordinal)
                && referencedVariable[datasetName.Length] == '.'
                && variables.Contains(referencedVariable[(datasetName.Length + 1)..]))
            {
                return true;
            }
        }

        return false;
    }
}
