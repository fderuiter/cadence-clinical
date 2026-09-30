using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cadence.Kernel.Persistence.Tenancy;

/// <summary>
/// The one place the database tenant is set (K-ID-2, ADR-2250 rule 2). Every connection a
/// <see cref="KernelDbContext"/> opens gets <see cref="TenantIsolationSql.TenantSetting"/> set to the context's
/// tenant before any statement runs, and cleared again before the connection goes back to the pool. A context with
/// no tenant cannot open a connection at all, so a request without a tenant fails instead of reading nothing.
/// </summary>
/// <remarks>
/// The setting lasts for the connection lease rather than one transaction, because a command reads its aggregate
/// before it opens its write transaction and EF Core runs those reads as separate statements. Each lease belongs to
/// one context and so to one tenant. Npgsql also discards session state when a connection returns to its pool.
/// @req:PRD-SYS-053
/// </remarks>
internal sealed class KernelTenantConnectionInterceptor : DbConnectionInterceptor
{
    private const string SetTenantSql = $"SELECT pg_catalog.set_config('{TenantIsolationSql.TenantSetting}', @tenant, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = SetTenantCommand(connection, TenantOf(eventData));
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = SetTenantCommand(connection, TenantOf(eventData));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State == ConnectionState.Open)
        {
            using var command = SetTenantCommand(connection, string.Empty);
            command.ExecuteNonQuery();
        }

        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State == ConnectionState.Open)
        {
            await using var command = SetTenantCommand(connection, string.Empty);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>The tenant id to set, or an empty value for a tenant-directory context.</summary>
    private static string TenantOf(ConnectionEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        return eventData.Context switch
        {
            KernelDbContext { Tenant: { } tenant } => tenant.Value.ToString(),
            KernelDbContext { IsTenantDirectory: true } => string.Empty,
            _ => throw new InvalidOperationException(
                "K-ID-2: a kernel connection was opened without a tenant. Create kernel contexts through KernelTenantDbContextFactory."),
        };
    }

    private static DbCommand SetTenantCommand(DbConnection connection, string tenant)
    {
        var command = connection.CreateCommand();
        command.CommandText = SetTenantSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenant;
        command.Parameters.Add(parameter);
        return command;
    }
}
