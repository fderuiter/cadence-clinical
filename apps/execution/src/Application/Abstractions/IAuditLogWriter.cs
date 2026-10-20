namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Explicit-call audit-log seam (MIG-SUBJ-017, `02_technical_design.md` §4.4) that the application
/// service (MIG-SUBJ-018/019) and <c>EConsentCompletedConsumer</c> (MIG-SUBJ-021) invoke for every Rule 6
/// application-initiated, semantically-named mutation (`ENROLL_SUBJECT`, `RECORD_CONSENT`,
/// `TRANSITION_&lt;STATE&gt;`, `UPDATE_DEMOGRAPHICS`, `BLOCKED_ENROLLMENT`) — the ones MIG-SUBJ-007's
/// automatic `GxPAuditInterceptor` cannot label on its own, since a bare `EntityState.Modified` cannot
/// distinguish "consent recorded" from "state transitioned". The action labels the interceptor's own entry for
/// that change, so each change has exactly one audit entry committed in the same `SaveChangesAsync` (#4768).
/// </summary>
public interface IAuditLogWriter
{
    /// <summary>
    /// Labels the caller's next save with a semantic action. The GxP interceptor, the only audit writer, puts it
    /// on the changed entity's own entry (one entry per change, #4768), or writes it as an action-only entry on
    /// the same chain when the save changes nothing. Does not itself call `SaveChangesAsync`.
    /// </summary>
    /// <param name="entityName">The aggregate's CLR type name, which is also the chain key (e.g. <c>nameof(ClinicalSubject)</c>).</param>
    /// <param name="recordId">The audited record's identifier.</param>
    /// <param name="action">The caller-supplied semantic action (e.g. `"ENROLL_SUBJECT"`, `"TRANSITION_SCREENING"`).</param>
    /// <param name="userId">The acting user, typically <see cref="ICurrentAuditContextAccessor.UserId"/>.</param>
    /// <param name="changeReason">The reason for change, typically <see cref="ICurrentAuditContextAccessor.ChangeReason"/>.</param>
    /// <param name="oldValues">The record's field values before the change, or <see langword="null"/> when not applicable.</param>
    /// <param name="newValues">The record's field values after the change, or <see langword="null"/> when not applicable.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteAsync(
        string entityName,
        string recordId,
        string action,
        string userId,
        string changeReason,
        IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues,
        CancellationToken ct);
}
