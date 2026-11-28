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

        var conflicts = new SortedSet<string>(StringComparer.Ordinal);
        var rules = packs.SelectMany(p => p.Rules).ToList();

        for (var i = 0; i < rules.Count; i++)
        {
            for (var j = i + 1; j < rules.Count; j++)
            {
                var r1 = rules[i];
                var r2 = rules[j];
                if (!ApplicabilityOverlaps(r1, r2))
                {
                    continue;
                }

                var sharedDataset = r1.Applicability.Dataset ?? r2.Applicability.Dataset;
                if (AreConflicting(r1.Condition, r2.Condition, sharedDataset))
                {
                    var (first, second) = CompareRuleOrder(r1, r2) <= 0 ? (r1, r2) : (r2, r1);
                    var datasetLabel = sharedDataset ?? "*";
                    conflicts.Add($"Mutually conflicting rule conditions detected on dataset '{datasetLabel}': {first.Id} (v{first.Version}) vs {second.Id} (v{second.Version}).");
                }
            }
        }

        return [.. conflicts];
    }

    private static int CompareRuleOrder(Rule left, Rule right)
    {
        var idComparison = StringComparer.Ordinal.Compare(left.Id, right.Id);
        return idComparison != 0 ? idComparison : left.Version.CompareTo(right.Version);
    }

    private static bool ApplicabilityOverlaps(Rule r1, Rule r2) =>
        r1.Applicability.Stage == r2.Applicability.Stage
        && MatchesOptional(r1.Applicability.Standard, r2.Applicability.Standard)
        && MatchesOptional(r1.Applicability.StandardVersion, r2.Applicability.StandardVersion)
        && MatchesOptional(r1.Applicability.Agency, r2.Applicability.Agency)
        && MatchesOptional(r1.Applicability.Dataset, r2.Applicability.Dataset)
        && EffectiveOverlaps(r1.Effective, r2.Effective);

    private static bool MatchesOptional(string? first, string? second) =>
        first is null || second is null || string.Equals(first, second, StringComparison.Ordinal);

    private static bool EffectiveOverlaps(EffectivePeriod first, EffectivePeriod second)
    {
        var start = MaxDate(first.From, second.From);
        var end = MinDate(first.Until, second.Until);
        return start is null || end is null || start.Value < end.Value;
    }

    private static DateOnly? MaxDate(DateOnly? left, DateOnly? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left.Value >= right.Value ? left : right;
    }

    private static DateOnly? MinDate(DateOnly? left, DateOnly? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left.Value <= right.Value ? left : right;
    }

    private static bool AreConflicting(RuleExpression expr1, RuleExpression expr2, string? sharedDataset)
    {
        var candidates = ConditionDomainExtractor.CollectCandidateVariables(expr1, sharedDataset)
            .Intersect(ConditionDomainExtractor.CollectCandidateVariables(expr2, sharedDataset), StringComparer.Ordinal);

        foreach (var variable in candidates)
        {
            var kind = ConditionDomainExtractor.InferDomainKind(expr1, expr2, variable, sharedDataset);
            if (!ConditionDomainExtractor.TryExtractAllowedDomain(expr1, variable, sharedDataset, kind, out var allowed1)
                || !ConditionDomainExtractor.TryExtractAllowedDomain(expr2, variable, sharedDataset, kind, out var allowed2))
            {
                continue;
            }

            if (DomainsConflict(allowed1, allowed2))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DomainsConflict(ValueDomain first, ValueDomain second)
    {
        var intersected = first.Intersect(second);
        if (first.HasPopulatedValue && second.HasPopulatedValue)
        {
            return !intersected.HasPopulatedValue;
        }

        return intersected.IsCompletelyEmpty;
    }
}
