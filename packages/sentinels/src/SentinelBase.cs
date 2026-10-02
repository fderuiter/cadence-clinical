using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Sentinels;

public record SentinelResult(
    string Name,
    bool Passed,
    int ExitCode = 0,
    double DurationSeconds = 0.0,
    string Summary = "",
    IReadOnlyList<string>? Violations = null,
    string? Remediation = null
);

public interface ISentinelCheck
{
    string Name { get; }
    string Description { get; }
    Task<SentinelResult> RunAsync(CancellationToken ct = default);
}

public record FieldBaselineStats(
    double Mean,
    double Variance,
    int SampleSize
);

public record StatisticalBaseline(
    IReadOnlyDictionary<int, long> GlobalDigitCounts,
    long GlobalTotalDigits,
    IReadOnlyDictionary<string, FieldBaselineStats> FieldStats
);

public record FraudObservationValue(
    string TestCode,
    double Value,
    string? SiteId = null
);

public record FraudEvaluationContext(
    string SubmissionId,
    string StudyId,
    string SubjectId,
    string? SiteId,
    string FormId,
    string? VisitId,
    IReadOnlyList<FraudObservationValue> Observations,
    StatisticalBaseline Baseline
);

public interface IFraudSentinelCheck : ISentinelCheck
{
    Task<SentinelResult> EvaluateAsync(FraudEvaluationContext context, CancellationToken ct = default);
}
