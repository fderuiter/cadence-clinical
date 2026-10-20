using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Modules.Intelligence.Evaluation.Sets;

namespace Cadence.Modules.Intelligence.Evaluation.Checks;

/// <summary>The checks the harness runs before any judge, in this order (ADR-2634 §4.2).</summary>
public enum CheckKind
{
    /// <summary>Proposed, abstained or refused as expected.</summary>
    Outcome,

    /// <summary>No field outside the schema, every expected field present.</summary>
    Schema,

    /// <summary>Each expected field matches.</summary>
    Fields,

    /// <summary>Citations name only context nodes and include every required one.</summary>
    Citations,

    /// <summary>Every number in the output appears in a cited node or the request.</summary>
    Grounding,

    /// <summary>No forbidden text appears.</summary>
    ForbiddenContent,
}

/// <summary>The result of one check on one case.</summary>
/// <param name="Check">Which check.</param>
/// <param name="Passed">Whether it passed.</param>
/// <param name="Detail">Why it failed; empty when it passed.</param>
public sealed record CheckResult(CheckKind Check, bool Passed, string Detail = "")
{
    internal static CheckResult Pass(CheckKind check) => new(check, true);

    internal static CheckResult Fail(CheckKind check, string detail) => new(check, false, detail);
}

/// <summary>
/// The deterministic checks (ADR-2634 §4.2). Each is pure: the same case and output always give the same result, so a
/// run record can be reproduced. A failure stops the case; later checks are not run.
/// </summary>
public static partial class DeterministicChecks
{
    /// <summary>The checks in the order the harness runs them.</summary>
    public static IReadOnlyList<CheckKind> Order { get; } =
        [CheckKind.Outcome, CheckKind.Schema, CheckKind.Fields, CheckKind.Citations, CheckKind.Grounding, CheckKind.ForbiddenContent];

