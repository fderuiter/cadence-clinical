using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Observations;

namespace Cadence.Apps.Execution.Infrastructure.Normalization;

/// <summary>
/// Reference range selection and clinical indicator evaluation engine conforming to PRD-SYS-121.
/// Matches age- and sex-stratified normal intervals and computes out-of-range indicators.
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public sealed class LabRangeService : ILabRangeService
{
    private sealed record RangeRule(double Low, double High, string? Sex = null);

    private static readonly FrozenDictionary<string, RangeRule[]> StandardCatalog = new Dictionary<string, RangeRule[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["GLUC"] = [new(70.0, 99.0)],
        ["LB_GLUC"] = [new(70.0, 99.0)],
        ["SYSBP"] = [new(90.0, 120.0)],
        ["VSSBP"] = [new(90.0, 120.0)],
        ["DIABP"] = [new(60.0, 80.0)],
        ["VSDPB"] = [new(60.0, 80.0)],
        ["HR"] = [new(60.0, 100.0)],
        ["PULSE"] = [new(60.0, 100.0)],
        ["VSHR"] = [new(60.0, 100.0)],
        ["TEMP"] = [new(36.5, 37.5)],
        ["VSTEMP"] = [new(36.5, 37.5)],
        ["ALT"] = [new(10.0, 40.0, "M"), new(7.0, 35.0, "F"), new(10.0, 40.0)],
        ["LB_ALT"] = [new(10.0, 40.0, "M"), new(7.0, 35.0, "F"), new(10.0, 40.0)],
        ["AST"] = [new(10.0, 40.0, "M"), new(9.0, 32.0, "F"), new(10.0, 40.0)],
        ["LB_AST"] = [new(10.0, 40.0, "M"), new(9.0, 32.0, "F"), new(10.0, 40.0)],
        ["WBC"] = [new(4.0, 11.0)],
        ["LB_WBC"] = [new(4.0, 11.0)],
        ["K"] = [new(3.5, 5.0)],
        ["LB_K"] = [new(3.5, 5.0)],
        ["BILI"] = [new(5.1, 20.5)],
        ["WT"] = [new(40.0, 150.0)],
        ["HT"] = [new(1.2, 2.2)],
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public LabAssessmentResult EvaluateRange(
        string testCode,
        double? value,
        int? ageYears = null,
        string? sex = null,
        string? labSource = null,
        bool isNormalized = true) =>
        Evaluate(new TestCode(testCode), value, sex, ageYears, labSource, isNormalized);

    public LabAssessmentResult Evaluate(
        TestCode testCode,
        double? value,
        string? sex = null,
        int? ageYears = null,
        string? labSource = null,
        bool isNormalized = true)
    {
        if (!isNormalized)
        {
            return new LabAssessmentResult("UNNORMALIZED_UNIT", false, null, null, null);
        }

        if (value is null)
        {
            return new LabAssessmentResult("NORMAL", false, null, null, null);
        }

        if (!StandardCatalog.TryGetValue(testCode.Value, out var rules) || rules.Length == 0)
        {
            return new LabAssessmentResult("NORMAL", false, null, null, null);
        }

        string normSex = string.IsNullOrWhiteSpace(sex) ? "U" : sex.Trim().ToUpperInvariant();
        RangeRule? selectedRule = null;

        foreach (var rule in rules)
        {
            if (rule.Sex != null && string.Equals(rule.Sex, normSex, StringComparison.OrdinalIgnoreCase))
            {
                selectedRule = rule;
                break;
            }
        }

        selectedRule ??= rules[0];

        double low = selectedRule.Low;
        double high = selectedRule.High;
        double val = value.Value;
        string boundsJson = string.Create(CultureInfo.InvariantCulture, $"{{\"low\":{low},\"high\":{high}}}");

        if (val < low)
        {
            return new LabAssessmentResult("LOW", true, boundsJson, low, high);
        }

        if (val > high)
        {
            return new LabAssessmentResult("HIGH", true, boundsJson, low, high);
        }

        return new LabAssessmentResult("NORMAL", false, boundsJson, low, high);
    }
}
