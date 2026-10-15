using Cadence.Apps.Execution.Domain.Observations;

namespace Cadence.Apps.Execution.Application.Abstractions;

public sealed record LabAssessmentResult(
    string Indicator,
    bool OutOfRange,
    string? MatchedBoundsJson,
    double? RangeLow,
    double? RangeHigh)
{
    public bool IsOutOfRange => OutOfRange;
}

/// <summary>
/// Service abstraction for matching laboratory reference intervals and evaluating clinical indicators.
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public interface ILabRangeService
{
    LabAssessmentResult Evaluate(
        TestCode testCode,
        double? value,
        string? sex = null,
        int? ageYears = null,
        string? labSource = null,
        string? studyId = null,
        string? siteId = null);

    LabAssessmentResult EvaluateRange(
        string testCode,
        double? value,
        int? ageYears = null,
        string? sex = null,
        string? labSource = null,
        string? studyId = null,
        string? siteId = null);

    void UpdateActiveRanges(IEnumerable<Domain.Observations.LabReferenceRange> ranges);
}