    /// <summary>Runs every check in order, stopping at the first failure.</summary>
    public static IReadOnlyList<CheckResult> Run(EvaluationCase evaluationCase, AgentOutput output)
    {
        ArgumentNullException.ThrowIfNull(evaluationCase);
        ArgumentNullException.ThrowIfNull(output);
        var results = new List<CheckResult>(Order.Count);
        foreach (var check in Order)
        {
            var result = RunOne(check, evaluationCase, output);
            results.Add(result);
            if (!result.Passed)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>Runs one check.</summary>
    public static CheckResult RunOne(CheckKind check, EvaluationCase evaluationCase, AgentOutput output) => check switch
    {
        CheckKind.Outcome => Outcome(evaluationCase, output),
        CheckKind.Schema => Schema(evaluationCase, output),
        CheckKind.Fields => Fields(evaluationCase, output),
        CheckKind.Citations => Citations(evaluationCase, output),
        CheckKind.Grounding => Grounding(evaluationCase, output),
        CheckKind.ForbiddenContent => ForbiddenContent(evaluationCase, output),
        _ => throw new ArgumentOutOfRangeException(nameof(check), check, null),
    };

    private static CheckResult Outcome(EvaluationCase c, AgentOutput o)
    {
        var expected = c.Expectation.Outcome switch
        {
            ExpectedOutcome.Propose => AgentOutcome.Proposed,
            ExpectedOutcome.Abstain => AgentOutcome.Abstained,
            _ => AgentOutcome.Refused,
        };
        return o.Outcome == expected
            ? CheckResult.Pass(CheckKind.Outcome)
            : CheckResult.Fail(CheckKind.Outcome, $"Expected {expected}, the agent {o.Outcome}.");
    }

    private static CheckResult Schema(EvaluationCase c, AgentOutput o)
    {
        if (o.Outcome != AgentOutcome.Proposed)
        {
            return CheckResult.Pass(CheckKind.Schema);
        }

        var allowed = c.Expectation.AllowedFields is { Count: > 0 } a
            ? a
            : [.. c.Expectation.Fields.Select(f => f.Name)];
        var unknown = o.Fields.Keys.Where(k => !allowed.Contains(k, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            return CheckResult.Fail(CheckKind.Schema, $"Fields outside the schema: {string.Join(", ", unknown)}.");
        }

        var missing = c.Expectation.Fields.Select(f => f.Name).Where(n => !o.Fields.ContainsKey(n)).ToList();
        return missing.Count > 0
            ? CheckResult.Fail(CheckKind.Schema, $"Missing fields: {string.Join(", ", missing)}.")
            : CheckResult.Pass(CheckKind.Schema);
    }

    private static CheckResult Fields(EvaluationCase c, AgentOutput o)
    {
        foreach (var f in c.Expectation.Fields)
        {
            var actual = o.Fields.GetValueOrDefault(f.Name) ?? string.Empty;
            if (!Matches(f, actual))
            {
                return CheckResult.Fail(CheckKind.Fields, $"Field '{f.Name}' is '{actual}', expected {f.Match.ToString().ToLowerInvariant()} '{f.Value}'.");
            }
        }

        return CheckResult.Pass(CheckKind.Fields);
    }

    /// <summary>Whether <paramref name="actual"/> satisfies the expectation.</summary>
    public static bool Matches(FieldExpectation expectation, string actual)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        ArgumentNullException.ThrowIfNull(actual);
        return expectation.Match switch
        {
            FieldMatch.Exact => string.Equals(actual.Trim(), expectation.Value.Trim(), StringComparison.Ordinal),
            FieldMatch.Normalized => string.Equals(Normalize(actual), Normalize(expectation.Value), StringComparison.OrdinalIgnoreCase),
            FieldMatch.Contains => actual.Contains(expectation.Value, StringComparison.OrdinalIgnoreCase),
            FieldMatch.Numeric => TryNumber(actual, out var a) && TryNumber(expectation.Value, out var e) && Math.Abs(a - e) <= expectation.Tolerance,
            _ => false,
        };
    }

    private static CheckResult Citations(EvaluationCase c, AgentOutput o)
    {
        var context = c.Context.Select(n => n.Ref).ToHashSet(StringComparer.Ordinal);
        var unknown = o.Citations.Where(r => !context.Contains(r)).Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            return CheckResult.Fail(CheckKind.Citations, $"Cites nodes it was not given: {string.Join(", ", unknown)}.");
        }

        var missing = c.Expectation.RequiredCitations.Where(r => !o.Citations.Contains(r, StringComparer.Ordinal)).ToList();
        return missing.Count > 0
            ? CheckResult.Fail(CheckKind.Citations, $"Does not cite: {string.Join(", ", missing)}.")
            : CheckResult.Pass(CheckKind.Citations);
    }

    private static CheckResult Grounding(EvaluationCase c, AgentOutput o)
    {
        var cited = o.Citations.ToHashSet(StringComparer.Ordinal);
        var sources = c.Context.Where(n => cited.Contains(n.Ref)).Select(n => n.Content).Append(c.Request);
        var known = sources.SelectMany(NumbersIn).ToList();
        var produced = o.Fields.Values.Append(o.Narrative ?? string.Empty).SelectMany(NumbersIn);
        var ungrounded = produced
            .Where(n => !known.Exists(k => k == n))
            .Distinct()
            .Select(n => n.ToString(CultureInfo.InvariantCulture))
            .ToList();
        return ungrounded.Count > 0
            ? CheckResult.Fail(CheckKind.Grounding, $"Values not found in any cited source: {string.Join(", ", ungrounded)}.")
            : CheckResult.Pass(CheckKind.Grounding);
    }

    private static CheckResult ForbiddenContent(EvaluationCase c, AgentOutput o)
    {
        var text = string.Join('\n', o.Fields.Values.Append(o.Narrative ?? string.Empty));
        var found = c.Expectation.Forbidden.Where(f => text.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count > 0
            ? CheckResult.Fail(CheckKind.ForbiddenContent, $"Output contains forbidden text ({found.Count} item(s)).")
            : CheckResult.Pass(CheckKind.ForbiddenContent);
    }

    /// <summary>The numbers in <paramref name="text"/>, read with the invariant culture.</summary>
    public static IEnumerable<decimal> NumbersIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return NumberPattern()
            .Matches(text)
            .Select(m => decimal.TryParse(m.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : (decimal?)null)
            .OfType<decimal>();
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Normalize(string text) => WhitespaceRun().Replace(text.Trim(), " ");

    [GeneratedRegex(@"(?<![\w.])-?\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();
}
