using System;

namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// The actor and reason for change stamped on every audited write. The actor is always a real identity:
/// the authenticated caller's <c>sub</c> claim inside an HTTP request, or a named
/// <see cref="ServiceAccount"/> that background work declared through <see cref="IAuditActorScope"/>.
/// Neither actor nor change reason has an anonymous or system fallback (21 CFR 11.10(e), ALCOA+ attributable).
/// </summary>
public interface ICurrentAuditContextAccessor
{
    /// <summary>The acting user or service account.</summary>
    /// <exception cref="AuditActorUnresolvedException">No authenticated caller and no service account.</exception>
    string UserId { get; }

    /// <summary>
    /// The reason for change: the service account's declared reason, else the caller's decoded
    /// <c>X-Change-Reason</c> header (minimum 10 characters). Throws <see cref="AuditChangeReasonUnresolvedException"/>
    /// when neither is present.
    /// </summary>
    /// <exception cref="AuditChangeReasonUnresolvedException">No service account and no valid change reason header.</exception>
    string ChangeReason { get; }
}

/// <summary>
/// Lets background work (message consumers, scheduled jobs, seeders) run the current DI scope as a named
/// <see cref="ServiceAccount"/>, so its writes are attributed to that account rather than failing.
/// </summary>
public interface IAuditActorScope
{
    /// <summary>Attributes every write in the current scope to <paramref name="account"/>.</summary>
    void RunAs(ServiceAccount account);
}

/// <summary>
/// Thrown when an audited write has no resolvable actor.
/// </summary>
public sealed class AuditActorUnresolvedException()
    : InvalidOperationException(
        "No audit actor: the request has no authenticated 'sub' claim and no service account was declared for this scope.");

/// <summary>
/// Thrown when an audited write has no resolvable change reason of at least 10 characters.
/// </summary>
public sealed class AuditChangeReasonUnresolvedException()
    : InvalidOperationException(
        "No valid audit change reason: the request lacks a valid 'X-Change-Reason' header of at least 10 characters and no service account was declared for this scope.");
