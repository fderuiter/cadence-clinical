namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// The actor and reason for change stamped on every audited write. The actor is always a real identity:
/// the authenticated caller's <c>sub</c> claim inside an HTTP request, or a named
/// <see cref="ServiceAccount"/> that background work declared through <see cref="IAuditActorScope"/>.
/// There is no anonymous fallback; reading <see cref="UserId"/> with neither in place throws
/// <see cref="AuditActorUnresolvedException"/>, so the write fails instead of being recorded against
/// nobody (21 CFR 11.10(e), ALCOA+ attributable).
/// @req:PRD-SYS-053
/// </summary>
public interface ICurrentAuditContextAccessor
{
    /// <summary>The acting user or service account.</summary>
    /// <exception cref="AuditActorUnresolvedException">No authenticated caller and no service account.</exception>
    string UserId { get; }

    /// <summary>
    /// The reason for change: the service account's declared reason, else the caller's
    /// <c>X-Change-Reason</c> header, else <c>"system_operation"</c>.
    /// </summary>
    string ChangeReason { get; }
}

/// <summary>
/// Lets background work (message consumers, scheduled jobs) run the current DI scope as a named
/// <see cref="ServiceAccount"/>, so its writes are attributed to that account rather than failing.
/// @req:PRD-SYS-053
/// </summary>
public interface IAuditActorScope
{
    /// <summary>Attributes every write in the current scope to <paramref name="account"/>.</summary>
    void RunAs(ServiceAccount account);
}

/// <summary>
/// Thrown when an audited write has no resolvable actor. Surfaces as a 500 rather than a write attributed to
/// no one; authenticated HTTP callers never reach it because the gateway scheme rejects them first with 401.
/// @req:PRD-SYS-053
/// </summary>
public sealed class AuditActorUnresolvedException()
    : InvalidOperationException(
        "No audit actor: the request has no authenticated 'sub' claim and no service account was declared for this scope.");
