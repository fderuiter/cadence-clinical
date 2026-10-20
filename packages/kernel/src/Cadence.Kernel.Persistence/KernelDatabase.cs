using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.IdentityVault;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// Deployment operations on the kernel schema: applying migrations as the owning role, and granting the application
/// role exactly what the kernel needs (ADR-2250 rule 1). Hosts call these from their migration step, never from a
/// request path.
/// </summary>
public static class KernelDatabase
{
    /// <summary>
    /// Applies the kernel migrations. Run as the role that owns the <c>kernel</c> schema (<c>cadence_owner</c> in
    /// ADR-2250), not as the application role. The context used here is not tenant-scoped.
    /// </summary>
    public static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = new DbContextOptionsBuilder<KernelDbContext>();
        UseKernelNpgsql(options, connectionString);
        await using var db = new KernelDbContext(options.Options);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SQL that grants <paramref name="role"/> what the kernel needs and nothing more: read, insert and update on the
    /// kernel tables, insert without update on the insert-only ones, no delete anywhere, nothing on the migration
    /// history, the tenant directory functions, and the IdentityVault functions without any privilege on the vault's
    /// tables (ADR-2458). Re-run it after every migration that adds a table. The role must not own
    /// the schema and must not have <c>BYPASSRLS</c>; the schema test checks both.
    /// </summary>
    /// <param name="role">An existing lower-case role name, for example <c>cadence_app</c>.</param>
    public static string GrantAppRoleSql(string role)
    {
        var r = SqlIdentifier.Quote(role);
        var s = SqlIdentifier.Quote(KernelDbContext.Schema);
        var insertOnly = string.Join(", ", InsertOnlySql.Tables.Select(t => $"{s}.{SqlIdentifier.Quote(t)}"));
        return $"""
            GRANT USAGE ON SCHEMA {s} TO {r};
            GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA {s} TO {r};
            REVOKE UPDATE ON {insertOnly} FROM {r};
            REVOKE ALL ON {s}."{MigrationsHistoryTable}" FROM {r};
            GRANT EXECUTE ON FUNCTION {s}.{TenantIsolationSql.DirectoryFunction}() TO {r};
            GRANT EXECUTE ON FUNCTION {s}.{TenantIsolationSql.CodeDirectoryFunction}(text) TO {r};
            {IdentityVaultSql.GrantSql(r)}
            """;
    }

    internal const string MigrationsHistoryTable = "__ef_migrations_history";

    internal static void UseKernelNpgsql(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, KernelDbContext.Schema));
}
