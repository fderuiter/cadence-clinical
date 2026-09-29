using System.Text.Json;
using System.Threading;

using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cadence.Database;

/// <summary>
/// EF Core SaveChangesInterceptor enforcing 21 CFR Part 11 immutable audit trail fields and Merkle block hashing.
/// @req:PRD-SYS-053
/// </summary>
public class GxPAuditInterceptor(Func<string> currentUserProvider, Func<DateTime>? clockProvider = null) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
        {
            return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
        }

        DbContext context = eventData.Context;
        if (!HasAuditedWork(context))
        {
            // Nothing audited in this save (e.g. outbox bookkeeping), so there is no actor to resolve.
            return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
        }

        // Resolved only once something is audited; a provider with no actor throws here and the save fails.
        string currentUser = currentUserProvider();
        DateTime now = AuditLogEntry.NormalizeTimestamp((clockProvider ?? (() => DateTime.UtcNow))());

        foreach (var entry in context.ChangeTracker.Entries<IGxPAuditable>())
        {
            if (entry.State == EntityState.Added && entry.Entity is AggregateRootBase addedAgg)
            {
                addedAgg.CreatedAt = now;
                addedAgg.CreatedBy = string.IsNullOrWhiteSpace(addedAgg.CreatedBy) ? currentUser : addedAgg.CreatedBy;
                addedAgg.VersionIndex = 1;
            }
            else if (entry.State == EntityState.Modified && entry.Entity is AggregateRootBase modifiedAgg)
            {
                modifiedAgg.VersionIndex++;
            }
        }

        // This interceptor is the only audit writer, so there is exactly one entry per changed entity per save
        // and one chain per EntityName (the aggregate's CLR type name). Semantic actions recorded through
        // AuditActions label the matching entity's entry; any left over become action-only entries.
        var actions = AuditActions.Drain(context);
        var chain = new AuditChainCursor(context);

        // Snapshot to a list before mutating the change tracker (context.Add below).
        var auditableEntries = context.ChangeTracker.Entries<IGxPAuditable>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in auditableEntries)
        {
            string entityName = entry.Metadata.ClrType.Name;
            string entityId = BuildEntityId(entry);
            string changesJson = JsonSerializer.Serialize(
                entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));

            AuditAction? action = TakeAction(actions, entityName, entityId);
            await chain.AppendAsync(new AuditLogEntryDraft(
                entityName,
                entityId,
                action?.Action ?? entry.State.ToString(),
                now,
                action?.UserId ?? currentUser,
                action?.ReasonForChange ?? entry.Entity.ReasonForChange,
                entry.Entity.VersionIndex,
                changesJson), cancellationToken).ConfigureAwait(false);
        }

        // Actions with no changed entity (a blocked attempt, an activation that changes no row) are still
        // audit events: they join the same entity-name chain with version index 0.
        foreach (AuditAction action in actions)
        {
            await chain.AppendAsync(new AuditLogEntryDraft(
                action.EntityName,
                action.EntityId,
                action.Action,
                now,
                action.UserId,
                action.ReasonForChange,
                0,
                action.ChangesJson), cancellationToken).ConfigureAwait(false);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Synchronous saves cannot run the asynchronous chain lookup, so a synchronous save with anything to audit
    /// fails rather than committing without an audit entry. Saves with nothing to audit pass through.
    /// </summary>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null && HasAuditedWork(eventData.Context))
        {
            throw new InvalidOperationException(
                "Audited changes must be saved with SaveChangesAsync so GxPAuditInterceptor can write their audit entries.");
        }

        return base.SavingChanges(eventData, result);
    }

    private static bool HasAuditedWork(DbContext context) =>
        AuditActions.HasPending(context)
        || context.ChangeTracker.Entries<IGxPAuditable>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

    private static AuditAction? TakeAction(List<AuditAction> actions, string entityName, string entityId)
    {
        int index = actions.FindIndex(a =>
            string.Equals(a.EntityName, entityName, StringComparison.Ordinal)
            && string.Equals(a.EntityId, entityId, StringComparison.Ordinal));
        if (index < 0)
        {
            return null;
        }

        AuditAction action = actions[index];
        actions.RemoveAt(index);
        return action;
    }

    private sealed record AuditLogEntryDraft(
        string EntityName,
        string EntityId,
        string Action,
        DateTime Timestamp,
        string UserId,
        string ReasonForChange,
        long VersionIndex,
        string? ChangesJson);

    /// <summary>
    /// Appends entries to their EntityName chain: the predecessor is the latest entry added in this save for that
    /// name, otherwise the persisted chain tip (the entry no other entry names as its predecessor).
    /// </summary>
    private sealed class AuditChainCursor(DbContext context)
    {
        private readonly Dictionary<string, string> _tips = new(StringComparer.Ordinal);

        public async Task AppendAsync(AuditLogEntryDraft draft, CancellationToken cancellationToken)
        {
            if (!_tips.TryGetValue(draft.EntityName, out string? predecessorHash))
            {
                string name = draft.EntityName;
                predecessorHash = await context.Set<AuditLogEntry>()
                    .Where(e => e.EntityName == name)
                    .Where(e => !context.Set<AuditLogEntry>().Any(sub => sub.EntityName == name && sub.PredecessorHash == e.BlockHash))
                    .OrderByDescending(e => e.Timestamp)
                    .ThenByDescending(e => e.VersionIndex)
                    .ThenByDescending(e => e.Id)
                    .Select(e => e.BlockHash)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
            }

            string blockHash = AuditLogEntry.ComputeBlockHash(
                draft.EntityName, draft.EntityId, draft.Action, draft.Timestamp, draft.UserId,
                draft.VersionIndex, draft.ChangesJson, predecessorHash);

            context.Add(new AuditLogEntry
            {
                EntityName = draft.EntityName,
                EntityId = draft.EntityId,
                Action = draft.Action,
                Timestamp = draft.Timestamp,
                UserId = draft.UserId,
                ReasonForChange = draft.ReasonForChange,
                VersionIndex = draft.VersionIndex,
                ChangesJson = draft.ChangesJson,
                PredecessorHash = predecessorHash,
                BlockHash = blockHash,
            });

            _tips[draft.EntityName] = blockHash;
        }
    }

    private static string BuildEntityId(EntityEntry<IGxPAuditable> entry)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey();
        if (primaryKey is null)
        {
            return string.Empty;
        }

        return string.Join(
            ":",
            primaryKey.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? string.Empty));
    }
}
