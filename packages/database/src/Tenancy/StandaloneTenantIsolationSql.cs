using System;
using System.Collections.Generic;

using Cadence.Kernel.Sql;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Database.Tenancy;

/// <summary>
/// PostgreSQL Row-Level Security DDL helpers for standalone clinical service schemas (K-ID-1, K-ID-2, ADR-2250).
/// Every tenant-scoped table enables <b>and forces</b> RLS with <c>tenant_id = kernel.current_tenant_id()</c>
/// for both <c>USING</c> and <c>WITH CHECK</c>.
/// </summary>
public static class StandaloneTenantIsolationSql
{
    public const string IsolationPolicy = PostgresTenantRlsSql.IsolationPolicy;
    public const string PendingWorkDirectoryPolicy = PostgresTenantRlsSql.PendingWorkDirectoryPolicy;
    public const string PendingWorkDirectorySetting = PostgresTenantRlsSql.PendingWorkDirectorySetting;
    public const string TenantSetting = PostgresTenantRlsSql.TenantSetting;
    public const string CurrentTenantFunction = PostgresTenantRlsSql.KernelCurrentTenantExpression;
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>
    /// Ensures <c>kernel.current_tenant_id()</c> exists when a standalone service migration executes before
    /// <c>KernelDatabase.MigrateAsync</c> (for example in isolated schema tests), without creating kernel tables.
    /// </summary>
    public const string BootstrapKernelTenantFunctionSql = PostgresTenantRlsSql.BootstrapKernelTenantFunctionSql;

    public static string EnableForTenantColumn(string schema, string table) =>
        PostgresTenantRlsSql.EnableForTenantColumnSql(
            schema,
            table,
            CurrentTenantFunction,
            allowMixedCaseIdentifiers: true,
            dropIfExists: true,
            quotePolicyName: false);

    public static string DisableForTenantColumn(string schema, string table) =>
        PostgresTenantRlsSql.DisablePoliciesSql(
            schema,
            table,
            [IsolationPolicy],
            allowMixedCaseIdentifiers: true,
            quotePolicyNames: false);

    /// <summary>
    /// Creates a narrow <c>SECURITY DEFINER</c> directory function returning only distinct <c>tenant_id</c> UUIDs
    /// with pending work matching <paramref name="predicateSql"/> (modeled on <c>kernel.tenant_ids()</c>, ADR-2548).
    /// Under <c>FORCE ROW LEVEL SECURITY</c>, the accompanying <c>FOR SELECT</c> policy requires both the
    /// function-local <c>cadence.pending_work_directory = 'on'</c> GUC and membership in the table owner role, so
    /// <c>cadence_app</c> (<c>NOSUPERUSER NOBYPASSRLS</c>) cannot bypass RLS by setting the GUC directly.
    /// </summary>
    public static string EnablePendingWorkDirectoryFunctionSql(
        string schema,
        string table,
        string functionName,
        string predicateSql,
        string role = "cadence_app") =>
        PostgresTenantRlsSql.EnablePendingWorkDirectoryFunctionSql(
            schema,
            table,
            functionName,
            predicateSql,
            role,
            allowMixedCaseIdentifiers: true);

    public static string DisablePendingWorkDirectoryFunctionSql(
        string schema,
        string table,
        string functionName,
        bool dropPolicy = true) =>
        PostgresTenantRlsSql.DisablePendingWorkDirectoryFunctionSql(
            schema,
            table,
            functionName,
            dropPolicy,
            allowMixedCaseIdentifiers: true);

    /// <summary>
    /// Grants the non-superuser, <c>NOBYPASSRLS</c> application role (<c>cadence_app</c>) DML privileges on a
    /// standalone module schema (plus <c>EXECUTE</c> on narrow zero-arg <c>*_tenant_ids()</c> directory functions)
    /// while revoking privileges on <c>__EFMigrationsHistory</c>.
    /// </summary>
    public static string GrantAppRoleSql(string schema, string role = "cadence_app") =>
        PostgresTenantRlsSql.GrantConditionalStandaloneSchemaRoleSql(schema, role, MigrationsHistoryTable);

    public static void EnableStandaloneTenantIsolation(this MigrationBuilder migrationBuilder, string schema, IEnumerable<string> tables)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(BootstrapKernelTenantFunctionSql);
        foreach (string table in tables)
        {
            migrationBuilder.Sql(EnableForTenantColumn(schema, table));
        }
    }

    public static void DisableStandaloneTenantIsolation(this MigrationBuilder migrationBuilder, string schema, IEnumerable<string> tables)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        foreach (string table in tables)
        {
            migrationBuilder.Sql(DisableForTenantColumn(schema, table));
        }
    }
}
