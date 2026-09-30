using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Evaluation;

/// <summary>
/// The in-process .NET engine behind <see cref="IRuleEvaluator"/>. Each pack's conditions are compiled to delegates
/// once and cached for the pack's lifetime; evaluation holds no other state, so one instance serves every caller.
/// </summary>
public sealed class DotNetRuleEvaluator : IRuleEvaluator
{
    private readonly ConditionalWeakTable<RulePack, CompiledRule[]> _compiled = [];

    /// <inheritdoc/>
    public IReadOnlyList<RuleViolation> Evaluate(RulePack pack, RuleEvaluationContext context, IEnumerable<RuleRecord> records)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(records);

        var applicable = _compiled.GetValue(pack, Compile).Where(r => Applies(r.Rule, context)).ToArray();
        var violations = new List<RuleViolation>();
        if (applicable.Length == 0)
        {
            return violations;
        }

        var byDataset = new Dictionary<string, CompiledRule[]>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (!byDataset.TryGetValue(record.Dataset, out var rules))
            {
                rules = [.. applicable.Where(r => r.Rule.Applicability.Dataset is null
                    || string.Equals(r.Rule.Applicability.Dataset, record.Dataset, StringComparison.Ordinal))];
                byDataset.Add(record.Dataset, rules);
            }

            foreach (var rule in rules)
            {
                if (rule.Condition(record).IsTrue)
                {
                    violations.Add(new RuleViolation(
                        rule.Rule.Id,
                        rule.Rule.Version,
                        rule.Rule.Severity,
                        record.Dataset,
                        record.Key,
                        rule.Rule.Explanation,
                        [.. rule.Variables.Select(v => new ObservedValue(v, record[v]))]));
                }
            }
        }

        return violations;
    }

    private static bool Applies(Rule rule, RuleEvaluationContext context) =>
        rule.Applicability.Stage == context.Stage
        && rule.Effective.Contains(context.AsOf)
        && Matches(rule.Applicability.Standard, context.Standard)
        && Matches(rule.Applicability.StandardVersion, context.StandardVersion)
        && Matches(rule.Applicability.Agency, context.Agency);

    private static bool Matches(string? required, string? actual) =>
        required is null || string.Equals(required, actual, StringComparison.Ordinal);

    private static CompiledRule[] Compile(RulePack pack) =>
        [.. pack.Rules.Select(r => new CompiledRule(r, RuleExpressionCompiler.Compile(r.Condition), RuleExpressionJson.Variables(r.Condition)))];

    private sealed record CompiledRule(Rule Rule, Func<IRuleVariables, RuleValue> Condition, IReadOnlyList<string> Variables);
}
