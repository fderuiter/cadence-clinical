namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Testability seam for <see cref="DateTimeOffset.UtcNow"/> (`01_spec.md` §3) — lets aggregate and
/// application-service tests assert exact timestamps without wall-clock flakiness.
/// @req:PRD-SUB-001
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
