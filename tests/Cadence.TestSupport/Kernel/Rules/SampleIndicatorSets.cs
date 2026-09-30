using System;
using System.Collections.Generic;
using System.Text.Json;

using Cadence.Kernel;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Indicators;
using Cadence.Rules.Packs;

namespace Cadence.TestSupport.Kernel.Rules;

/// <summary>A small Study Pulse indicator set (ADR-2488) and matching records, shared by the unit and integration tests.</summary>
internal static class SampleIndicatorSets
{
    /// <summary>The population the enrollment indicators count.</summary>
    public const string Enrollments = "enrollments";

    /// <summary>The population the quality indicator counts.</summary>
    public const string Findings = "findings";

    /// <summary>
    /// Three indicators: enrolled participants (a count with no threshold), the screen failure rate (a percentage that
    /// needs attention above 30%) and open error findings (a QTL breached above zero).
    /// </summary>
    public static IndicatorSet Pulse() => new(RuleAuthority.Sponsor, "Sample Study Pulse",
    [
        new Indicator("pulse.enrolled", 1, "Participants enrolled", IndicatorArea.Enrollment, IndicatorKind.Pulse, Enrollments,
            Expression("""{"op":"in","args":[{"var":"STATUS"},"Enrolled","Randomized"]}"""), IndicatorMeasure.Count, null,
            "Participants who are enrolled or randomized.", EffectivePeriod.Always),
        new Indicator("pulse.screen_failure_rate", 2, "Screen failure rate", IndicatorArea.Enrollment, IndicatorKind.Pulse, Enrollments,
            Expression("""{"op":"==","args":[{"var":"STATUS"},"ScreenFailed"]}"""), IndicatorMeasure.Percent,
            new IndicatorThreshold(ThresholdDirection.Above, 30m, ValidationSeverity.Warning),
            "Participants who failed screening, as a share of everyone screened.", EffectivePeriod.Always),
        new Indicator("qtl.open_error_findings", 1, "Open error findings", IndicatorArea.Quality, IndicatorKind.Qtl, Findings,
            Expression("""{"op":"and","args":[{"op":"==","args":[{"var":"STATUS"},"Open"]},{"op":"==","args":[{"var":"SEVERITY"},"Error"]}]}"""),
            IndicatorMeasure.Count, new IndicatorThreshold(ThresholdDirection.Above, 0m, ValidationSeverity.Error),
            "Validation findings of severity Error that are still open.", EffectivePeriod.Always),
    ]);

    /// <summary>One enrollment record per status, each standing for a new enrollment.</summary>
    public static IReadOnlyList<IndicatorRecord> EnrollmentRecords(params string[] statuses) =>
        [.. Array.ConvertAll(statuses, status => Record(NodeRef.For(EnrollmentId.New()), ("STATUS", RuleValue.FromText(status))))];

    /// <summary>A record for <paramref name="node"/> with the given variables.</summary>
    public static IndicatorRecord Record(NodeRef node, params (string Name, RuleValue Value)[] values)
    {
        var map = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            map[name] = value;
        }

        return new IndicatorRecord(node, map);
    }

    private static RuleExpression Expression(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RuleExpressionJson.Parse(document.RootElement);
    }
}
