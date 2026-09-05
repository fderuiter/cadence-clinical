namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Explicit-call audit-log seam (MIG-SUBJ-017, `02_technical_design.md` §4.4) that the application
/// service (MIG-SUBJ-018/019) and <c>EConsentCompletedConsumer</c> (MIG-SUBJ-021) invoke for every Rule 6
/// application-initiated, semantically-named mutation (`ENROLL_SUBJECT`, `RECORD_CONSENT`,
/// `TRANSITION_&lt;STATE&gt;`, `UPDATE_DEMOGRAPHICS`, `BLOCKED_ENROLLMENT`) — the ones MIG-SUBJ-007's
/// automatic `GxPAuditInterceptor` cannot label on its own, since a bare `EntityState.Modified` cannot
/// distinguish "consent recorded" from "state transitioned". Implementations append the resulting
/// <see cref="Cadence.Core.Domain.Audit.AuditLogEntry"/> to the caller's own, already-scoped unit of
/// work, so it commits inside the identical `SaveChangesAsync` transaction as the entity mutation being
/// audited — operating alongside the interceptor's own field-stamping, not instead of it.
/// @req:PRD-SYS-053
/// </summary>
public interface IAuditLogWriter
{
    /// <summary>
    /// Appends a Merkle-chained <see cref="Cadence.Core.Domain.Audit.AuditLogEntry"/> — chained per
    /// <paramref name="tableName"/>, exactly as `GxPAuditInterceptor`'s own per-`EntityName` chain is —
    /// to the caller's current unit of work. Does not itself call `SaveChangesAsync`; the entry commits
    /// alongside whatever mutation the caller persists next.
    /// </summary>
    /// <param name="tableName">The audited table/entity name — also the chain's scoping key (e.g. `"clinical_subjects"`).</param>
    /// <param name="recordId">The audited record's identifier.</param>
    /// <param name="action">The caller-supplied semantic action (e.g. `"ENROLL_SUBJECT"`, `"TRANSITION_SCREENING"`).</param>
    /// <param name="userId">The acting user, typically <see cref="ICurrentAuditContextAccessor.UserId"/>.</param>
    /// <param name="changeReason">The reason for change, typically <see cref="ICurrentAuditContextAccessor.ChangeReason"/>.</param>
    /// <param name="oldValues">The record's field values before the change, or <see langword="null"/> when not applicable.</param>
    /// <param name="newValues">The record's field values after the change, or <see langword="null"/> when not applicable.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteAsync(
        string tableName,
        string recordId,
        string action,
        string userId,
        string changeReason,
        IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues,
        CancellationToken ct);
}
