using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Sql;

/// <summary>
/// Pure BCL <see cref="DbCommand"/> helper binding and clearing the PostgreSQL <c>cadence.tenant_id</c> session
/// configuration parameter (<c>K-ID-2</c>, ADR-2250, ADR-2548).
/// </summary>
public static class PostgresTenantSession
{
    /// <summary>Parameterized SQL statement binding <see cref="PostgresTenantRlsSql.TenantSetting"/> for the session.</summary>
    public const string SetTenantSql = $"SELECT pg_catalog.set_config('{PostgresTenantRlsSql.TenantSetting}', @tenant, false)";

    /// <summary>Parameter name carrying the tenant UUID string (or empty string when clearing).</summary>
    public const string TenantParameterName = "tenant";

    /// <summary>Creates a parameterized <see cref="DbCommand"/> executing <see cref="SetTenantSql"/>.</summary>
    public static DbCommand CreateSetTenantCommand(
        DbConnection connection,
        string tenantValue,
        DbTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tenantValue);

        var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = SetTenantSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = TenantParameterName;
        parameter.Value = tenantValue;
        command.Parameters.Add(parameter);
        return command;
    }

    /// <summary>Synchronously sets <c>cadence.tenant_id</c> on <paramref name="connection"/>.</summary>
    public static void ExecuteSetTenant(
        DbConnection connection,
        string tenantValue,
        DbTransaction? transaction = null)
    {
        using var command = CreateSetTenantCommand(connection, tenantValue, transaction);
        command.ExecuteNonQuery();
    }

    /// <summary>Asynchronously sets <c>cadence.tenant_id</c> on <paramref name="connection"/>.</summary>
    public static async ValueTask ExecuteSetTenantAsync(
        DbConnection connection,
        string tenantValue,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateSetTenantCommand(connection, tenantValue, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Clears <c>cadence.tenant_id</c> if <paramref name="connection"/> is currently open.</summary>
    public static void ResetTenantIfOpen(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State == ConnectionState.Open)
        {
            ExecuteSetTenant(connection, string.Empty);
        }
    }

    /// <summary>Asynchronously clears <c>cadence.tenant_id</c> if <paramref name="connection"/> is currently open.</summary>
    public static async ValueTask ResetTenantIfOpenAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State == ConnectionState.Open)
        {
            await ExecuteSetTenantAsync(connection, string.Empty, transaction: null, cancellationToken).ConfigureAwait(false);
        }
    }
}
