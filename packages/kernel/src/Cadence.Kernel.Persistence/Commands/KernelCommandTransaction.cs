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
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence.IdentityVault;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

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
        new KernelCommandTransaction(contextFactory.Create(execution.Command.TenantId), execution, FaultInjector, _moduleStores);
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

        // A session lock on a connection this command keeps open: the handler's reads, its commit and the release all run
        // on it. DisposeAsync releases the lock after the commit or failure, and Npgsql resets pooled connections anyway.
        await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({key})", cancellationToken).ConfigureAwait(false);
        _lockKey = key;
    }

    public void Add(AggregateRootBase aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        var entry = StoreFor(aggregate.GetType()).Add(aggregate);
        Claim(aggregate.GetType(), KeyText(entry));
    }

    public void Raise(object domainEvent, DateTimeOffset? occurredValid = null)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _ = EventAttribute(domainEvent.GetType());
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
            var changed = StampAggregates();
            aggregate = changed.FirstOrDefault();
            Fault(CommitStage.StateStamped);

            AddProvenance("Committed", aggregate, _permit);
            Fault(CommitStage.ProvenanceStaged);

            StageAllLineage(changed, aggregate);

            Fault(CommitStage.LineageStaged);

            foreach (var (domainEvent, occurredValid) in _events)
            {
                var attribute = EventAttribute(domainEvent.GetType());
                AddEvent(attribute.Type, attribute.SchemaVersion, JsonSerializer.SerializeToElement(domainEvent, domainEvent.GetType(), KernelCommandPipelineJson.Options), occurredValid, aggregate?.Subject);
            }

            Fault(CommitStage.EventsStaged);

            AuditEntryRecord? primaryAudit = null;
            if (changed.Count == 0)
            {
                primaryAudit = await AppendAuditAsync("NoChange", null, cancellationToken).ConfigureAwait(false);
            }

            foreach (var written in changed)
            {
                var appended = await AppendAuditAsync(written.Action, written, cancellationToken).ConfigureAwait(false);
                primaryAudit ??= appended;
            }

            await StageBiostatExportAuditAsync(
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

            if (IsStudyLockedProtocolImport(out _))
            {
                foreach (var reopened in db.ChangeTracker.Entries<ProtocolVersion>()
                    .Where(e => e.State == EntityState.Modified
                        && (ProtocolVersionStatus)e.Property(nameof(ProtocolVersion.Status)).OriginalValue! == ProtocolVersionStatus.InReview
                        && e.Entity.Status == ProtocolVersionStatus.Draft))
                {
                    var schema = SqlIdentifier.Quote(db.Model.FindEntityType(typeof(ProtocolVersion))?.GetSchema() ?? "kernel");
                    var reopenSql = string.Concat(
                        "UPDATE ",
                        schema,
                        ".protocol_versions SET status = 'Draft', content_hash = NULL WHERE tenant_id = {0} AND id = {1} AND version_index = {2} AND status = 'InReview'");
                    var originalVersionIndex = (long)reopened.Property(nameof(AggregateRootBase.VersionIndex)).OriginalValue!;
                    var stepped = await db.Database.ExecuteSqlRawAsync(
                        reopenSql,
                        [Command.TenantId.Value, reopened.Entity.Id.Value, originalVersionIndex],
                        cancellationToken).ConfigureAwait(false);
                    if (stepped != 1)
                    {
                        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                        return CommitOutcome.ConcurrencyConflict;
                    }
                }
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
        var deniedAudit = await AppendAuditAsync("Denied", aggregate: null, cancellationToken, changes: payload.GetRawText()).ConfigureAwait(false);
        await StageBiostatExportAuditAsync(
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
        var refusedAudit = await AppendAuditAsync("Refused", aggregate: null, cancellationToken, changes: payload.GetRawText()).ConfigureAwait(false);
        await StageBiostatExportAuditAsync(
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

    /// <summary>The context that maps <paramref name="aggregateType"/>, with the connection held for a module's.</summary>
    private async Task<DbContext> StoreForAsync(Type aggregateType, CancellationToken cancellationToken)
    {
        var store = StoreFor(aggregateType);
        if (!ReferenceEquals(store, db))
        {
            await HoldConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        return store;
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

    private void Claim(Type type, string key)
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
            return;
        }

        if (_scopeLocked
            && (type == typeof(Assertion) || type == typeof(Attestation))
            && _claimed.All(c => c.Type == typeof(Assertion) || c.Type == typeof(Attestation))
            && !_claimed.Any(c => c.Type == type && c.Key != key))
        {
            _claimed.Add((type, key));
            return;
        }

        if (IsStudyLockedProtocolImport(out var importDraft))
        {
            var isTargetVersion = type == typeof(ProtocolVersion)
                && key == KeyText(importDraft.TargetVersionId)
                && !_claimed.Any(c => c.Type == typeof(ProtocolVersion) && c.Key != key);
            var isStagedConcept = type == typeof(Concept)
                && importDraft.StagedConcepts.Any(c => KeyText(c.ConceptId) == key);
            if (!isTargetVersion && !isStagedConcept)
            {
                throw new InvalidOperationException(
                    $"K-TX-1: {execution.CommandType} under study scope lock {_lockedScope} may change only protocol version {importDraft.TargetVersionId} and its staged concepts, not {type.Name} {key}.");
            }

            _claimed.Add((type, key));
            return;
        }

        if (type == typeof(FormVersionSnapshot)
            && _claimed.All(c => c.Type == typeof(ProtocolVersion) || c.Type == typeof(FormVersionSnapshot))
            && _claimed.Count(c => c.Type == typeof(ProtocolVersion)) == 1)
        {
            _claimed.Add((type, key));
            return;
        }

        if (_claimed.Count > 0 && !_claimed.Contains((type, key)))
        {
            var claimed = _claimed.First();
            throw new InvalidOperationException(
                $"K-TX-1: {execution.CommandType} already changes {claimed.Type.Name} {claimed.Key}; it cannot also change {type.Name} {key}. Use an event-driven saga.");
        }

        _claimed.Add((type, key));
    }

    /// <summary>
    /// Stamps GxP audit fields on the changed aggregates and captures their change sets, the primary one first: the one
    /// changed aggregate (K-TX-1), a form instance followed by the observations a form save changed (ADR-2622), or a
    /// scope-locked lock/release assertion paired with its inline electronic signature (ADR-2621).
    /// </summary>
    private List<ChangedAggregate> StampAggregates()
    {
        var changed = db.ChangeTracker.Entries<AggregateRootBase>()
            .Select(e => (Entry: e, Module: (string?)null))
            .Concat(_modules.SelectMany(m => m.Context.ChangeTracker.Entries<AggregateRootBase>().Select(e => (Entry: e, Module: (string?)m.Store.Module))))
            .Where(e => e.Entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (IsStudyLockedProtocolImport(out var importDraft))
        {
            changed = OrderProtocolDraftImport(changed, importDraft);
        }
        else if (changed.Count > 1)
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
                changed = OrderProtocolApprovalSnapshots(changed);
            }
        }

        return [.. changed.Select(c => Stamp(c.Entry, c.Module))];
    }

    private bool IsStudyLockedProtocolImport(out ImportProtocolDraft importDraft)
    {
        if (Command is ImportProtocolDraft cmd
            && _scopeLocked
            && _lockedScope is { Kind: NodeKinds.Study } scope
            && scope.Id == cmd.StudyId.Value)
        {
            importDraft = cmd;
            return true;
        }

        importDraft = null!;
        return false;
    }

    /// <summary>
    /// Orders the aggregates of an atomic protocol draft import under a study scope lock: the target
    /// <see cref="ProtocolVersion"/> first, followed by newly added <see cref="Concept"/> records by id.
    /// </summary>
    private List<(EntityEntry<AggregateRootBase> Entry, string? Module)> OrderProtocolDraftImport(
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

    /// <summary>
    /// The aggregates of an approved protocol version and its bound immutable <see cref="FormVersionSnapshot"/>s (ADR-2809).
    /// </summary>
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

        if (isPrimary && _proposal is { } proposal && proposal.Unversioned() != subject.Unversioned())
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
    /// Appends one link to the tenant's chain. The head row is created if missing and locked <c>FOR UPDATE</c>, so
    /// concurrent commands for one tenant serialise here and the chain never forks.
    /// </summary>
    private async Task<AuditEntryRecord> AppendAuditAsync(string action, ChangedAggregate? aggregate, CancellationToken cancellationToken, string? changes = null)
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
        return link;
    }

    private async Task StageBiostatExportAuditAsync(
        string outcome,
        string auditEntryHash,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        switch (Command)
        {
            case ExportStudySnapshot studyExport:
                {
                    var sha = outcome == "Committed"
                        ? NormalizeSha256Hex(studyExport.Content.ContentHash, auditEntryHash)
                        : auditEntryHash;
                    var details = Serialize(new
                    {
                        command = execution.CommandType,
                        outcome,
                        artifactId = studyExport.ArtifactId.Value,
                        artifactVersionId = studyExport.ArtifactVersionId.Value,
                        format = studyExport.Format,
                        lockFilter = studyExport.LockFilter,
                        validAt = studyExport.ValidAt,
                        knownAt = studyExport.KnownAt,
                        isUnblinded = studyExport.IncludesUnblindedData,
                        contentHash = studyExport.Content.ContentHash,
                        auditEntryHash,
                        reasonForChange = studyExport.ReasonForChange,
                        failureReason,
                    });
                    db.BiostatExportAuditLogs.Add(new BiostatExportAuditLog
                    {
                        Id = Guid.CreateVersion7(execution.RecordedAt),
                        TenantId = studyExport.TenantId,
                        StudyId = studyExport.StudyId,
                        EnrollmentId = null,
                        ArtifactId = studyExport.ArtifactId,
                        ActivityId = execution.ActivityId,
                        Timestamp = execution.RecordedAt,
                        UserId = execution.Actor.Id,
                        Action = "EXPORT_STUDY_SNAPSHOT",
                        Outcome = outcome,
                        Format = studyExport.Format,
                        LockFilter = studyExport.LockFilter,
                        ValidAt = studyExport.ValidAt,
                        KnownAt = studyExport.KnownAt,
                        IsUnblinded = studyExport.IncludesUnblindedData,
                        DetailsJson = details,
                        Sha256Hash = sha,
                        CreatedAt = execution.RecordedAt,
                    });
                    break;
                }

            case ExportEnrollmentCasebook casebookExport:
                {
                    var resolvedStudyId = casebookExport.StudyId;
                    if (resolvedStudyId.IsEmpty)
                    {
                        var foundStudyId = await db.Enrollments.AsNoTracking()
                            .Where(e => e.Id == casebookExport.EnrollmentId)
                            .Select(e => (StudyId?)e.StudyId)
                            .FirstOrDefaultAsync(cancellationToken)
                            .ConfigureAwait(false);
                        if (foundStudyId is { } sid)
                        {
                            resolvedStudyId = sid;
                        }
                    }

                    var sha = outcome == "Committed"
                        ? NormalizeSha256Hex(casebookExport.Content.ContentHash, auditEntryHash)
                        : auditEntryHash;
                    var details = Serialize(new
                    {
                        command = execution.CommandType,
                        outcome,
                        enrollmentId = casebookExport.EnrollmentId.Value,
                        artifactId = casebookExport.ArtifactId.Value,
                        artifactVersionId = casebookExport.ArtifactVersionId.Value,
                        format = casebookExport.Format,
                        validAt = casebookExport.ValidAt,
                        knownAt = casebookExport.KnownAt,
                        isUnblinded = casebookExport.IncludesUnblindedData,
                        contentHash = casebookExport.Content.ContentHash,
                        auditEntryHash,
                        reasonForChange = casebookExport.ReasonForChange,
                        failureReason,
                    });
                    db.BiostatExportAuditLogs.Add(new BiostatExportAuditLog
                    {
                        Id = Guid.CreateVersion7(execution.RecordedAt),
                        TenantId = casebookExport.TenantId,
                        StudyId = resolvedStudyId,
                        EnrollmentId = casebookExport.EnrollmentId,
                        ArtifactId = casebookExport.ArtifactId,
                        ActivityId = execution.ActivityId,
                        Timestamp = execution.RecordedAt,
                        UserId = execution.Actor.Id,
                        Action = "EXPORT_ENROLLMENT_CASEBOOK",
                        Outcome = outcome,
                        Format = casebookExport.Format,
                        LockFilter = null,
                        ValidAt = casebookExport.ValidAt,
                        KnownAt = casebookExport.KnownAt,
                        IsUnblinded = casebookExport.IncludesUnblindedData,
                        DetailsJson = details,
                        Sha256Hash = sha,
                        CreatedAt = execution.RecordedAt,
                    });
                    break;
                }
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

    private static string NormalizeSha256Hex(string? candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return fallback;
        }

        var trimmed = candidate.StartsWith(ContentHash.Prefix, StringComparison.OrdinalIgnoreCase)
            ? candidate[ContentHash.Prefix.Length..]
            : candidate;
        return trimmed.Length <= 64 ? trimmed : trimmed[..64];
    }

    private sealed record ChangedAggregate(string Type, string Id, long Version, string Action, string Changes, NodeRef? Subject, bool Derived);
}

/// <summary>JSON options shared by kernel persistence for event payloads, envelopes and audit change sets.</summary>
internal static class KernelCommandPipelineJson
{
    // jsonb reorders object members, so a polymorphic value's "type" member may come back after its other members.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };
}
