using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Persistence.Commands;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Tenancy;

/// <summary>
/// Creates kernel contexts already bound to a tenant (K-ID-2). Kernel code creates contexts only through this
/// factory: the command pipeline for the command's tenant, and system jobs for each tenant in turn.
/// </summary>
internal sealed class KernelTenantDbContextFactory(IDbContextFactory<KernelDbContext> contextFactory)
{
    /// <summary>A context whose connections see and write only <paramref name="tenant"/>'s rows.</summary>
    public KernelDbContext Create(TenantId tenant)
    {
        var db = contextFactory.CreateDbContext();
        db.BindTenant(tenant);
        return db;
    }

    /// <summary>
    /// Acquires a tenant-scoped read lease for untracked (<c>AsNoTracking</c>) queries. Reuses the active command
    /// transaction's already-bound <see cref="KernelDbContext"/> connection when running sequentially in the same
    /// tenant before commit; otherwise opens a dedicated tenant-bound connection lease (K-ID-2).
    /// </summary>
    public async ValueTask<KernelTenantReadLease> AcquireReadLeaseAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        if (KernelCommandTransaction.Current is { } active
            && await active.TryAcquireSharedReadLeaseAsync(tenant, cancellationToken).ConfigureAwait(false) is { } shared)
        {
            return shared;
        }

        var db = Create(tenant);
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            return new KernelTenantReadLease(db, onRelease: null, ownsContext: true);
        }
        catch
        {
            await db.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Every tenant id, for jobs that visit tenants one at a time (ADR-2250 rule 3). Reads through the database's
    /// tenant directory function; the connection itself sees no tenant's rows.
    /// </summary>
    public async Task<IReadOnlyList<TenantId>> ListTenantsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.BindTenantDirectory();
        var ids = await db.Database
            .SqlQueryRaw<Guid>($"SELECT t AS \"Value\" FROM {KernelDbContext.Schema}.{TenantIsolationSql.DirectoryFunction}() AS t")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. ids.Select(id => new TenantId(id))];
    }
}

/// <summary>
/// Holds a tenant-bound <see cref="KernelDbContext"/> connection lease across untracked read queries, closing and
/// disposing the context only when owned by this lease.
/// </summary>
internal readonly struct KernelTenantReadLease(KernelDbContext db, Action? onRelease, bool ownsContext) : IAsyncDisposable
{
    public KernelDbContext Db => db;

    public async ValueTask DisposeAsync()
    {
        onRelease?.Invoke();
        if (ownsContext)
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }
}
