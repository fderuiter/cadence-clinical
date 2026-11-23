using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Identity;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Events;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence.IdentityVault;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

namespace Cadence.Kernel.Persistence.Commands;

/// <summary>Points in <see cref="KernelCommandTransaction.CommitAsync"/> where tests can inject a failure.</summary>
internal enum CommitStage
{
    StateStamped,
    ProvenanceStaged,
    LineageStaged,
    EventsStaged,
    AuditAppended,
    IdempotencyStaged,
    ModuleWritten,
    Saved,
    VaultWritten,
}

/// <summary>
/// Creates a <see cref="KernelCommandTransaction"/> with its own <see cref="KernelDbContext"/> per command, able to open
/// the registered module stores on the same connection (ADR-2600).
/// </summary>
internal sealed class KernelUnitOfWorkFactory(
    KernelTenantDbContextFactory contextFactory,
    IEnumerable<IModuleCommandStore> moduleStores) : IKernelUnitOfWorkFactory
{
    private readonly IModuleCommandStore[] _moduleStores = [.. moduleStores];

    /// <summary>Test hook: invoked at each <see cref="CommitStage"/>; throwing aborts the commit.</summary>
    internal Action<CommitStage>? FaultInjector { get; set; }

    public IKernelCommandTransaction Begin(CommandExecution execution) =>
        KernelCommandTransaction.Start(contextFactory.Create(execution.Command.TenantId), execution, FaultInjector, _moduleStores);
}

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
    private static readonly AsyncLocal<KernelCommandTransaction?> Ambient = new();

    private readonly List<(object Event, DateTimeOffset? OccurredValid, NodeRef? Subject)> _events = [];
    private readonly List<(NodeRef Input, LineageEdgeKind Kind, DateTimeOffset? ValidFrom, NodeRef? Target)> _inputs = [];
    private readonly Dictionary<NodeRef, bool> _parts = [];
    private readonly List<(NodeRef From, NodeRef To, LineageEdgeKind Kind)> _partEdges = [];
    private readonly List<IIdentityVaultWrite> _vaultWrites = [];
    private readonly List<(IModuleCommandStore Store, ModuleDbContext Context)> _modules = [];
    private readonly HashSet<(Type Type, string Key)> _claimed = [];
    private readonly List<(Type Type, string Key, bool Added)> _claimLog = [];
    private KernelCommandTransaction? _previousAmbient;
    private int _preCommitReadState;
    private CollectionInstanceId? _collection;
    private long? _lockKey;
    private bool _scopeLocked;
    private readonly List<long> _sharedKeys = [];
    private bool _connectionHeld;
    private AuthorizationDecision? _permit;
    private NodeRef? _proposal;

    internal static KernelCommandTransaction? Current => Ambient.Value;

    public IKernelUnitOfWork Work => this;

    private ICommand Command => execution.Command;

    internal static KernelCommandTransaction Start(
        KernelDbContext db,
        CommandExecution execution,
        Action<CommitStage>? faultInjector,
        IReadOnlyList<IModuleCommandStore>? moduleStores = null)
    {
        var tx = new KernelCommandTransaction(db, execution, faultInjector, moduleStores)
        {
            _previousAmbient = Ambient.Value,
        };
        Ambient.Value = tx;
        return tx;
    }

    internal async ValueTask<KernelTenantReadLease?> TryAcquireSharedReadLeaseAsync(
        TenantId tenant,
        CancellationToken cancellationToken)
    {
        if (Command.TenantId != tenant || Interlocked.CompareExchange(ref _preCommitReadState, 1, 0) != 0)
        {
            return null;
        }

        try
        {
            await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
            return new KernelTenantReadLease(
                db,
                onRelease: () => Interlocked.CompareExchange(ref _preCommitReadState, 0, 1),
                ownsContext: false);
        }
        catch
        {
            Interlocked.CompareExchange(ref _preCommitReadState, 0, 1);
            throw;
        }
    }

    public async Task<TAggregate?> FindAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(id);
        Claim(typeof(TAggregate), KeyText(id));
        var store = await StoreForAsync(typeof(TAggregate), cancellationToken).ConfigureAwait(false);
        return await store.Set<TAggregate>().FindAsync([id], cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync<TAggregate>(object id, CancellationToken cancellationToken)
        where TAggregate : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(id);
        var store = await StoreForAsync(typeof(TAggregate), cancellationToken).ConfigureAwait(false);
        var found = await store.Set<TAggregate>().FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (found is not null && !_claimed.Contains((typeof(TAggregate), KeyText(id))))
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
        await LockExclusiveAsync(AdvisoryLockKey(Command.TenantId, typeof(TAggregate), KeyText(id)), cancellationToken).ConfigureAwait(false);
    }

    public async Task LockCollectionAsync(CollectionInstanceId instance, CancellationToken cancellationToken)
    {
        if (_claimed.Count > 0 || _sharedKeys.Count > 0)
        {
            throw new InvalidOperationException(
                $"K-TX-1: {execution.CommandType} must lock its form instance before it reads, adds or locks anything else (ADR-2622).");
        }

        await LockExclusiveAsync(AdvisoryLockKey(Command.TenantId, typeof(CollectionInstance), KeyText(instance)), cancellationToken).ConfigureAwait(false);
        _collection = instance;
    }

    public async Task ShareScopesAsync(IReadOnlyList<NodeRef> scopes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        // An aggregate's or form instance's exclusive lock may come first: nothing that holds a shared scope lock waits
        // for one, and a lock command holds its exclusive scope lock alone, so this cannot deadlock (ADR-2622). A scope's
        // exclusive lock excludes shared ones (ADR-2621).
        if (_scopeLocked)
        {
            throw new InvalidOperationException(
                $"K-TX-4: {execution.CommandType} holds a lock scope exclusively; it cannot also share scope locks, so locks cannot deadlock (ADR-2621).");
        }

        await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var key in scopes.Select(scope => ScopeLockKey(Command.TenantId, scope)).Where(key => !_sharedKeys.Contains(key)))
        {
            // Shared locks never wait on each other, only on a lock command's exclusive one, which holds nothing else.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock_shared({key})", cancellationToken).ConfigureAwait(false);
            _sharedKeys.Add(key);
        }
    }

    public async Task LockScopeAsync(NodeRef scope, CancellationToken cancellationToken)
    {
        await LockExclusiveAsync(ScopeLockKey(Command.TenantId, scope), cancellationToken).ConfigureAwait(false);
        _scopeLocked = true;
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

        // A session lock on a connection this command keeps open: the handler's reads, its commit and the release all run
        // on it. DisposeAsync releases the lock after the commit or failure, and Npgsql resets pooled connections anyway.
        await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({key})", cancellationToken).ConfigureAwait(false);
        _lockKey = key;
    }

    public void Add(AggregateRootBase aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        var store = StoreFor(aggregate.GetType());
        var autoDetect = store.ChangeTracker.AutoDetectChangesEnabled;
        store.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var entry = store.Add(aggregate);
            Claim(aggregate.GetType(), KeyText(entry), added: true);
        }
        finally
        {
            store.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    public void Raise(object domainEvent, DateTimeOffset? occurredValid = null)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _ = EventAttribute(domainEvent.GetType());
        _events.Add((domainEvent, occurredValid, null));
    }

    public void Raise(object domainEvent, NodeRef subject, DateTimeOffset? occurredValid = null)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(subject);
        _ = EventAttribute(domainEvent.GetType());
        _events.Add((domainEvent, occurredValid, subject));
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

    public async Task<IdempotentOutcome?> FindPriorOutcomeAsync(CancellationToken cancellationToken)
    {
        await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await db.IdempotencyRecords.AsNoTracking()
            .Where(r => r.TenantId == Command.TenantId && r.IdempotencyKey == Command.IdempotencyKey)
            .Select(r => new IdempotentOutcome(r.RequestHash, r.ResultJson))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CommitOutcome> CommitAsync(string resultJson, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _preCommitReadState, 2);
        await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
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
            db.ChangeTracker.DetectChanges();
            foreach (var (_, context) in _modules)
            {
                context.ChangeTracker.DetectChanges();
            }

            var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                var changed = StampAggregates();
                aggregate = changed.FirstOrDefault();
                Fault(CommitStage.StateStamped);

                AddProvenance("Committed", aggregate, _permit);
                Fault(CommitStage.ProvenanceStaged);

                StageAllLineage(changed, aggregate);

                Fault(CommitStage.LineageStaged);

                foreach (var (domainEvent, occurredValid, explicitSubject) in _events)
                {
                    var attribute = EventAttribute(domainEvent.GetType());
                    var eventSubject = explicitSubject
                        ?? (domainEvent is ConceptDefined conceptDefined
                            ? NodeRef.For(conceptDefined.Concept.ConceptId, conceptDefined.Concept.Version)
                            : aggregate?.Subject);
                    AddEvent(attribute.Type, attribute.SchemaVersion, JsonSerializer.SerializeToElement(domainEvent, domainEvent.GetType(), KernelCommandPipelineJson.Options), occurredValid, eventSubject);
                }

                Fault(CommitStage.EventsStaged);

                if (changed.Count == 0)
                {
                    await AppendAuditBatchAsync([("NoChange", null, null)], cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await AppendAuditBatchAsync(
                        [.. changed.Select(written => (written.Action, (ChangedAggregate?)written, (string?)null))],
                        cancellationToken).ConfigureAwait(false);
                }

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
            }
            finally
            {
                db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
            }

            // The module's rows first: a stale version writes no row and fails the command before any kernel row.
            foreach (var (_, context) in _modules)
            {
                using var moduleWrites = context.PermitCommandWrites();
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            Fault(CommitStage.ModuleWritten);

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
            // Another command erased the record or linked the participant after this handler read the vault; a retry
            // re-reads it and fails with the matching error.
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
            return UniqueViolationOutcome(unique.TableName, aggregate);
        }
    }

    /// <summary>
    /// Classifies a unique violation. Two commands that update the same aggregate version both write its next lineage
    /// node, and PostgreSQL may report that duplicate before the row-version check, so on an update a lineage duplicate
    /// is the same concurrency conflict.
    /// </summary>
    private static CommitOutcome UniqueViolationOutcome(string? table, ChangedAggregate? aggregate)
    {
        if (string.Equals(table, "idempotency_records", StringComparison.Ordinal))
        {
            return CommitOutcome.IdempotencyRace;
        }

        return aggregate?.Action == "Updated" && table is "lineage_nodes" or "lineage_edges"
            ? CommitOutcome.ConcurrencyConflict
            : CommitOutcome.UniqueConflict;
    }

    public async Task RecordDenialAsync(AuthorizationDecision denial, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(denial);
        Volatile.Write(ref _preCommitReadState, 2);
        await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var writes = db.PermitPipelineWrites();
#pragma warning disable RS0030 // Kernel contexts register no retrying execution strategy (KernelDatabase.UseKernelNpgsql; KernelExecutionStrategyTests), so EF permits this transaction and the commit pipeline keeps its own conflict handling.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore RS0030

        AddProvenance("Denied", aggregate: null, denial);
        var payload = JsonSerializer.SerializeToElement(
            new
            {
                command = execution.CommandType,
                idempotencyKey = Command.IdempotencyKey,
                reason = denial.Reason,
                conditions = Conditions(denial),
            },
            KernelCommandPipelineJson.Options);
        AddEvent(DecisionDeniedEvent, 1, payload, occurredValid: null);
        await AppendAuditAsync("Denied", aggregate: null, cancellationToken, changes: payload.GetRawText()).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordRefusalAsync(KernelError refusal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        Volatile.Write(ref _preCommitReadState, 2);

        // Nothing the handler read or staged is written: the refusal is the only row besides its provenance.
        db.ChangeTracker.Clear();
        _events.Clear();
        _inputs.Clear();
        _parts.Clear();
        _partEdges.Clear();
        _vaultWrites.Clear();

        await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var writes = db.PermitPipelineWrites();
#pragma warning disable RS0030 // Kernel contexts register no retrying execution strategy (KernelDatabase.UseKernelNpgsql; KernelExecutionStrategyTests), so EF permits this transaction and the commit pipeline keeps its own conflict handling.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore RS0030

        AddProvenance("Refused", aggregate: null, _permit);
        var payload = JsonSerializer.SerializeToElement(
            new
            {
                command = execution.CommandType,
                idempotencyKey = Command.IdempotencyKey,
                code = refusal.Code,
                reason = refusal.Message,
                producer = refusal.Explanation?.Producer,
                conditions = refusal.Explanation?.Conditions.Select(c => new { c.Code, c.Description, c.Met, evidence = c.Evidence.Select(e => e.ToString()) }),
            },
            KernelCommandPipelineJson.Options);
        await AppendAuditAsync("Refused", aggregate: null, cancellationToken, changes: payload.GetRawText()).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _preCommitReadState, 2);
        if (ReferenceEquals(Ambient.Value, this))
        {
            Ambient.Value = _previousAmbient;
        }

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

    /// <summary>
    /// Opens this command's connection and keeps it open until <see cref="DisposeAsync"/>. The kernel context opens it,
    /// so its tenant interceptor scopes the session before a lock or any module context uses it (K-ID-2).
    /// </summary>
    private async Task HoldConnectionAsync(CancellationToken cancellationToken)
    {
        if (!_connectionHeld)
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            _connectionHeld = true;
        }
    }

    /// <summary>The context that maps <paramref name="aggregateType"/>, keeping this command's connection open.</summary>
    private async Task<DbContext> StoreForAsync(Type aggregateType, CancellationToken cancellationToken)
    {
        await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
        return StoreFor(aggregateType);
    }

    /// <summary>
    /// The context that maps <paramref name="aggregateType"/>: the kernel's, or a registered module's opened on this
    /// command's connection (ADR-2600).
    /// </summary>
    private DbContext StoreFor(Type aggregateType)
    {
        if (db.Model.FindEntityType(aggregateType) is not null)
        {
            return db;
        }

        foreach (var (_, context) in _modules)
        {
            if (context.Model.FindEntityType(aggregateType) is not null)
            {
                return context;
            }
        }

        foreach (var store in moduleStores ?? [])
        {
            if (_modules.Exists(m => ReferenceEquals(m.Store, store)))
            {
                continue;
            }

            var context = store.Create(db.Database.GetDbConnection());
            _modules.Add((store, context));
            if (context.Model.FindEntityType(aggregateType) is not null)
            {
                return context;
            }
        }

        throw new InvalidOperationException(
            $"{aggregateType.Name} is mapped neither by the kernel nor by a registered module command store (ADR-2600).");
    }

    /// <summary>
    /// The advisory lock key of one aggregate in one tenant: the first eight bytes of SHA-256 over
    /// <c>tenant:type:id</c>, so locks of different tenants and aggregate types never collide in practice.
    /// </summary>
    internal static long AdvisoryLockKey(TenantId tenant, Type aggregateType, string id)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenant}:{aggregateType.FullName}:{id}"));
        return BinaryPrimitives.ReadInt64BigEndian(digest);
    }

    /// <summary>
    /// The advisory lock key of one lock scope in one tenant (ADR-2621), in its own namespace so it never collides with an
    /// aggregate's key: a study's scope lock is not the study aggregate's numbering lock.
    /// </summary>
    internal static long ScopeLockKey(TenantId tenant, NodeRef scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenant}:lock-scope:{scope.Kind}:{scope.Id}"));
        return BinaryPrimitives.ReadInt64BigEndian(digest);
    }

    private void Claim(Type type, string key, bool added = false)
    {
        if (_collection is { } instance)
        {
            // K-TX-1 as amended (ADR-2622): the form instance and observations, which the commit checks are its members.
            if (!(type == typeof(CollectionInstance) && key == KeyText(instance)) && type != typeof(Observation))
            {
                throw new InvalidOperationException(
                    $"K-TX-1: {execution.CommandType} changes form instance {instance}; it can change only that instance and its observations, not {type.Name} {key}.");
            }

            _claimed.Add((type, key));
            _claimLog.Add((type, key, added));
            return;
        }

        if (_scopeLocked
            && (type == typeof(Assertion) || type == typeof(Attestation))
            && _claimed.All(c => c.Type == typeof(Assertion) || c.Type == typeof(Attestation))
            && !_claimed.Any(c => c.Type == type && c.Key != key))
        {
            _claimed.Add((type, key));
            _claimLog.Add((type, key, added));
            return;
        }

        if (added
            && type == typeof(Concept)
            && _claimLog.All(c => c.Added && c.Type == typeof(Concept))
            && !_claimed.Contains((type, key)))
        {
            _claimed.Add((type, key));
            _claimLog.Add((type, key, true));
            return;
        }

        if (_claimed.Count > 0 && (!_claimed.Contains((type, key)) || _claimLog.Count > 1))
        {
            var claimed = _claimLog[0];
            throw new InvalidOperationException(
                $"K-TX-1: {execution.CommandType} already changes {claimed.Type.Name} {claimed.Key}; it cannot also change {type.Name} {key}. Use an event-driven saga.");
        }

        if (_claimed.Add((type, key)))
        {
            _claimLog.Add((type, key, added));
        }
    }

    /// <summary>
    /// Stamps GxP audit fields on the changed aggregates and captures their change sets, the primary one first: the one
    /// changed aggregate (K-TX-1), a form instance followed by the observations a form save changed (ADR-2622), a
    /// scope-locked lock/release assertion paired with its inline electronic signature (ADR-2621), or a homogeneous batch
    /// of newly defined concepts.
    /// </summary>
    private List<ChangedAggregate> StampAggregates()
    {
        var changed = db.ChangeTracker.Entries<AggregateRootBase>()
            .Select(e => (Entry: e, Module: (string?)null))
            .Concat(_modules.SelectMany(m => m.Context.ChangeTracker.Entries<AggregateRootBase>().Select(e => (Entry: e, Module: (string?)m.Store.Module))))
            .Where(e => e.Entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (changed.Count > 1)
        {
            if (_scopeLocked)
            {
                changed = OrderSignedLockAssertion(changed);
            }
            else if (_collection is not null)
            {
                changed = OrderFormSave(changed);
            }
            else
            {
                changed = OrderConceptBatchCreation(changed);
            }
        }

        return [.. changed.Select(c => Stamp(c.Entry, c.Module))];
    }

    /// <summary>
    /// Orders a homogeneous batch of newly added <see cref="Concept"/> aggregates in the deterministic order the handler
    /// staged them; rejects mixed aggregate types or non-added concept mutations under K-TX-1.
    /// </summary>
    private List<(EntityEntry<AggregateRootBase> Entry, string? Module)> OrderConceptBatchCreation(
        List<(EntityEntry<AggregateRootBase> Entry, string? Module)> changed)
    {
        if (changed.Any(c => c.Module is not null || c.Entry.State != EntityState.Added || c.Entry.Entity is not Concept))
        {
            throw new InvalidOperationException($"K-TX-1: {execution.CommandType} changed {changed.Count} aggregates.");
        }

        var orderByKey = new Dictionary<string, int>(_claimLog.Count, StringComparer.Ordinal);
        for (int i = 0; i < _claimLog.Count; i++)
        {
            orderByKey.TryAdd(_claimLog[i].Key, i);
        }

        return [.. changed.OrderBy(c => orderByKey.GetValueOrDefault(KeyText(c.Entry), int.MaxValue))];
    }

    /// <summary>
    /// The aggregates of an atomic signed lock or lock release: the lock/release assertion first, followed by the
    /// attestation bound to that assertion (ADR-2621).
    /// </summary>
    private List<(EntityEntry<AggregateRootBase> Entry, string? Module)> OrderSignedLockAssertion(
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

    /// <summary>
    /// The aggregates of a form save, the instance first and its observations by id; anything else is a K-TX-1 defect.
    /// </summary>
    private List<(EntityEntry<AggregateRootBase> Entry, string? Module)> OrderFormSave(List<(EntityEntry<AggregateRootBase> Entry, string? Module)> changed)
    {
        var instance = _collection is { } id
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

    private ChangedAggregate Stamp(EntityEntry<AggregateRootBase> entry, string? module)
    {
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
                entry.DetectChanges();
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

    private static object Conditions(AuthorizationDecision decision) =>
        decision.Conditions.Select(c => new { c.Code, c.Description, c.Met, evidence = c.Evidence.Select(e => e.ToString()) });

    private void AddProvenance(string outcome, ChangedAggregate? aggregate, AuthorizationDecision? decision) =>
        db.ProvenanceActivities.Add(new ProvenanceActivityRecord
        {
            Id = execution.ActivityId,
            TenantId = Command.TenantId,
            CommandType = execution.CommandType,
            ActorId = execution.Actor.Id,
            ActorKind = execution.Actor.Kind,
            RequestedBy = execution.Actor.RequestedBy?.Id,
            RecordedAt = execution.RecordedAt,
            ReasonForChange = Command.ReasonForChange,
            IdempotencyKey = Command.IdempotencyKey,
            Outcome = outcome,
            AggregateType = aggregate?.Type,
            AggregateId = aggregate?.Id,
            CorrelationId = execution.Metadata.CorrelationId,
            CausationId = execution.Metadata.CausationId,
            ProposalId = outcome == "Committed" ? _proposal?.Id : null,
            Authorization = decision is null
                ? null
                : JsonSerializer.Serialize(
                    new { permitted = decision.Permitted, reason = decision.Reason, conditions = Conditions(decision) },
                    KernelCommandPipelineJson.Options),
        });

    /// <summary>
    /// Stages lineage nodes and edges for every aggregate the command wrote, verifying that every declared input and
    /// part attaches to a written object version (K-LIN-2).
    /// </summary>
    private void StageAllLineage(IReadOnlyList<ChangedAggregate> changed, ChangedAggregate? primary)
    {
        if ((_parts.Count > 0 || _partEdges.Count > 0)
            && (primary?.Subject is not { Version: not null } || primary.Action == "Deleted"))
        {
            throw new InvalidOperationException(
                $"K-LIN-2: {execution.CommandType} declared lineage inputs or parts but wrote no object version to attach them to.");
        }

        if (_inputs.Exists(i => !changed.Any(w => MatchesAggregate(i.Target, w, ReferenceEquals(w, primary)))))
        {
            throw new InvalidOperationException(
                $"K-LIN-2: {execution.CommandType} declared lineage inputs or parts but wrote no object version to attach them to.");
        }

        foreach (var written in changed)
        {
            StageLineage(written, isPrimary: ReferenceEquals(written, primary));
        }
    }

    private static bool MatchesAggregate(NodeRef? target, ChangedAggregate written, bool isPrimary) =>
        written.Subject is { Version: { } version } subject
        && written.Action != "Deleted"
        && (target is null
            ? isPrimary
            : target.Unversioned() == subject.Unversioned() && (target.Version is null || target.Version == version));

    /// <summary>
    /// Records the written object version as a lineage node and its inbound edges (spec §9.6, K-LIN-2): the inputs the
    /// handler declared for this aggregate, plus <see cref="LineageEdgeKind.Supersedes"/> from the previous version on
    /// an update. A derived object without an inbound edge is a handler defect and aborts the commit (K-LIN-1).
    /// </summary>
    private void StageLineage(ChangedAggregate written, bool isPrimary)
    {
        if (written.Subject is not { Version: { } version } subject || written.Action == "Deleted")
        {
            return;
        }

        var scoped = _inputs.Where(i => MatchesAggregate(i.Target, written, isPrimary)).ToList();
        if (scoped.Exists(i => i.Input.Unversioned() == subject.Unversioned() && i.Input.Version >= version))
        {
            throw new InvalidOperationException(
                $"K-LIN: {execution.CommandType} declared {subject} as an input of itself or of an earlier version.");
        }

        var inbound = scoped.Select(i => (From: i.Input, i.Kind, i.ValidFrom)).ToList();
        if (written.Action == "Updated")
        {
            inbound.Add((subject.AtVersion(version - 1), LineageEdgeKind.Supersedes, null));
        }

        if (_proposal is { } proposal && proposal.Unversioned() != subject.Unversioned())
        {
            inbound.Add((proposal, LineageEdgeKind.ExecutedUnder, null));
        }

        if (written.Derived && inbound.Count == 0)
        {
            throw new InvalidOperationException(
                $"K-LIN-1: {execution.CommandType} wrote derived object {subject} without declaring its inputs.");
        }

        var pinned = subject.AtVersion(version);
        db.LineageNodes.Add(new LineageNodeRecord
        {
            TenantId = Command.TenantId,
            Kind = subject.Kind,
            NodeId = subject.Id,
            Version = version,
            Derived = written.Derived,
            ActivityId = execution.ActivityId,
            RecordedAt = execution.RecordedAt,
        });
        foreach (var (from, kind, validFrom) in inbound.DistinctBy(e => (e.From, e.Kind)))
        {
            AddEdge(from, pinned, kind, validFrom);
        }

        if (isPrimary)
        {
            StageParts(pinned);
        }
    }

    /// <summary>
    /// Writes the parts of the subject version (ADR-2474): a node for each new part version, the edge from the subject to
    /// every part, and the declared edges between parts, each of which must touch a new part.
    /// </summary>
    private void StageParts(NodeRef subject)
    {
        foreach (var (part, newVersion) in _parts)
        {
            if (part.Unversioned() == subject.Unversioned())
            {
                throw new InvalidOperationException($"K-LIN: {execution.CommandType} declared {part} as a part of itself.");
            }

            if (newVersion)
            {
                db.LineageNodes.Add(new LineageNodeRecord
                {
                    TenantId = Command.TenantId,
                    Kind = part.Kind,
                    NodeId = part.Id,
                    Version = part.Version!.Value,
                    Derived = false,
                    ActivityId = execution.ActivityId,
                    RecordedAt = execution.RecordedAt,
                });
            }

            AddEdge(subject, part, LineageEdgeKind.GovernedBy, null);
        }

        foreach (var (from, to, kind) in _partEdges)
        {
            if (!_parts.GetValueOrDefault(from) && !_parts.GetValueOrDefault(to))
            {
                throw new InvalidOperationException(
                    $"K-LIN-2: {execution.CommandType} declared the edge {from} → {to}, but neither is a part it writes.");
            }

            AddEdge(from, to, kind, null);
        }
    }

    private void AddEdge(NodeRef from, NodeRef to, LineageEdgeKind kind, DateTimeOffset? validFrom) =>
        db.LineageEdges.Add(new LineageEdgeRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = Command.TenantId,
            FromKind = from.Kind,
            FromId = from.Id,
            FromVersion = from.Version!.Value,
            ToKind = to.Kind,
            ToId = to.Id,
            ToVersion = to.Version!.Value,
            Kind = kind,
            ActivityId = execution.ActivityId,
            RecordedAt = execution.RecordedAt,
            ValidFrom = validFrom,
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
    /// Appends one link to the tenant's chain, delegating to <see cref="AppendAuditBatchAsync"/>.
    /// </summary>
    private Task AppendAuditAsync(string action, ChangedAggregate? aggregate, CancellationToken cancellationToken, string? changes = null) =>
        AppendAuditBatchAsync([(action, aggregate, changes)], cancellationToken);

    /// <summary>
    /// Locks <c>kernel.audit_chain_heads</c> <c>FOR UPDATE</c> once per commit and appends one sequentially hashed
    /// <see cref="AuditEntryRecord"/> per item, so concurrent commands for one tenant serialise at the chain head
    /// without repeating head locks across multi-aggregate writes.
    /// </summary>
    private async Task AppendAuditBatchAsync(
        IReadOnlyList<(string Action, ChangedAggregate? Aggregate, string? Changes)> entries,
        CancellationToken cancellationToken)
    {
        var tenant = Command.TenantId.Value;
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO kernel.audit_chain_heads (tenant_id, last_sequence, last_hash) VALUES ({tenant}, 0, '') ON CONFLICT (tenant_id) DO NOTHING",
            cancellationToken).ConfigureAwait(false);
        var head = await db.AuditChainHeads
            .FromSql($"SELECT * FROM kernel.audit_chain_heads WHERE tenant_id = {tenant} FOR UPDATE")
            .SingleAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (action, aggregate, changes) in entries)
        {
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
    }

    private void Fault(CommitStage stage) => faultInjector?.Invoke(stage);

    private static KernelEventAttribute EventAttribute(Type eventType) =>
        eventType.GetCustomAttribute<KernelEventAttribute>()
        ?? throw new InvalidOperationException($"{eventType.Name} must declare [KernelEvent(type, schemaVersion)].");

    private static string KeyText(EntityEntry entry) =>
        string.Join("|", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => KeyText(entry.Property(p.Name).CurrentValue!)));

    private static string KeyText(object id) => id is IEntityId entityId ? entityId.Value.ToString("D") : id.ToString()!;

    private static string Serialize(object value) => JsonSerializer.Serialize(value, KernelCommandPipelineJson.Options);

    private sealed record ChangedAggregate(string Type, string Id, long Version, string Action, string Changes, NodeRef? Subject, bool Derived);
}

/// <summary>JSON options shared by kernel persistence for event payloads, envelopes and audit change sets.</summary>
internal static class KernelCommandPipelineJson
{
    // jsonb reorders object members, so a polymorphic value's "type" member may come back after its other members.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };
}
