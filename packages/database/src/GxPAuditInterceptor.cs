using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cadence.Database;

/// <summary>
/// EF Core SaveChangesInterceptor enforcing 21 CFR Part 11 immutable audit trail fields and Merkle block hashing.
/// </summary>
public class GxPAuditInterceptor(Func<string> currentUserProvider, IClock clock) : SaveChangesInterceptor
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
        DateTime now = AuditLogEntry.NormalizeTimestamp(clock.UtcNow.UtcDateTime);

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
                // From the loaded value, not the current one, so a retried save does not count twice.
                modifiedAgg.VersionIndex = (long)(entry.Property(nameof(IGxPAuditable.VersionIndex)).OriginalValue ?? 0L) + 1;
            }
        }

        // This interceptor is the only audit writer, so there is exactly one entry per changed entity per save
        // and one chain per EntityName (the aggregate's CLR type name). Semantic actions recorded through
        // AuditActions label the matching entity's entry; any left over become action-only entries.
        var actions = AuditActions.Drain(context);
        var chain = new AuditChainCursor(context);
        InFlight.AddOrUpdate(context, new SaveAttempt(actions.ToList(), chain.Added));

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

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            InFlight.Remove(eventData.Context);
        }

        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// A failed save leaves its changes pending, and the caller may retry it. Undo this attempt's audit work so
    /// the retry writes each entry once: detach the entries it added and put its semantic actions back.
    /// </summary>
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        RollBackAttempt(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        RollBackAttempt(eventData.Context);
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    private static readonly ConditionalWeakTable<DbContext, SaveAttempt> InFlight = new();

    private sealed record SaveAttempt(List<AuditAction> Actions, List<AuditLogEntry> AddedEntries);

    private static void RollBackAttempt(DbContext? context)
    {
        if (context is null || !InFlight.TryGetValue(context, out SaveAttempt? attempt))
        {
            return;
        }

        InFlight.Remove(context);
        foreach (AuditLogEntry added in attempt.AddedEntries)
        {
            context.Entry(added).State = EntityState.Detached;
        }

        AuditActions.Restore(context, attempt.Actions);
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

        public List<AuditLogEntry> Added { get; } = [];

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

            var logEntry = new AuditLogEntry
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
            };
            context.Add(logEntry);

            _tips[draft.EntityName] = blockHash;
            Added.Add(logEntry);
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
