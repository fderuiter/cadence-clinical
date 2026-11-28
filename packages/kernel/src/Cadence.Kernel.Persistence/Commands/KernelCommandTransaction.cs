using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Identity;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence.IdentityVault;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

namespace Cadence.Kernel.Persistence.Commands;

/// <summary>
/// One command's persistence (spec §5.1): tracks its single aggregate (K-TX-1), or a form instance and the observations
/// it holds (K-TX-1 as amended by ADR-2622, <see cref="LockCollectionAsync"/>), its declared lineage inputs and its
/// events, then writes state, provenance, lineage, events, the audit link, outbox rows, the idempotency record and any
/// staged IdentityVault writes in one PostgreSQL transaction (K-TX-2, K-LIN-2, ADR-2458). The aggregate may be the
/// kernel's or a module's: a module's context is opened on this command's connection and enlisted in the same
/// transaction, so its rows commit or roll back with the kernel's (ADR-2600).
/// </summary>
internal sealed class KernelCommandTransaction(
    KernelDbContext db,
    CommandExecution execution,
    Action<CommitStage>? faultInjector,
    IReadOnlyList<IModuleCommandStore>? moduleStores = null) : IKernelCommandTransaction, IKernelUnitOfWork, IIdentityVaultStaging
{
    private const string DecisionDeniedEvent = "DecisionDenied";

    private readonly List<(object Event, DateTimeOffset? OccurredValid)> _events = [];
    private readonly List<(NodeRef Input, LineageEdgeKind Kind, DateTimeOffset? ValidFrom, NodeRef? Target)> _inputs = [];
    private readonly Dictionary<NodeRef, bool> _parts = [];
    private readonly List<(NodeRef From, NodeRef To, LineageEdgeKind Kind)> _partEdges = [];
    private readonly List<IIdentityVaultWrite> _vaultWrites = [];
    private readonly List<(IModuleCommandStore Store, ModuleDbContext Context)> _modules = [];
    private readonly HashSet<(Type Type, string Key)> _claimed = [];
    private CollectionInstanceId? _collection;
    private long? _lockKey;
    private bool _scopeLocked;
    private NodeRef? _lockedScope;
    private readonly List<long> _sharedKeys = [];
    private bool _connectionHeld;
    private AuthorizationDecision? _permit;
    private NodeRef? _proposal;

    public IKernelUnitOfWork Work => this;

    private ICommand Command => execution.Command;

    public async Task<TAggregate?> FindAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(id);
        Claim(typeof(TAggregate), KernelCommandAggregateStamper.KeyText(id));
        var store = await StoreForAsync(typeof(TAggregate), cancellationToken).ConfigureAwait(false);
        return await store.Set<TAggregate>().FindAsync([id], cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(id);
        var store = await StoreForAsync(typeof(TAggregate), cancellationToken).ConfigureAwait(false);
        var found = await store.Set<TAggregate>().FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (found is not null && !_claimed.Contains((typeof(TAggregate), KernelCommandAggregateStamper.KeyText(id))))
        {
            store.Entry(found).State = EntityState.Detached;
        }

        return found is not null;
    }

    public async Task<TAggregate?> ReadAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(id);
        var store = await StoreForAsync(typeof(TAggregate), cancellationToken).ConfigureAwait(false);
        var key = store.Model.FindEntityType(typeof(TAggregate))?.FindPrimaryKey()?.Properties.Single().Name
            ?? throw new InvalidOperationException($"{typeof(TAggregate).Name} has no single-column key.");
        return await store.Set<TAggregate>().AsNoTracking()
            .SingleOrDefaultAsync(e => EF.Property<object>(e, key) == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TAggregate>> ReadAllAsync<TAggregate>(
        Expression<Func<TAggregate, bool>> filter, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(filter);
        var store = await StoreForAsync(typeof(TAggregate), cancellationToken).ConfigureAwait(false);
        return await store.Set<TAggregate>().AsNoTracking().Where(filter).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task LockAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(id);
        await LockExclusiveAsync(AdvisoryLockKey(Command.TenantId, typeof(TAggregate), KernelCommandAggregateStamper.KeyText(id)), cancellationToken).ConfigureAwait(false);
    }

    public async Task LockCollectionAsync(CollectionInstanceId instance, CancellationToken cancellationToken)
    {
        if (_claimed.Count > 0 || _sharedKeys.Count > 0)
        {
            throw new InvalidOperationException(
                $"K-TX-1: {execution.CommandType} must lock its form instance before it reads, adds or locks anything else (ADR-2622).");
        }

        await LockExclusiveAsync(AdvisoryLockKey(Command.TenantId, typeof(CollectionInstance), KernelCommandAggregateStamper.KeyText(instance)), cancellationToken).ConfigureAwait(false);
        _collection = instance;
    }

    public async Task ShareScopesAsync(IReadOnlyList<NodeRef> scopes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        if (_scopeLocked)
        {
            throw new InvalidOperationException(
                $"K-TX-4: {execution.CommandType} holds a lock scope exclusively; it cannot also share scope locks, so locks cannot deadlock (ADR-2621).");
        }

        await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var key in scopes.Select(scope => ScopeLockKey(Command.TenantId, scope)).Where(key => !_sharedKeys.Contains(key)))
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock_shared({key})", cancellationToken).ConfigureAwait(false);
            _sharedKeys.Add(key);
        }
    }

    public async Task LockScopeAsync(NodeRef scope, CancellationToken cancellationToken)
    {
        await LockExclusiveAsync(ScopeLockKey(Command.TenantId, scope), cancellationToken).ConfigureAwait(false);
        _scopeLocked = true;
        _lockedScope = scope;
    }

    private async Task LockExclusiveAsync(long key, CancellationToken cancellationToken)
    {
        if (_lockKey is { } held)
        {
            if (held == key)
            {
                return;
            }

            throw new InvalidOperationException(
                $"K-TX-4: {execution.CommandType} already holds an exclusive lock; a command takes at most one, so locks cannot deadlock.");
        }

        if (_sharedKeys.Count > 0)
        {
            throw new InvalidOperationException(
                $"K-TX-4: {execution.CommandType} shares scope locks; it cannot also take an exclusive lock (ADR-2621).");
        }

        await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({key})", cancellationToken).ConfigureAwait(false);
        _lockKey = key;
    }

    public void Add(AggregateRootBase aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        var entry = StoreFor(aggregate.GetType()).Add(aggregate);
        Claim(aggregate.GetType(), KernelCommandAggregateStamper.KeyText(entry));
    }

    public void Raise(object domainEvent, DateTimeOffset? occurredValid = null)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _ = KernelCommandAuditAndOutboxStager.EventAttribute(domainEvent.GetType());
        _events.Add((domainEvent, occurredValid));
    }

    public void LinkInput(NodeRef input, LineageEdgeKind kind, DateTimeOffset? validFrom = null, NodeRef? target = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Version is null)
        {
            throw new ArgumentException($"K-LIN: lineage inputs name a version; '{input}' does not.", nameof(input));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown lineage edge kind.");
        }

        if (!_inputs.Contains((input, kind, validFrom, target)))
        {
            _inputs.Add((input, kind, validFrom, target));
        }
    }

    public void IncludePart(NodeRef part, bool newVersion)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (part.Version is null)
        {
            throw new ArgumentException($"K-LIN: parts name a version; '{part}' does not.", nameof(part));
        }

        if (_parts.TryGetValue(part, out var declared) && declared != newVersion)
        {
            throw new InvalidOperationException($"K-LIN: {part} is declared both as a new version and as an existing one.");
        }

        _parts[part] = newVersion;
    }

    public void LinkPart(NodeRef from, NodeRef to, LineageEdgeKind kind)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from.Version is null || to.Version is null)
        {
            throw new ArgumentException($"K-LIN: lineage edges join versions; '{from}' → '{to}' does not.", nameof(from));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown lineage edge kind.");
        }

        if (from == to)
        {
            throw new ArgumentException($"K-LIN: {from} cannot depend on itself.", nameof(to));
        }

        if (!_partEdges.Contains((from, to, kind)))
        {
            _partEdges.Add((from, to, kind));
        }
    }

    public void StageVaultWrite(IIdentityVaultWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        _vaultWrites.Add(write);
    }

    public void RecordPermit(AuthorizationDecision permit)
    {
        ArgumentNullException.ThrowIfNull(permit);
        _permit = permit;
    }

    public void RecordProposalExecution(NodeRef proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.Kind != NodeKinds.Proposal || proposal.Version is null)
        {
            throw new ArgumentException($"K-AI-2: '{proposal}' is not a proposal version.", nameof(proposal));
        }

        _proposal = proposal;
    }

    public Task<IdempotentOutcome?> FindPriorOutcomeAsync(CancellationToken cancellationToken) =>
        db.IdempotencyRecords.AsNoTracking()
            .Where(r => r.TenantId == Command.TenantId && r.IdempotencyKey == Command.IdempotencyKey)
            .Select(r => new IdempotentOutcome(r.RequestHash, r.ResultJson))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<CommitOutcome> CommitAsync(string resultJson, CancellationToken cancellationToken)
    {
        using var writes = db.PermitPipelineWrites();
#pragma warning disable RS0030 // Kernel contexts register no retrying execution strategy (KernelDatabase.UseKernelNpgsql; KernelExecutionStrategyTests), so EF permits this transaction and the commit pipeline keeps its own conflict handling.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore RS0030
        foreach (var (_, context) in _modules)
        {
            await context.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken).ConfigureAwait(false);
        }

        ChangedAggregate? aggregate = null;
        try
        {
            var changed = KernelCommandAggregateStamper.StampAggregates(db, _modules, execution, _scopeLocked, _lockedScope, _collection);
            aggregate = changed.FirstOrDefault();
            Fault(CommitStage.StateStamped);

            KernelCommandAuditAndOutboxStager.AddProvenance(db, execution, "Committed", aggregate, _permit, _proposal);
            Fault(CommitStage.ProvenanceStaged);

            KernelCommandLineageStager.StageAllLineage(db, execution, changed, aggregate, _inputs, _parts, _partEdges, _proposal);
            Fault(CommitStage.LineageStaged);

            foreach (var (domainEvent, occurredValid) in _events)
            {
                var attribute = KernelCommandAuditAndOutboxStager.EventAttribute(domainEvent.GetType());
                KernelCommandAuditAndOutboxStager.AddEvent(
                    db,
                    execution,
                    attribute.Type,
                    attribute.SchemaVersion,
                    JsonSerializer.SerializeToElement(domainEvent, domainEvent.GetType(), KernelCommandPipelineJson.Options),
                    occurredValid,
                    aggregate?.Subject);
            }

            Fault(CommitStage.EventsStaged);

            AuditEntryRecord? primaryAudit = null;
            if (changed.Count == 0)
            {
                primaryAudit = await KernelCommandAuditAndOutboxStager.AppendAuditAsync(db, execution, "NoChange", null, cancellationToken).ConfigureAwait(false);
            }

            foreach (var written in changed)
            {
                var appended = await KernelCommandAuditAndOutboxStager.AppendAuditAsync(db, execution, written.Action, written, cancellationToken).ConfigureAwait(false);
                primaryAudit ??= appended;
            }

            await KernelCommandAuditAndOutboxStager.StageBiostatExportAuditAsync(
                db,
                execution,
                outcome: "Committed",
                auditEntryHash: primaryAudit?.Hash ?? string.Empty,
                failureReason: null,
                cancellationToken).ConfigureAwait(false);

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

            // The module's rows first: a stale version writes no row and fails the command before any kernel row.
            foreach (var (_, context) in _modules)
            {
                using var moduleWrites = context.PermitCommandWrites();
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            Fault(CommitStage.ModuleWritten);

            if (KernelCommandAggregateStamper.IsStudyLockedProtocolImport(Command, _scopeLocked, _lockedScope, out _)
                && !await KernelCommandLockAndStoreHelper.ReopenInReviewProtocolVersionsAsync(db, Command.TenantId, cancellationToken).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return CommitOutcome.ConcurrencyConflict;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            Fault(CommitStage.Saved);

            foreach (var write in _vaultWrites)
            {
                await IdentityVaultSql.ApplyAsync(db, write, execution.RecordedAt, cancellationToken).ConfigureAwait(false);
            }

            Fault(CommitStage.VaultWritten);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CommitOutcome.Committed;
        }
        catch (PostgresException ex) when (ex.SqlState is IdentityVaultSql.ErasedSqlState or IdentityVaultSql.LinkConflictSqlState)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return CommitOutcome.ConcurrencyConflict;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return CommitOutcome.ConcurrencyConflict;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return KernelCommandAggregateStamper.UniqueViolationOutcome(unique.TableName, aggregate);
        }
    }

    public async Task RecordDenialAsync(AuthorizationDecision denial, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(denial);
        using var writes = db.PermitPipelineWrites();
#pragma warning disable RS0030 // Kernel contexts register no retrying execution strategy (KernelDatabase.UseKernelNpgsql; KernelExecutionStrategyTests), so EF permits this transaction and the commit pipeline keeps its own conflict handling.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore RS0030

        KernelCommandAuditAndOutboxStager.AddProvenance(db, execution, "Denied", aggregate: null, denial, _proposal);
        var payload = KernelCommandAuditAndOutboxStager.BuildDenialPayload(execution, denial);
        KernelCommandAuditAndOutboxStager.AddEvent(db, execution, DecisionDeniedEvent, 1, payload, occurredValid: null);
        var deniedAudit = await KernelCommandAuditAndOutboxStager.AppendAuditAsync(db, execution, "Denied", aggregate: null, cancellationToken, changes: payload.GetRawText()).ConfigureAwait(false);
        await KernelCommandAuditAndOutboxStager.StageBiostatExportAuditAsync(
            db,
            execution,
            outcome: "Denied",
            auditEntryHash: deniedAudit.Hash,
            failureReason: denial.Reason,
            cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordRefusalAsync(KernelError refusal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        // Nothing the handler read or staged is written: the refusal is the only row besides its provenance.
        db.ChangeTracker.Clear();
        _events.Clear();
        _inputs.Clear();
        _parts.Clear();
        _partEdges.Clear();
        _vaultWrites.Clear();

        using var writes = db.PermitPipelineWrites();
#pragma warning disable RS0030 // Kernel contexts register no retrying execution strategy (KernelDatabase.UseKernelNpgsql; KernelExecutionStrategyTests), so EF permits this transaction and the commit pipeline keeps its own conflict handling.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore RS0030

        KernelCommandAuditAndOutboxStager.AddProvenance(db, execution, "Refused", aggregate: null, _permit, _proposal);
        var payload = KernelCommandAuditAndOutboxStager.BuildRefusalPayload(execution, refusal);
        var refusedAudit = await KernelCommandAuditAndOutboxStager.AppendAuditAsync(db, execution, "Refused", aggregate: null, cancellationToken, changes: payload.GetRawText()).ConfigureAwait(false);
        await KernelCommandAuditAndOutboxStager.StageBiostatExportAuditAsync(
            db,
            execution,
            outcome: "Refused",
            auditEntryHash: refusedAudit.Hash,
            failureReason: refusal.Message,
            cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_lockKey is { } key)
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({key})").ConfigureAwait(false);
            }

            foreach (var shared in _sharedKeys)
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock_shared({shared})").ConfigureAwait(false);
            }
        }
        catch (NpgsqlException)
        {
            // A broken connection has already lost its session locks; a healthy one is reset when it returns to the pool.
        }
        finally
        {
            foreach (var (_, context) in _modules)
            {
                await context.DisposeAsync().ConfigureAwait(false);
            }

            if (_connectionHeld)
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }

            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HoldConnectionAsync(CancellationToken cancellationToken)
    {
        if (!_connectionHeld)
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            _connectionHeld = true;
        }
    }

    private async Task<DbContext> StoreForAsync(Type aggregateType, CancellationToken cancellationToken)
    {
        var store = StoreFor(aggregateType);
        if (!ReferenceEquals(store, db))
        {
            await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        return store;
    }

    private DbContext StoreFor(Type aggregateType) =>
        KernelCommandLockAndStoreHelper.ResolveStore(db, _modules, moduleStores, aggregateType);

    internal static long AdvisoryLockKey(TenantId tenant, Type aggregateType, string id) =>
        KernelCommandLockAndStoreHelper.AdvisoryLockKey(tenant, aggregateType, id);

    internal static long ScopeLockKey(TenantId tenant, NodeRef scope) =>
        KernelCommandLockAndStoreHelper.ScopeLockKey(tenant, scope);

    private void Claim(Type type, string key) =>
        KernelCommandAggregateStamper.Claim(execution, _claimed, _collection, _scopeLocked, _lockedScope, type, key);

    private void Fault(CommitStage stage) => faultInjector?.Invoke(stage);
}
