using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Tenancy;

/// <summary>
/// Resolves a tenant code through the database's <see cref="TenantIsolationSql.CodeDirectoryFunction"/>, which returns
/// the id of an active tenant and nothing else. The connection itself sees no tenant's rows.
/// </summary>
internal sealed class KernelTenantDirectory(IDbContextFactory<KernelDbContext> contextFactory) : ITenantDirectory
{
    /// <summary>The longest tenant code the kernel stores.</summary>
    public const int MaxCodeLength = 64;

    public async Task<TenantId?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength)
        {
            return null;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.BindTenantDirectory();
        var ids = await db.Database
            .SqlQueryRaw<Guid?>(
                $"SELECT {KernelDbContext.Schema}.{TenantIsolationSql.CodeDirectoryFunction}({{0}}) AS \"Value\"", code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.SingleOrDefault() is { } id ? new TenantId(id) : null;
    }

    public async Task<IReadOnlyList<TenantId>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        List<Guid> candidateIds;
        await using (var directoryDb = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            directoryDb.BindTenantDirectory();
            candidateIds = await directoryDb.Database
                .SqlQueryRaw<Guid>($"SELECT t AS \"Value\" FROM {KernelDbContext.Schema}.{TenantIsolationSql.DirectoryFunction}() AS t")
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var active = new List<TenantId>(candidateIds.Count);
        foreach (var rawId in candidateIds)
        {
            var tenantId = new TenantId(rawId);
            await using var tenantDb = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            tenantDb.BindTenant(tenantId);
            if (await tenantDb.Tenants
                .AsNoTracking()
                .AnyAsync(t => t.Id == tenantId && t.IsActive, cancellationToken)
                .ConfigureAwait(false))
            {
                active.Add(tenantId);
            }
        }

        return active;
    }

    public async Task<string?> FindCodeAsync(TenantId tenant, CancellationToken cancellationToken = default)
    {
        await using var tenantDb = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        tenantDb.BindTenant(tenant);
        return await tenantDb.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenant)
            .Select(t => t.Code)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
