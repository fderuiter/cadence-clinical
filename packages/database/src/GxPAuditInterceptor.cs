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
        string currentUser = currentUserProvider();
        DateTime now = (clockProvider ?? (() => DateTime.UtcNow))();

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

        // Per-entity-type (not global) Merkle chain: each EntityName's audit trail chains independently,
        // predecessor sourced from the most recently persisted AuditLogEntry for that same EntityName.
        var lastBlockHashByEntityName = new Dictionary<string, string>(StringComparer.Ordinal);

        // Snapshot to a list before mutating the change tracker (context.Add below) to avoid enumerating
        // a collection that is being modified.
        var auditableEntries = context.ChangeTracker.Entries<IGxPAuditable>().ToList();

        foreach (var entry in auditableEntries)
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            string entityName = entry.Metadata.ClrType.Name;

            if (!lastBlockHashByEntityName.TryGetValue(entityName, out var predecessorHash))
            {
                predecessorHash = await context.Set<AuditLogEntry>()
                    .Where(e => e.EntityName == entityName)
                    .Where(e => !context.Set<AuditLogEntry>().Any(sub => sub.EntityName == entityName && sub.PredecessorHash == e.BlockHash))
                    .OrderByDescending(e => e.Timestamp)
                    .ThenByDescending(e => e.VersionIndex)
                    .ThenByDescending(e => e.Id)
                    .Select(e => e.BlockHash)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
            }

            string entityId = BuildEntityId(entry);
            string changesJson = JsonSerializer.Serialize(
                entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));

            // Interim Action value: no per-request semantic-action scope exists yet in the shared kernel
            // (e.g. "consent recorded" vs. "state transitioned"). Wiring that seam is this slice's own
            // follow-up (MIG-SUBJ-017/018/019), not blocking on it here per the design's explicit call-out.
            string action = entry.State.ToString();

            string blockHash = AuditLogEntry.ComputeBlockHash(
                entityName, entityId, action, now, currentUser,
                entry.Entity.VersionIndex, changesJson, predecessorHash);

            context.Add(new AuditLogEntry
            {
                EntityName = entityName,
                EntityId = entityId,
                Action = action,
                Timestamp = now,
                UserId = currentUser,
                ReasonForChange = entry.Entity.ReasonForChange,
                VersionIndex = entry.Entity.VersionIndex,
                ChangesJson = changesJson,
                PredecessorHash = predecessorHash,
                BlockHash = blockHash,
            });

            lastBlockHashByEntityName[entityName] = blockHash;
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
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
