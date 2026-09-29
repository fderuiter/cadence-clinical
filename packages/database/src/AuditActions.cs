using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Database;

/// <summary>
/// A semantic audit action (for example <c>ENROLL_SUBJECT</c>) that application code attaches to the next save
/// of a <see cref="DbContext"/>. <see cref="GxPAuditInterceptor"/> is the only audit writer: it puts the action
/// on the audit entry of the changed entity with the same name and id, or writes an action-only entry when
/// nothing changed (for example <c>BLOCKED_ENROLLMENT</c>).
/// @req:PRD-SYS-053
/// </summary>
/// <param name="EntityName">The chain key: the aggregate's CLR type name, e.g. <c>ClinicalSubject</c>.</param>
/// <param name="EntityId">The record id, formatted the way the interceptor formats the primary key.</param>
/// <param name="Action">The semantic action name.</param>
/// <param name="UserId">The actor.</param>
/// <param name="ReasonForChange">The reason for change.</param>
/// <param name="ChangesJson">Optional details, used only for action-only entries.</param>
public sealed record AuditAction(
    string EntityName,
    string EntityId,
    string Action,
    string UserId,
    string ReasonForChange,
    string? ChangesJson = null);

/// <summary>
/// Holds <see cref="AuditAction"/>s per <see cref="DbContext"/> until its next save.
/// @req:PRD-SYS-053
/// </summary>
public static class AuditActions
{
    private static readonly ConditionalWeakTable<DbContext, List<AuditAction>> Pending = new();

    /// <summary>Attaches <paramref name="action"/> to the next save of <paramref name="context"/>.</summary>
    public static void Record(DbContext context, AuditAction action)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);
        List<AuditAction> actions = Pending.GetOrCreateValue(context);
        lock (actions)
        {
            actions.Add(action);
        }
    }

    internal static bool HasPending(DbContext context) =>
        Pending.TryGetValue(context, out List<AuditAction>? actions) && actions.Count > 0;

    internal static List<AuditAction> Drain(DbContext context)
    {
        if (!Pending.TryGetValue(context, out List<AuditAction>? actions))
        {
            return [];
        }

        lock (actions)
        {
            var drained = actions.ToList();
            actions.Clear();
            return drained;
        }
    }
}
