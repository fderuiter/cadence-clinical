using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Core.Domain;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Cadence.Kernel.Persistence.Commands;

internal sealed record ChangedAggregate(
    string Type,
    string Id,
    long Version,
    string Action,
    string Changes,
    NodeRef? Subject,
    bool Derived);

internal static class KernelCommandAggregateStamper
{
    public static void Claim(
        CommandExecution execution,
        HashSet<(Type Type, string Key)> claimed,
        CollectionInstanceId? collection,
        bool scopeLocked,
        NodeRef? lockedScope,
        Type type,
        string key)
    {
        if (collection is { } instance)
        {
            // K-TX-1 as amended (ADR-2622): the form instance and observations, which the commit checks are its members.
            if (!(type == typeof(CollectionInstance) && key == KeyText(instance)) && type != typeof(Observation))
            {
                throw new InvalidOperationException(
                    $"K-TX-1: {execution.CommandType} changes form instance {instance}; it can change only that instance and its observations, not {type.Name} {key}.");
            }

            claimed.Add((type, key));
            return;
        }

        if (scopeLocked
            && (type == typeof(Assertion) || type == typeof(Attestation))
            && claimed.All(c => c.Type == typeof(Assertion) || c.Type == typeof(Attestation))
            && !claimed.Any(c => c.Type == type && c.Key != key))
        {
            claimed.Add((type, key));
            return;
        }

        if (IsStudyLockedProtocolImport(execution.Command, scopeLocked, lockedScope, out var importDraft))
        {
            var isTargetVersion = type == typeof(ProtocolVersion)
                && key == KeyText(importDraft.TargetVersionId)
                && !claimed.Any(c => c.Type == typeof(ProtocolVersion) && c.Key != key);
            var isStagedConcept = type == typeof(Concept)
                && importDraft.StagedConcepts.Any(c => KeyText(c.ConceptId) == key);
            if (!isTargetVersion && !isStagedConcept)
            {
                throw new InvalidOperationException(
                    $"K-TX-1: {execution.CommandType} under study scope lock {lockedScope} may change only protocol version {importDraft.TargetVersionId} and its staged concepts, not {type.Name} {key}.");
            }

            claimed.Add((type, key));
            return;
        }

        if (type == typeof(FormVersionSnapshot)
            && claimed.All(c => c.Type == typeof(ProtocolVersion) || c.Type == typeof(FormVersionSnapshot))
            && claimed.Count(c => c.Type == typeof(ProtocolVersion)) == 1)
        {
            claimed.Add((type, key));
            return;
        }

        if (claimed.Count > 0 && !claimed.Contains((type, key)))
        {
            var firstClaimed = claimed.First();
            throw new InvalidOperationException(
                $"K-TX-1: {execution.CommandType} already changes {firstClaimed.Type.Name} {firstClaimed.Key}; it cannot also change {type.Name} {key}. Use an event-driven saga.");
        }

        claimed.Add((type, key));
    }

    public static bool IsStudyLockedProtocolImport(
        ICommand command,
        bool scopeLocked,
        NodeRef? lockedScope,
        out ImportProtocolDraft importDraft)
    {
        if (command is ImportProtocolDraft cmd
            && scopeLocked
            && lockedScope is { Kind: NodeKinds.Study } scope
            && scope.Id == cmd.StudyId.Value)
        {
            importDraft = cmd;
            return true;
        }

        importDraft = null!;
        return false;
    }

    /// <summary>
    /// Stamps GxP audit fields on the changed aggregates and captures their change sets, the primary one first: the one
    /// changed aggregate (K-TX-1), a form instance followed by the observations a form save changed (ADR-2622), or a
    /// scope-locked lock/release assertion paired with its inline electronic signature (ADR-2621).
    /// </summary>
    public static List<ChangedAggregate> StampAggregates(
        KernelDbContext db,
        IReadOnlyList<(IModuleCommandStore Store, ModuleDbContext Context)> modules,
        CommandExecution execution,
        bool scopeLocked,
        NodeRef? lockedScope,
        CollectionInstanceId? collection)
    {
        var changed = db.ChangeTracker.Entries<AggregateRootBase>()
            .Select(e => (Entry: e, Module: (string?)null))
            .Concat(modules.SelectMany(m => m.Context.ChangeTracker.Entries<AggregateRootBase>().Select(e => (Entry: e, Module: (string?)m.Store.Module))))
            .Where(e => e.Entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (IsStudyLockedProtocolImport(execution.Command, scopeLocked, lockedScope, out var importDraft))
        {
            changed = OrderProtocolDraftImport(execution, changed, importDraft);
        }
        else if (changed.Count > 1)
        {
            if (scopeLocked)
            {
                changed = OrderSignedLockAssertion(execution, changed);
            }
            else if (collection is not null)
            {
                changed = OrderFormSave(execution, collection, changed);
            }
            else
            {
                changed = OrderProtocolApprovalSnapshots(changed);
            }
        }

        return [.. changed.Select(c => Stamp(execution, c.Entry, c.Module))];
    }

    /// <summary>
    /// Classifies a unique violation. Two commands that update the same aggregate version both write its next lineage
    /// node, and PostgreSQL may report that duplicate before the row-version check, so on an update a lineage duplicate
    /// is the same concurrency conflict.
    /// </summary>
    public static CommitOutcome UniqueViolationOutcome(string? table, ChangedAggregate? aggregate)
    {
        if (string.Equals(table, "idempotency_records", StringComparison.Ordinal))
        {
            return CommitOutcome.IdempotencyRace;
        }

        return aggregate?.Action == "Updated" && table is "lineage_nodes" or "lineage_edges"
            ? CommitOutcome.ConcurrencyConflict
            : CommitOutcome.UniqueConflict;
    }

    public static string KeyText(EntityEntry entry) =>
        string.Join("|", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => KeyText(entry.Property(p.Name).CurrentValue!)));

