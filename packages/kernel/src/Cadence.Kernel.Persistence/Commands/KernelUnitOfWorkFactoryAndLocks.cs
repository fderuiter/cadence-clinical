using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

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

internal static class KernelCommandLockAndStoreHelper
{
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

    internal static DbContext ResolveStore(
        KernelDbContext db,
        List<(IModuleCommandStore Store, ModuleDbContext Context)> modules,
        IReadOnlyList<IModuleCommandStore>? moduleStores,
        Type aggregateType)
    {
        if (db.Model.FindEntityType(aggregateType) is not null)
        {
            return db;
        }

        foreach (var (_, context) in modules)
        {
            if (context.Model.FindEntityType(aggregateType) is not null)
            {
                return context;
            }
        }

        foreach (var store in moduleStores ?? [])
        {
            if (modules.Exists(m => ReferenceEquals(m.Store, store)))
            {
                continue;
            }

            var context = store.Create(db.Database.GetDbConnection());
            modules.Add((store, context));
            if (context.Model.FindEntityType(aggregateType) is not null)
            {
                return context;
            }
        }

        throw new InvalidOperationException(
            $"{aggregateType.Name} is mapped neither by the kernel nor by a registered module command store (ADR-2600).");
    }

    internal static async Task<bool> ReopenInReviewProtocolVersionsAsync(
        KernelDbContext db,
        TenantId tenant,
        CancellationToken cancellationToken)
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
                [tenant.Value, reopened.Entity.Id.Value, originalVersionIndex],
                cancellationToken).ConfigureAwait(false);
            if (stepped != 1)
            {
                return false;
            }
        }

        return true;
    }
}
