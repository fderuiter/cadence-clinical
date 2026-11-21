using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Database.Tenancy;

/// <summary>
/// Binds <c>cadence.tenant_id</c> via <c>pg_catalog.set_config('cadence.tenant_id', @tenant, false)</c> when a
/// standalone PostgreSQL <see cref="DbConnection"/> opens, and clears it before the connection returns to the pool
/// (K-ID-2, ADR-2250).
/// </summary>
public sealed class StandaloneTenantConnectionInterceptor : DbConnectionInterceptor
{
    public static readonly StandaloneTenantConnectionInterceptor Instance = new();

    private const string SetTenantSql = $"SELECT pg_catalog.set_config('{StandaloneTenantIsolationSql.TenantSetting}', @tenant, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        string tenant = ResolveTenantSetting(eventData);
        using var command = CreateSetTenantCommand(connection, tenant);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        string tenant = await ResolveTenantSettingAsync(eventData, cancellationToken).ConfigureAwait(false);
        await using var command = CreateSetTenantCommand(connection, tenant);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State == ConnectionState.Open)
        {
            using var command = CreateSetTenantCommand(connection, string.Empty);
            command.ExecuteNonQuery();
        }

        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State == ConnectionState.Open)
        {
            await using var command = CreateSetTenantCommand(connection, string.Empty);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return result;
    }

    private static string ResolveTenantSetting(ConnectionEventData eventData)
    {
        var ext = eventData.Context?.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        if (ext?.TenantAccessor is null || ext.TenantAccessor.IsSystemSuppressed)
        {
            return string.Empty;
        }

        Guid? tenantId = ext.TenantAccessor.GetCurrentTenantId();
        return tenantId?.ToString("D") ?? string.Empty;
    }

    private static async ValueTask<string> ResolveTenantSettingAsync(ConnectionEventData eventData, CancellationToken cancellationToken)
    {
        var ext = eventData.Context?.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        if (ext?.TenantAccessor is null || ext.TenantAccessor.IsSystemSuppressed)
        {
            return string.Empty;
        }

        Guid? tenantId = await ext.TenantAccessor.GetCurrentTenantIdAsync(cancellationToken).ConfigureAwait(false);
        return tenantId?.ToString("D") ?? string.Empty;
    }

    private static DbCommand CreateSetTenantCommand(DbConnection connection, string tenant)
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