    public static string KeyText(object id) => id is IEntityId entityId ? entityId.Value.ToString("D") : id.ToString()!;

    private static List<(EntityEntry<AggregateRootBase> Entry, string? Module)> OrderProtocolDraftImport(
        CommandExecution execution,
        List<(EntityEntry<AggregateRootBase> Entry, string? Module)> changed,
        ImportProtocolDraft importDraft)
    {
        if (changed.Count == 0)
        {
            return changed;
        }

        var protocolEntries = changed
            .Where(c => c.Module is null
                && c.Entry.State is EntityState.Added or EntityState.Modified
                && c.Entry.Entity is ProtocolVersion pv
                && pv.Id == importDraft.TargetVersionId
                && pv.StudyId == importDraft.StudyId)
            .ToList();
        var allowedConcepts = importDraft.StagedConcepts.Select(c => c.ConceptId).ToHashSet();
        var conceptEntries = changed
            .Where(c => c.Module is null
                && c.Entry.State == EntityState.Added
                && c.Entry.Entity is Concept concept
                && allowedConcepts.Contains(concept.Id))
            .OrderBy(c => ((Concept)c.Entry.Entity).Id.Value)
            .ToList();
        if (protocolEntries.Count != 1 || conceptEntries.Count != changed.Count - 1)
        {
            throw new InvalidOperationException(
                $"K-TX-1: {execution.CommandType} under a study scope lock may write only one target ProtocolVersion and its newly added Concept records.");
        }

        return [protocolEntries[0], .. conceptEntries];
    }

    private static List<(EntityEntry<AggregateRootBase> Entry, string? Module)> OrderSignedLockAssertion(
        CommandExecution execution,
        List<(EntityEntry<AggregateRootBase> Entry, string? Module)> changed)
    {
        if (changed.Count == 2)
        {
            var assertionEntry = changed.SingleOrDefault(c => c.Module is null && c.Entry.State == EntityState.Added && c.Entry.Entity is Assertion);
            var attestationEntry = changed.SingleOrDefault(c => c.Module is null && c.Entry.State == EntityState.Added && c.Entry.Entity is Attestation);
            if (assertionEntry.Entry?.Entity is Assertion assertion
                && attestationEntry.Entry?.Entity is Attestation attestation
                && (assertion.IsLock || assertion.RetractsId is not null)
                && assertion.AttestationId == attestation.Id
                && attestation.TargetKind == NodeKinds.Assertion
                && attestation.TargetId == assertion.Id.Value)
            {
                return [assertionEntry, attestationEntry];
            }
        }

        throw new InvalidOperationException(
            $"K-TX-1: {execution.CommandType} may add only a lock/release assertion and its bound attestation under a scope lock (ADR-2621).");
    }

    private static List<(EntityEntry<AggregateRootBase> Entry, string? Module)> OrderFormSave(
        CommandExecution execution,
        CollectionInstanceId? collection,
        List<(EntityEntry<AggregateRootBase> Entry, string? Module)> changed)
    {
        var instance = collection is { } id
            ? changed.Select(c => c.Entry.Entity).OfType<CollectionInstance>().SingleOrDefault(c => c.Id == id)
            : null;
        if (instance is null)
        {
            throw new InvalidOperationException($"K-TX-1: {execution.CommandType} changed {changed.Count} aggregates.");
        }

        var observations = changed.Where(c => c.Entry.Entity is Observation).ToList();
        if (observations.Count != changed.Count - 1
            || observations.Any(o => o.Module is not null || o.Entry.State == EntityState.Deleted || !instance.Contains(((Observation)o.Entry.Entity).Id)))
        {
            throw new InvalidOperationException(
                $"K-TX-1: {execution.CommandType} may change form instance {instance.Id} and its member observations only (ADR-2622).");
        }

        return [changed.Single(c => ReferenceEquals(c.Entry.Entity, instance)), .. observations.OrderBy(o => ((Observation)o.Entry.Entity).Id)];
    }

