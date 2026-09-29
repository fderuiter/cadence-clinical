namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// A save lost an optimistic-concurrency race: another request changed the same record after this one read it.
/// The save is rejected, never retried over the other change (#4766), and the endpoint returns 409 Conflict
/// with the stable code <see cref="Code"/> so the caller can reload and re-apply its change.
/// @req:PRD-SUB-001
/// </summary>
public sealed class ConcurrencyConflictException(IReadOnlyList<string> entityNames, Exception innerException)
    : Exception(
        $"Another request changed {string.Join(", ", entityNames)} after it was read; the save was rejected.",
        innerException)
{
    /// <summary>The <c>code</c> extension on the 409 ProblemDetails.</summary>
    public const string Code = "CONCURRENCY_CONFLICT";

    /// <summary>The CLR type names of the entities whose saves conflicted.</summary>
    public IReadOnlyList<string> EntityNames { get; } = entityNames;
}
