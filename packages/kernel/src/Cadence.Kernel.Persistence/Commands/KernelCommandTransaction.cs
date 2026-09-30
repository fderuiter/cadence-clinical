using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using Npgsql;

namespace Cadence.Kernel.Persistence.Commands;

/// <summary>Points in <see cref="KernelCommandTransaction.CommitAsync"/> where tests can inject a failure.</summary>
internal enum CommitStage
{
    StateStamped,
    ProvenanceStaged,
    EventsStaged,
    AuditAppended,
    IdempotencyStaged,
    Saved,
}

/// <summary>Creates a <see cref="KernelCommandTransaction"/> with its own <see cref="KernelDbContext"/> per command.</summary>
internal sealed class KernelUnitOfWorkFactory(KernelTenantDbContextFactory contextFactory) : IKernelUnitOfWorkFactory
{
    /// <summary>Test hook: invoked at each <see cref="CommitStage"/>; throwing aborts the commit.</summary>
    internal Action<CommitStage>? FaultInjector { get; set; }

    public IKernelCommandTransaction Begin(CommandExecution execution) =>
        new KernelCommandTransaction(contextFactory.Create(execution.Command.TenantId), execution, FaultInjector);
}

/// <summary>
/// One command's persistence (spec §5.1): tracks its single aggregate (K-TX-1) and events, then writes state,
/// provenance, events, the audit link, outbox rows and the idempotency record in one PostgreSQL transaction (K-TX-2).
/// @req:PRD-SYS-053
/// </summary>
internal sealed class KernelCommandTransaction(
    KernelDbContext db,
    CommandExecution execution,
    Action<CommitStage>? faultInjector) : IKernelCommandTransaction, IKernelUnitOfWork
{
    private const string DecisionDeniedEvent = "DecisionDenied";

    private readonly List<(object Event, DateTimeOffset? OccurredValid)> _events = [];
    private (Type Type, string Key)? _claimed;

    public IKernelUnitOfWork Work => this;

    private ICommand Command => execution.Command;

    public async Task<TAggregate?> FindAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(id);
        Claim(typeof(TAggregate), KeyText(id));
        return await db.Set<TAggregate>().FindAsync([id], cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(id);
        var found = await db.Set<TAggregate>().FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (found is not null && _claimed != (typeof(TAggregate), KeyText(id)))
        {
            db.Entry(found).State = EntityState.Detached;
        }

        return found is not null;
    }

    public async Task<TAggregate?> ReadAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(id);
        var key = db.Model.FindEntityType(typeof(TAggregate))?.FindPrimaryKey()?.Properties.Single().Name
            ?? throw new InvalidOperationException($"{typeof(TAggregate).Name} is not a kernel aggregate.");
        return await db.Set<TAggregate>().AsNoTracking()
            .SingleOrDefaultAsync(e => EF.Property<object>(e, key) == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TAggregate>> ReadAllAsync<TAggregate>(
        Expression<Func<TAggregate, bool>> filter, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(filter);
        return await db.Set<TAggregate>().AsNoTracking().Where(filter).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Add(AggregateRootBase aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        var entry = db.Add(aggregate);
        Claim(aggregate.GetType(), KeyText(entry));
    }

    public void Raise(object domainEvent, DateTimeOffset? occurredValid = null)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _ = EventAttribute(domainEvent.GetType());
        _events.Add((domainEvent, occurredValid));
    }

    public Task<IdempotentOutcome?> FindPriorOutcomeAsync(CancellationToken cancellationToken) =>
        db.IdempotencyRecords.AsNoTracking()
            .Where(r => r.TenantId == Command.TenantId && r.IdempotencyKey == Command.IdempotencyKey)
            .Select(r => new IdempotentOutcome(r.RequestHash, r.ResultJson))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<CommitOutcome> CommitAsync(string resultJson, CancellationToken cancellationToken)
    {
        using var writes = db.PermitPipelineWrites();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var aggregate = StampAggregate();
            Fault(CommitStage.StateStamped);

            AddProvenance("Committed", aggregate);
            Fault(CommitStage.ProvenanceStaged);

            foreach (var (domainEvent, occurredValid) in _events)
            {
                var attribute = EventAttribute(domainEvent.GetType());
                AddEvent(attribute.Type, attribute.SchemaVersion, JsonSerializer.SerializeToElement(domainEvent, domainEvent.GetType(), KernelCommandPipelineJson.Options), occurredValid, aggregate?.Subject);
            }

            Fault(CommitStage.EventsStaged);

            await AppendAuditAsync(aggregate?.Action ?? "NoChange", aggregate, cancellationToken).ConfigureAwait(false);
            Fault(CommitStage.AuditAppended);

            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                TenantId = Command.TenantId,
                IdempotencyKey = Command.IdempotencyKey,
                CommandType = execution.CommandType,
                RequestHash = execution.RequestHash,
                ResultJson = resultJson,
                ActivityId = execution.ActivityId,
                RecordedAt = execution.RecordedAt,
            });
            Fault(CommitStage.IdempotencyStaged);

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            Fault(CommitStage.Saved);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CommitOutcome.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return CommitOutcome.ConcurrencyConflict;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return string.Equals(unique.TableName, "idempotency_records", StringComparison.Ordinal)
                ? CommitOutcome.IdempotencyRace
                : CommitOutcome.UniqueConflict;
        }
    }

    public async Task RecordDenialAsync(AuthorizationDecision denial, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(denial);
        using var writes = db.PermitPipelineWrites();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        AddProvenance("Denied", aggregate: null);
        var payload = JsonSerializer.SerializeToElement(
            new
            {
                command = execution.CommandType,
                idempotencyKey = Command.IdempotencyKey,
                reason = denial.Reason,
                conditions = denial.Conditions.Select(c => new { c.Code, c.Description, c.Met, evidence = c.Evidence.Select(e => e.ToString()) }),
            },
            KernelCommandPipelineJson.Options);
        AddEvent(DecisionDeniedEvent, 1, payload, occurredValid: null);
        await AppendAuditAsync("Denied", aggregate: null, cancellationToken, changes: payload.GetRawText()).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();

    private void Claim(Type type, string key)
    {
        if (_claimed is { } claimed && claimed != (type, key))
        {
            throw new InvalidOperationException(
                $"K-TX-1: {execution.CommandType} already changes {claimed.Type.Name} {claimed.Key}; it cannot also change {type.Name} {key}. Use an event-driven saga.");
        }

        _claimed = (type, key);
    }

    /// <summary>Stamps GxP audit fields on the one changed aggregate and captures its change set.</summary>
    private ChangedAggregate? StampAggregate()
    {
        var changed = db.ChangeTracker.Entries<AggregateRootBase>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (changed.Count > 1)
        {
            throw new InvalidOperationException($"K-TX-1: {execution.CommandType} changed {changed.Count} aggregates.");
        }

        if (changed.Count == 0)
        {
            return null;
        }

        var entry = changed[0];
        var aggregate = entry.Entity;
        string action;
        string changes;
        switch (entry.State)
        {
            case EntityState.Added:
                aggregate.CreatedAt = execution.RecordedAt.UtcDateTime;
                aggregate.CreatedBy = execution.Actor.Id;
                aggregate.ReasonForChange = Command.ReasonForChange;
                aggregate.VersionIndex = 1;
                action = "Created";
                changes = Serialize(new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["after"] = Snapshot(entry, modifiedOnly: false, original: false) });
                break;
            case EntityState.Modified:
                aggregate.ReasonForChange = Command.ReasonForChange;
                aggregate.VersionIndex++;
                action = "Updated";
                changes = Serialize(new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["before"] = Snapshot(entry, modifiedOnly: true, original: true),
                    ["after"] = Snapshot(entry, modifiedOnly: true, original: false),
                });
                break;
            default:
                action = "Deleted";
                changes = Serialize(new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["before"] = Snapshot(entry, modifiedOnly: false, original: true) });
                break;
        }

        var subject = entry.Metadata.FindPrimaryKey()?.Properties is [var key] && entry.Property(key.Name).CurrentValue is IEntityId id
            ? NodeRef.For(id, aggregate.VersionIndex)
            : null;
        return new ChangedAggregate(entry.Metadata.ClrType.Name, KeyText(entry), aggregate.VersionIndex, action, changes, subject);
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

    private void AddProvenance(string outcome, ChangedAggregate? aggregate) =>
        db.ProvenanceActivities.Add(new ProvenanceActivityRecord
        {
            Id = execution.ActivityId,
            TenantId = Command.TenantId,
            CommandType = execution.CommandType,
            ActorId = execution.Actor.Id,
            ActorKind = execution.Actor.Kind,
            RecordedAt = execution.RecordedAt,
            ReasonForChange = Command.ReasonForChange,
            IdempotencyKey = Command.IdempotencyKey,
            Outcome = outcome,
            AggregateType = aggregate?.Type,
            AggregateId = aggregate?.Id,
            CorrelationId = execution.Metadata.CorrelationId,
            CausationId = execution.Metadata.CausationId,
        });

    private void AddEvent(string type, int schemaVersion, JsonElement payload, DateTimeOffset? occurredValid, NodeRef? subject = null)
    {
        var envelope = new EventEnvelope(
            Guid.CreateVersion7(),
            type,
            schemaVersion,
            Command.TenantId,
            occurredValid,
            execution.RecordedAt,
            execution.Actor.Id,
            execution.ActivityId,
            execution.Metadata.CorrelationId,
            execution.Metadata.CausationId,
            payload,
            subject);

        db.DomainEvents.Add(new DomainEventRecord
        {
            EventId = envelope.EventId,
            TenantId = envelope.Tenant,
            Type = envelope.Type,
            SchemaVersion = envelope.SchemaVersion,
            OccurredValid = envelope.OccurredValid,
            Recorded = envelope.Recorded,
            ActorId = envelope.Actor,
            ActivityId = envelope.ActivityId,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            Payload = payload.GetRawText(),
        });
        db.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = Guid.CreateVersion7(),
            EventId = envelope.EventId,
            TenantId = envelope.Tenant,
            Type = envelope.Type,
            Envelope = JsonSerializer.Serialize(envelope, KernelCommandPipelineJson.Options),
            EnqueuedAt = envelope.Recorded,
        });
    }

    /// <summary>
    /// Appends one link to the tenant's chain. The head row is created if missing and locked <c>FOR UPDATE</c>, so
    /// concurrent commands for one tenant serialise here and the chain never forks.
    /// </summary>
    private async Task AppendAuditAsync(string action, ChangedAggregate? aggregate, CancellationToken cancellationToken, string? changes = null)
    {
        var tenant = Command.TenantId.Value;
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO kernel.audit_chain_heads (tenant_id, last_sequence, last_hash) VALUES ({tenant}, 0, '') ON CONFLICT (tenant_id) DO NOTHING",
            cancellationToken).ConfigureAwait(false);
        var head = await db.AuditChainHeads
            .FromSql($"SELECT * FROM kernel.audit_chain_heads WHERE tenant_id = {tenant} FOR UPDATE")
            .SingleAsync(cancellationToken).ConfigureAwait(false);

        var link = new AuditEntryRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = Command.TenantId,
            Sequence = head.LastSequence + 1,
            RecordedAt = execution.RecordedAt,
            ActorId = execution.Actor.Id,
            Action = action,
            AggregateType = aggregate?.Type,
            AggregateId = aggregate?.Id,
            AggregateVersion = aggregate?.Version,
            ReasonForChange = Command.ReasonForChange,
            ActivityId = execution.ActivityId,
            Changes = changes ?? aggregate?.Changes ?? "{}",
            PredecessorHash = head.LastHash,
        };
        link.Hash = KernelAuditHash.Compute(link);

        db.AuditEntries.Add(link);
        head.LastSequence = link.Sequence;
        head.LastHash = link.Hash;
    }

    private void Fault(CommitStage stage) => faultInjector?.Invoke(stage);

    private static KernelEventAttribute EventAttribute(Type eventType) =>
        eventType.GetCustomAttribute<KernelEventAttribute>()
        ?? throw new InvalidOperationException($"{eventType.Name} must declare [KernelEvent(type, schemaVersion)].");

    private static string KeyText(EntityEntry entry) =>
        string.Join("|", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => KeyText(entry.Property(p.Name).CurrentValue!)));

    private static string KeyText(object id) => id is IEntityId entityId ? entityId.Value.ToString("D") : id.ToString()!;

    private static string Serialize(object value) => JsonSerializer.Serialize(value, KernelCommandPipelineJson.Options);

    private sealed record ChangedAggregate(string Type, string Id, long Version, string Action, string Changes, NodeRef? Subject);
}

/// <summary>JSON options shared by kernel persistence for event payloads, envelopes and audit change sets.</summary>
internal static class KernelCommandPipelineJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
