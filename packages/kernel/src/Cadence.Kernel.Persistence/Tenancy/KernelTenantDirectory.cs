using System;
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
}
