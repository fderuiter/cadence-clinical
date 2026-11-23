using System;
using System.Collections.Generic;
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
    private static readonly ConditionalWeakTable<RulePack, CompiledRule[]>.CreateValueCallback CompilePack = Compile;
    private readonly ConditionalWeakTable<RulePack, CompiledRule[]> _compiled = [];

    /// <inheritdoc/>
    public IReadOnlyList<RuleViolation> Evaluate(RulePack pack, RuleEvaluationContext context, IEnumerable<RuleRecord> records)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(records);

        var applicable = FilterApplicable(_compiled.GetValue(pack, CompilePack), context);
        if (applicable.Length == 0)
        {
            return [];
        }

        var violations = new List<RuleViolation>();
        var byDataset = new Dictionary<string, CompiledRule[]>(StringComparer.Ordinal);
        if (records is IList<RuleRecord> recordList)
        {
            for (var i = 0; i < recordList.Count; i++)
            {
                EvaluateRecord(recordList[i], applicable, byDataset, violations);
            }
        }
        else
        {
            foreach (var record in records)
            {
                EvaluateRecord(record, applicable, byDataset, violations);
            }
        }

        return violations;
    }

    private static void EvaluateRecord(
        RuleRecord record,
        CompiledRule[] applicable,
        Dictionary<string, CompiledRule[]> byDataset,
        List<RuleViolation> violations)
    {
        if (!byDataset.TryGetValue(record.Dataset, out var rules))
        {
            rules = FilterByDataset(applicable, record.Dataset);
            byDataset.Add(record.Dataset, rules);
        }

        for (var i = 0; i < rules.Length; i++)
        {
            var rule = rules[i];
            if (rule.Condition(record).IsTrue)
            {
                violations.Add(CreateViolation(rule, record));
            }
        }
    }

    private static RuleViolation CreateViolation(CompiledRule rule, RuleRecord record)
    {
        var variables = rule.Variables;
        ObservedValue[] observed;
        if (variables.Length == 0)
        {
            observed = [];
        }
        else
        {
            observed = new ObservedValue[variables.Length];
            for (var i = 0; i < variables.Length; i++)
            {
                var variable = variables[i];
                observed[i] = new ObservedValue(variable, record[variable]);
            }
        }

        return new RuleViolation(
            rule.Rule.Id,
            rule.Rule.Version,
            rule.Rule.Severity,
            record.Dataset,
            record.Key,
            rule.Rule.Explanation,
            observed);
    }

    private static CompiledRule[] FilterApplicable(CompiledRule[] compiled, RuleEvaluationContext context)
    {
        var count = 0;
        for (var i = 0; i < compiled.Length; i++)
        {
            if (Applies(compiled[i].Rule, context))
            {
                count++;
            }
        }

        if (count == 0)
        {
            return [];
        }

        if (count == compiled.Length)
        {
            return compiled;
        }

        var applicable = new CompiledRule[count];
        var index = 0;
        for (var i = 0; i < compiled.Length; i++)
        {
            var candidate = compiled[i];
            if (Applies(candidate.Rule, context))
            {
                applicable[index++] = candidate;
            }
        }

        return applicable;
    }

    private static CompiledRule[] FilterByDataset(CompiledRule[] applicable, string dataset)
    {
        var count = 0;
        for (var i = 0; i < applicable.Length; i++)
        {
            if (MatchesDataset(applicable[i].Rule, dataset))
            {
                count++;
            }
        }

        if (count == 0)
        {
            return [];
        }

        if (count == applicable.Length)
        {
            return applicable;
        }

        var filtered = new CompiledRule[count];
        var index = 0;
        for (var i = 0; i < applicable.Length; i++)
        {
            var candidate = applicable[i];
            if (MatchesDataset(candidate.Rule, dataset))
            {
                filtered[index++] = candidate;
            }
        }

        return filtered;
    }

    private static bool MatchesDataset(Rule rule, string dataset) =>
        rule.Applicability.Dataset is null || string.Equals(rule.Applicability.Dataset, dataset, StringComparison.Ordinal);

    private static bool Applies(Rule rule, RuleEvaluationContext context) =>
        rule.Applicability.Stage == context.Stage
        && rule.Effective.Contains(context.AsOf)
        && Matches(rule.Applicability.Standard, context.Standard)
        && Matches(rule.Applicability.StandardVersion, context.StandardVersion)
        && Matches(rule.Applicability.Agency, context.Agency);

    private static bool Matches(string? required, string? actual) =>
        required is null || string.Equals(required, actual, StringComparison.Ordinal);

    private static CompiledRule[] Compile(RulePack pack)
    {
        var rules = pack.Rules;
        if (rules.Count == 0)
        {
            return [];
        }

        var compiled = new CompiledRule[rules.Count];
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            compiled[i] = new CompiledRule(
                rule,
                RuleExpressionCompiler.Compile(rule.Condition),
                ToArray(RuleExpressionJson.Variables(rule.Condition)));
        }

        return compiled;
    }

    private static string[] ToArray(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var result = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            result[i] = items[i];
        }

        return result;
    }

    private sealed record CompiledRule(Rule Rule, Func<IRuleVariables, RuleValue> Condition, string[] Variables);
}
