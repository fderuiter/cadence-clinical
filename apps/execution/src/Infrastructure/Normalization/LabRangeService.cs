using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
    private readonly ConcurrentDictionary<string, List<LabReferenceRange>> _activeCustomRanges = new(StringComparer.OrdinalIgnoreCase);

    private sealed record RangeRule(double Low, double High, string? Sex = null);

    private static readonly FrozenDictionary<string, RangeRule[]> StandardCatalog = new Dictionary<string, RangeRule[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["GLUC"] = [new(70.0, 99.0)],
        ["LB_GLUC"] = [new(70.0, 99.0)],
        ["GLUCOSE"] = [new(70.0, 99.0)],
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
        ["POTASSIUM"] = [new(3.5, 5.0)],
        ["BILI"] = [new(5.1, 20.5)],
        ["BILIRUBIN"] = [new(5.1, 20.5)],
        ["CREAT"] = [new(0.6, 1.2)],
        ["CREATININE"] = [new(0.6, 1.2)],
        ["HEMOGLOBIN"] = [new(12.0, 16.0)],
        ["HGB"] = [new(12.0, 16.0)],
        ["PLATELETS"] = [new(150.0, 450.0)],
        ["PLT"] = [new(150.0, 450.0)],
        ["ANC"] = [new(1.5, 8.0)],
        ["WT"] = [new(40.0, 150.0)],
        ["HT"] = [new(1.2, 2.2)],
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public void UpdateActiveRanges(IEnumerable<LabReferenceRange> ranges)
    {
        if (ranges == null) return;
        _activeCustomRanges.Clear();

        foreach (var group in ranges.Where(r => r.IsActive && !r.IsDeleted).GroupBy(r => r.TestCode, StringComparer.OrdinalIgnoreCase))
        {
            _activeCustomRanges[group.Key] = group.ToList();
        }
    }

    public LabAssessmentResult EvaluateRange(
        string testCode,
        double? value,
        int? ageYears = null,
        string? sex = null,
        string? labSource = null,
        string? studyId = null,
        string? siteId = null) =>
        Evaluate(new TestCode(testCode), value, sex, ageYears, labSource, studyId, siteId);

    public LabAssessmentResult Evaluate(
        TestCode testCode,
        double? value,
        string? sex = null,
        int? ageYears = null,
        string? labSource = null,
        string? studyId = null,
        string? siteId = null)
    {
        if (value is null)
        {
            return new LabAssessmentResult("NORMAL", false, null, null, null);
        }

        string normCode = testCode.Value?.Trim() ?? string.Empty;
        string normSource = string.IsNullOrWhiteSpace(labSource) ? "CENTRAL" : labSource.Trim().ToUpperInvariant();
        string normSex = string.IsNullOrWhiteSpace(sex) ? "U" : sex.Trim().ToUpperInvariant();

        // 1. Check custom persisted ranges first
        if (_activeCustomRanges.TryGetValue(normCode, out var candidates) && candidates.Count > 0)
        {
            var bestMatch = SelectBestRange(candidates, normSource, siteId, normSex, ageYears, studyId);
            if (bestMatch != null)
            {
                return EvaluateAgainstCustomRange(value.Value, bestMatch);
            }
        }

        // 2. Fallback to StandardCatalog
        if (!StandardCatalog.TryGetValue(normCode, out var rules) || rules.Length == 0)
        {
            return new LabAssessmentResult("NORMAL", false, null, null, null);
        }

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

    private static LabReferenceRange? SelectBestRange(
        List<LabReferenceRange> candidates,
        string obsSource,
        string? obsSiteId,
        string obsSex,
        int? obsAge,
        string? obsStudyId)
    {
        var scored = new List<(LabReferenceRange Range, int Score)>();

        foreach (var r in candidates)
        {
            if (!string.IsNullOrWhiteSpace(obsStudyId) && !string.IsNullOrWhiteSpace(r.StudyId) &&
                !string.Equals(r.StudyId, obsStudyId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 1. Site Specificity
            int siteScore = 0;
            if (obsSource == "LOCAL")
            {
                if (r.Source == "LOCAL" && !string.IsNullOrWhiteSpace(r.SiteId) && string.Equals(r.SiteId, obsSiteId, StringComparison.OrdinalIgnoreCase))
                {
                    siteScore = 3; // Exact site match
                }
                else if (r.Source == "LOCAL" && string.IsNullOrWhiteSpace(r.SiteId))
                {
                    siteScore = 2; // Generic local match
                }
                else if (r.Source == "CENTRAL")
                {
                    siteScore = 1; // Central fallback
                }
            }
            else // CENTRAL observation
            {
                if (r.Source == "CENTRAL")
                {
                    siteScore = 1;
                }
            }

            if (siteScore == 0) continue;

            // 2. Sex Specificity
            int sexScore = 0;
            string rSex = r.SexApplicability?.Trim().ToUpperInvariant() ?? "ALL";
            if (rSex is "ALL" or "U" || string.IsNullOrWhiteSpace(rSex))
            {
                sexScore = 1;
            }
            else if (string.Equals(rSex, obsSex, StringComparison.OrdinalIgnoreCase))
            {
                sexScore = 2;
            }

            if (sexScore == 0) continue;

            // 3. Age Specificity
            int ageScore = 1; // Default no bounds matched
            if ((r.AgeLow.HasValue || r.AgeHigh.HasValue) && obsAge.HasValue)
            {
                bool lowOk = !r.AgeLow.HasValue || obsAge.Value >= r.AgeLow.Value;
                bool highOk = !r.AgeHigh.HasValue || obsAge.Value <= r.AgeHigh.Value;

                if (!lowOk || !highOk) continue; // Outside age bounds

                ageScore = (r.AgeLow.HasValue && r.AgeHigh.HasValue) ? 3 : 2;
            }

            int totalScore = siteScore * 100 + sexScore * 10 + ageScore;
            scored.Add((r, totalScore));
        }

        if (scored.Count == 0) return null;

        // Deterministic tie-breaking order: Highest Score -> Narrowest Age Span -> Highest Low Age -> Lowest Low Normal Bound -> ID
        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => (s.Range.AgeHigh ?? 150) - (s.Range.AgeLow ?? 0))
            .ThenByDescending(s => s.Range.AgeLow ?? 0)
            .ThenBy(s => s.Range.LowBound ?? double.MinValue)
            .ThenBy(s => s.Range.Id.ToString())
            .First().Range;
    }

    private static LabAssessmentResult EvaluateAgainstCustomRange(double val, LabReferenceRange r)
    {
        double? low = r.LowBound;
        double? high = r.HighBound;
        double? critLow = r.CriticalLow;
        double? critHigh = r.CriticalHigh;

        string boundsJson = string.Create(CultureInfo.InvariantCulture, $"{{\"low\":{(low.HasValue ? low.Value : "null")},\"high\":{(high.HasValue ? high.Value : "null")}}}");

        if (critLow.HasValue && val < critLow.Value)
        {
            return new LabAssessmentResult("LOW LOW", true, boundsJson, low, high);
        }

        if (critHigh.HasValue && val > critHigh.Value)
        {
            return new LabAssessmentResult("HIGH HIGH", true, boundsJson, low, high);
        }

        if (low.HasValue && val < low.Value)
        {
            return new LabAssessmentResult("LOW", true, boundsJson, low, high);
        }

        if (high.HasValue && val > high.Value)
        {
            return new LabAssessmentResult("HIGH", true, boundsJson, low, high);
        }

        return new LabAssessmentResult("NORMAL", false, boundsJson, low, high);
    }
}

