using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;

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
/// Custom ranges apply only to the study they were imported for (#5435).
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public interface ILabRangeService
{
    /// <summary>
    /// Loads the persisted active custom ranges on first use, so a restarted service never evaluates against an
    /// empty set. Later calls return at once. Call before <see cref="Evaluate"/>.
    /// </summary>
    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates <paramref name="value"/> against the best matching active custom range of <paramref name="studyId"/>,
    /// falling back to the standard catalog when the study has none for the test.
    /// </summary>
    LabAssessmentResult Evaluate(
        StudyCode studyId,
        TestCode testCode,
        double? value,
        string? sex = null,
        int? ageYears = null,
        string? labSource = null,
        string? siteId = null);

    /// <summary>Replaces every active custom range in one atomic swap; evaluations never see a partial set.</summary>
    void UpdateActiveRanges(IEnumerable<LabReferenceRange> ranges);
}
