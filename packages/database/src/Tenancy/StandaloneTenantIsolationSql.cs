using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Database.Tenancy;

/// <summary>
/// PostgreSQL Row-Level Security DDL helpers for standalone clinical service schemas (K-ID-1, K-ID-2, ADR-2250).
/// Every tenant-scoped table enables <b>and forces</b> RLS with <c>tenant_id = kernel.current_tenant_id()</c>
/// for both <c>USING</c> and <c>WITH CHECK</c>.
/// </summary>
public static partial class StandaloneTenantIsolationSql
{
    public const string IsolationPolicy = "tenant_isolation";
    public const string TenantSetting = "cadence.tenant_id";
    public const string CurrentTenantFunction = "kernel.current_tenant_id()";
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>
    /// Ensures <c>kernel.current_tenant_id()</c> exists when a standalone service migration executes before
    /// <c>KernelDatabase.MigrateAsync</c> (for example in isolated schema tests), without creating kernel tables.
    /// </summary>
    public const string BootstrapKernelTenantFunctionSql = """
        DO $$
        BEGIN
            IF to_regprocedure('kernel.current_tenant_id()') IS NULL THEN
                CREATE SCHEMA IF NOT EXISTS "kernel";
                CREATE OR REPLACE FUNCTION "kernel".current_tenant_id() RETURNS uuid
                LANGUAGE sql STABLE PARALLEL SAFE AS $fn$
                    SELECT NULLIF(pg_catalog.current_setting('cadence.tenant_id', true), '')::uuid
                $fn$;
            END IF;
        END $$;
        """;

    public static string EnableForTenantColumn(string schema, string table)
    {
        string qualified = $"{Quote(schema)}.{Quote(table)}";
        return $"""
            ALTER TABLE {qualified} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {qualified} FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS {IsolationPolicy} ON {qualified};
            CREATE POLICY {IsolationPolicy} ON {qualified}
                USING (tenant_id = {CurrentTenantFunction})
                WITH CHECK (tenant_id = {CurrentTenantFunction});
            """;
    }

    public static string DisableForTenantColumn(string schema, string table)
    {
        string qualified = $"{Quote(schema)}.{Quote(table)}";
        return $"""
            DROP POLICY IF EXISTS {IsolationPolicy} ON {qualified};
            ALTER TABLE {qualified} NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE {qualified} DISABLE ROW LEVEL SECURITY;
            """;
    }

    /// <summary>
    /// Grants the non-superuser, <c>NOBYPASSRLS</c> application role (<c>cadence_app</c>) DML privileges on a
    /// standalone module schema while revoking privileges on <c>__EFMigrationsHistory</c>.
    /// </summary>
    public static string GrantAppRoleSql(string schema, string role = "cadence_app")
    {
        string s = Quote(schema);
        string r = Quote(role);
        return $"""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                    GRANT USAGE ON SCHEMA {s} TO {r};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {s} TO {r};
                    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {s} TO {r};
                    IF to_regclass('{schema}."{MigrationsHistoryTable}"') IS NOT NULL THEN
                        REVOKE ALL ON {s}."{MigrationsHistoryTable}" FROM {r};
                    END IF;
                END IF;
            END $$;
            """;
    }

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

    private static string Quote(string identifier) =>
        identifier is not null && SafeIdentifier().IsMatch(identifier)
            ? $"\"{identifier}\""
            : throw new ArgumentException($"'{identifier}' is not a valid PostgreSQL identifier.", nameof(identifier));

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifier();
}