    private static List<(EntityEntry<AggregateRootBase> Entry, string? Module)> OrderProtocolApprovalSnapshots(
        List<(EntityEntry<AggregateRootBase> Entry, string? Module)> changed)
    {
        var protocolEntry = changed.SingleOrDefault(c => c.Module is null && c.Entry.State == EntityState.Modified && c.Entry.Entity is ProtocolVersion);
        if (protocolEntry.Entry?.Entity is ProtocolVersion protocol)
        {
            var snapshots = changed.Where(c => c.Entry.Entity is FormVersionSnapshot).ToList();
            if (snapshots.Count == changed.Count - 1
                && snapshots.All(s => s.Module is null
                    && s.Entry.State == EntityState.Added
                    && ((FormVersionSnapshot)s.Entry.Entity).ProtocolVersionId == protocol.Id
                    && ((FormVersionSnapshot)s.Entry.Entity).TenantId == protocol.TenantId
                    && ((FormVersionSnapshot)s.Entry.Entity).StudyId == protocol.StudyId))
            {
                return [protocolEntry, .. snapshots.OrderBy(s => ((FormVersionSnapshot)s.Entry.Entity).Id)];
            }
        }

        throw new InvalidOperationException($"K-TX-1: changed {changed.Count} aggregates.");
    }

    private static ChangedAggregate Stamp(CommandExecution execution, EntityEntry<AggregateRootBase> entry, string? module)
    {
        var aggregate = entry.Entity;
        string action;
        string changes;
        switch (entry.State)
        {
            case EntityState.Added:
                aggregate.CreatedAt = execution.RecordedAt.UtcDateTime;
                aggregate.CreatedBy = execution.Actor.Id;
                aggregate.ReasonForChange = execution.Command.ReasonForChange;
                aggregate.VersionIndex = 1;
                action = "Created";
                changes = JsonSerializer.Serialize(
                    new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["after"] = Snapshot(entry, modifiedOnly: false, original: false) },
                    KernelCommandPipelineJson.Options);
                break;
            case EntityState.Modified:
                aggregate.ReasonForChange = execution.Command.ReasonForChange;
                aggregate.VersionIndex++;
                action = "Updated";
                changes = JsonSerializer.Serialize(
                    new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["before"] = Snapshot(entry, modifiedOnly: true, original: true),
                        ["after"] = Snapshot(entry, modifiedOnly: true, original: false),
                    },
                    KernelCommandPipelineJson.Options);
                break;
            default:
                action = "Deleted";
                changes = JsonSerializer.Serialize(
                    new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["before"] = Snapshot(entry, modifiedOnly: false, original: true) },
                    KernelCommandPipelineJson.Options);
                break;
        }

        // A module aggregate is a lineage node only once its identifier type has a registered node kind (ADR-2481).
        var subject = entry.Metadata.FindPrimaryKey()?.Properties is [var key] && entry.Property(key.Name).CurrentValue is IEntityId id
            && (module is null || NodeKinds.IdentifierTypes.Contains(id.GetType()))
            ? NodeRef.For(id, aggregate.VersionIndex)
            : null;
        var derived = entry.Metadata.ClrType.IsDefined(typeof(DerivedObjectAttribute), inherit: false);
        var type = module is null ? entry.Metadata.ClrType.Name : $"{module}.{entry.Metadata.ClrType.Name}";
        return new ChangedAggregate(type, KeyText(entry), aggregate.VersionIndex, action, changes, subject, derived);
    }

    private static SortedDictionary<string, object?> Snapshot(EntityEntry entry, bool modifiedOnly, bool original)
    {
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in entry.Properties)
        {
            if (modifiedOnly && !property.IsModified)
            {
                continue;
            }

            values[property.Metadata.Name] = original ? property.OriginalValue : property.CurrentValue;
        }

        foreach (var complex in entry.ComplexProperties)
        {
            foreach (var property in complex.Properties.Where(p => !modifiedOnly || p.IsModified))
            {
                values[$"{complex.Metadata.Name}.{property.Metadata.Name}"] = original ? property.OriginalValue : property.CurrentValue;
            }
        }

        return values;
    }
}
