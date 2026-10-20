using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Execution.Infrastructure.Normalization;

/// <summary>
/// Reference range selection and clinical indicator evaluation engine conforming to PRD-SYS-121.
/// Matches age- and sex-stratified normal intervals and computes out-of-range indicators.
/// Custom ranges are held as an immutable snapshot keyed by study and test code. <see cref="UpdateActiveRanges"/>
/// builds a new snapshot and swaps it in one reference write, and <see cref="EnsureLoadedAsync"/> loads the persisted
/// ranges before the first evaluation, so evaluation never sees an empty or half-built set (#5435). Loading is lazy
/// because hosts that never evaluate (OpenAPI export, composition tests) must start without a database.
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
/// <param name="scopeFactory">Resolves <see cref="ExecutionDbContext"/> for the first load; without one (unit tests)
/// only <see cref="UpdateActiveRanges"/> fills the snapshot.</param>
public sealed class LabRangeService(IServiceScopeFactory? scopeFactory = null) : ILabRangeService, IDisposable
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile FrozenDictionary<string, LabReferenceRange[]> _activeCustomRanges = FrozenDictionary<string, LabReferenceRange[]>.Empty;
    private volatile bool _loaded;

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

    // A range without a study cannot be scoped, so it never applies.
    private static string Key(string studyId, string testCode) => $"{studyId.Trim()}\u001f{testCode.Trim()}";

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded || scopeFactory is null)
        {
            return;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var ranges = await db.LabReferenceRanges
                .AsNoTracking()
                .Where(r => r.IsActive && !r.IsDeleted)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // An import that finished while this load ran already published a complete, newer set.
            if (!_loaded)
            {
                UpdateActiveRanges(ranges);
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    // Every caller passes all active persisted ranges, so each call publishes a complete set.
    public void UpdateActiveRanges(IEnumerable<LabReferenceRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        _activeCustomRanges = ranges
            .Where(r => r.IsActive && !r.IsDeleted && !string.IsNullOrWhiteSpace(r.StudyId))
            .GroupBy(r => Key(r.StudyId, r.TestCode), StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        _loaded = true;
    }

    public LabAssessmentResult Evaluate(
        StudyCode studyId,
        TestCode testCode,
        double? value,
        string? sex = null,
        int? ageYears = null,
        string? labSource = null,
        string? siteId = null)
    {
        if (string.IsNullOrWhiteSpace(studyId.Value))
        {
            throw new ArgumentException("A study is required to choose a lab reference range.", nameof(studyId));
        }

        if (value is null)
        {
            return new LabAssessmentResult("NORMAL", false, null, null, null);
        }

        string normCode = testCode.Value?.Trim() ?? string.Empty;
        string normSource = string.IsNullOrWhiteSpace(labSource) ? "CENTRAL" : labSource.Trim().ToUpperInvariant();
        string normSex = string.IsNullOrWhiteSpace(sex) ? "U" : sex.Trim().ToUpperInvariant();

        // 1. Check the study's custom persisted ranges first
        if (_activeCustomRanges.TryGetValue(Key(studyId.Value, normCode), out var candidates) && candidates.Length > 0)
        {
            var bestMatch = SelectBestRange(candidates, normSource, siteId, normSex, ageYears);
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
        LabReferenceRange[] candidates,
        string obsSource,
        string? obsSiteId,
        string obsSex,
        int? obsAge)
    {
        var scored = new List<(LabReferenceRange Range, int Score)>();

        foreach (var r in candidates)
        {
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

    /// <summary>Releases the lock that serialises the first load; the container disposes the singleton at shutdown.</summary>
    public void Dispose() => _loadLock.Dispose();
}

