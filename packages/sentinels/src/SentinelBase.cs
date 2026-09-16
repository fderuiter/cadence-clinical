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
