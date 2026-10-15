using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Validation;

/// <summary>
/// Analyzes rule packs for mutually conflicting conditions across cross-form and field checks.
/// </summary>
public static class ConflictingConditionAnalyzer
{
    /// <summary>
    /// Analyzes the specified rule packs for mutually conflicting conditions.
    /// </summary>
    /// <param name="packs">The rule packs to analyze.</param>
    /// <returns>A list of conflicting rule pair descriptions.</returns>
    public static IReadOnlyList<string> Analyze(IEnumerable<RulePack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);

        var conflicts = new List<string>();
        var rules = packs.SelectMany(p => p.Rules).ToList();

        var byDataset = rules
            .Where(r => r.Applicability.Dataset is not null)
            .GroupBy(r => r.Applicability.Dataset!, StringComparer.Ordinal);

        foreach (var group in byDataset)
        {
            var datasetRules = group.ToList();
            for (var i = 0; i < datasetRules.Count; i++)
            {
                for (var j = i + 1; j < datasetRules.Count; j++)
                {
                    var r1 = datasetRules[i];
                    var r2 = datasetRules[j];

                    if (AreConflicting(r1.Condition, r2.Condition))
                    {
                        conflicts.Add($"Mutually conflicting rule conditions detected on dataset '{group.Key}': {r1.Id} (v{r1.Version}) vs {r2.Id} (v{r2.Version}).");
                    }
                }
            }
        }

        return conflicts;
    }

    private static bool AreConflicting(RuleExpression expr1, RuleExpression expr2)
    {
        return expr1 is ComparisonExpression comp1 &&
            expr2 is ComparisonExpression comp2 &&
            comp1.Left is VariableExpression v1 &&
            comp2.Left is VariableExpression v2 &&
            string.Equals(v1.Name, v2.Name, StringComparison.Ordinal) &&
            comp1.Right is LiteralExpression l1 &&
            comp2.Right is LiteralExpression l2 &&
            l1.Value.Equals(l2.Value) &&
            ((comp1.Operator == ComparisonOperator.Equal && comp2.Operator == ComparisonOperator.NotEqual) ||
             (comp1.Operator == ComparisonOperator.NotEqual && comp2.Operator == ComparisonOperator.Equal) ||
             (comp1.Operator == ComparisonOperator.Equal && comp2.Operator == ComparisonOperator.Equal));
    }
}
