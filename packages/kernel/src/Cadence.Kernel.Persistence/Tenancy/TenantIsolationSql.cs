using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Kernel.Persistence.Tenancy;

/// <summary>
/// DDL for database-enforced tenant isolation (K-ID-2, ADR-2250). Row-Level Security is enabled <b>and forced</b> on
/// every kernel table, so the policies bind the table owner too; only a superuser or a <c>BYPASSRLS</c> role escapes
/// them, and the application role is neither. Each policy compares a row's tenant with
/// <see cref="CurrentTenantFunction"/>, which reads <see cref="TenantSetting"/>: the one value
/// <see cref="KernelTenantConnectionInterceptor"/> sets. With no tenant set it is <c>NULL</c>, so the connection sees
/// no rows and can insert none.
/// @req:PRD-SYS-053
/// </summary>
internal static class TenantIsolationSql
{
    /// <summary>Connection setting that carries the current tenant id.</summary>
    public const string TenantSetting = "cadence.tenant_id";

    /// <summary>Setting that <see cref="DirectoryFunction"/> turns on for its own execution only.</summary>
    public const string DirectorySetting = "cadence.tenant_directory";

    /// <summary>Returns the current tenant id, or <c>NULL</c> when none is set.</summary>
    public const string CurrentTenantFunction = "current_tenant_id";

    /// <summary>Lists every tenant id, for jobs that visit tenants one at a time (ADR-2250 rule 3).</summary>
    public const string DirectoryFunction = "tenant_ids";

    /// <summary>Name of the isolation policy on every tenant-scoped table.</summary>
    public const string IsolationPolicy = "tenant_isolation";

    /// <summary>Name of the read-only policy on <c>tenants</c> that only <see cref="DirectoryFunction"/> satisfies.</summary>
    public const string DirectoryPolicy = "tenant_directory";

    /// <summary>Name of the policy on <c>people</c> (K-ID-4).</summary>
    public const string PersonVisibilityPolicy = "person_visibility";

    /// <summary>
    /// Kernel tables that are not tenant-scoped by a <c>tenant_id</c> column. <c>tenants</c> is keyed by the tenant
    /// itself and <c>people</c> is tenant-independent (K-ID-4); both still have forced RLS with their own policies.
    /// </summary>
    public static readonly IReadOnlySet<string> TablesWithoutTenantColumn = new HashSet<string>(["tenants", "people"]);

    public static string CreateFunctions(string schema)
    {
        var s = SqlIdentifier.Quote(schema);
        return $$"""
            CREATE OR REPLACE FUNCTION {{s}}.{{CurrentTenantFunction}}() RETURNS uuid
            LANGUAGE sql STABLE PARALLEL SAFE AS $fn$
                SELECT NULLIF(pg_catalog.current_setting('{{TenantSetting}}', true), '')::uuid
            $fn$;
            """;
    }

    public static string DropFunctions(string schema) =>
        $"DROP FUNCTION IF EXISTS {SqlIdentifier.Quote(schema)}.{CurrentTenantFunction}();";

    /// <summary>Enables and forces RLS on a table whose rows carry <c>tenant_id</c>, with one policy for all commands.</summary>
    public static string EnableForTenantColumn(string schema, string table) =>
        Enable(schema, table, IsolationPolicy, $"tenant_id = {SqlIdentifier.Quote(schema)}.{CurrentTenantFunction}()", withCheck: null);

    /// <summary>
    /// Enables and forces RLS on <c>tenants</c>: a tenant sees and creates only its own row. The directory policy
    /// adds read access to the ids, for the table owner, while <see cref="DirectoryFunction"/> runs; the function is
    /// the only way to list tenants and returns ids alone.
    /// </summary>
    public static string EnableForTenants(string schema)
    {
        var s = SqlIdentifier.Quote(schema);
        return Enable(schema, "tenants", IsolationPolicy, $"id = {s}.{CurrentTenantFunction}()", withCheck: null) + $$"""

            CREATE POLICY {{DirectoryPolicy}} ON {{s}}."tenants" FOR SELECT
                USING (pg_catalog.current_setting('{{DirectorySetting}}', true) = 'on'
                   AND pg_catalog.pg_has_role(current_user, (SELECT c.relowner FROM pg_catalog.pg_class c WHERE c.oid = '{{schema}}.tenants'::regclass), 'MEMBER'));
            CREATE OR REPLACE FUNCTION {{s}}.{{DirectoryFunction}}() RETURNS SETOF uuid
            LANGUAGE sql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
            SET {{DirectorySetting}} = 'on'
            AS $fn$
                SELECT t.id FROM {{s}}."tenants" t ORDER BY t.id
            $fn$;
            REVOKE ALL ON FUNCTION {{s}}.{{DirectoryFunction}}() FROM PUBLIC;
            """;
    }

    /// <summary>
    /// Enables and forces RLS on <c>people</c> (K-ID-4): a person is visible to a tenant only through that tenant's
    /// <c>tenant_memberships</c> row (itself filtered to the current tenant); any tenant may register a person.
    /// </summary>
    public static string EnableForPeople(string schema)
    {
        var s = SqlIdentifier.Quote(schema);
        return Enable(
            schema,
            "people",
            PersonVisibilityPolicy,
            $"EXISTS (SELECT 1 FROM {s}.\"tenant_memberships\" m WHERE m.person_id = \"people\".id)",
            withCheck: $"{s}.{CurrentTenantFunction}() IS NOT NULL");
    }

    public static string Disable(string schema, string table, params string[] policies)
    {
        var qualified = $"{SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(table)}";
        var drops = string.Concat(policies.Select(p => $"DROP POLICY IF EXISTS {SqlIdentifier.Quote(p)} ON {qualified};\n"));
        return drops + $"""
            ALTER TABLE {qualified} NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE {qualified} DISABLE ROW LEVEL SECURITY;
            """;
    }

    public static string DropDirectory(string schema) =>
        $"DROP FUNCTION IF EXISTS {SqlIdentifier.Quote(schema)}.{DirectoryFunction}();\n"
        + $"DROP POLICY IF EXISTS {DirectoryPolicy} ON {SqlIdentifier.Quote(schema)}.\"tenants\";";

    private static string Enable(string schema, string table, string policy, string predicate, string? withCheck)
    {
        var qualified = $"{SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(table)}";
        return $"""
            ALTER TABLE {qualified} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {qualified} FORCE ROW LEVEL SECURITY;
            CREATE POLICY {SqlIdentifier.Quote(policy)} ON {qualified}
                USING ({predicate})
                WITH CHECK ({withCheck ?? predicate});
            """;
    }
}

/// <summary>Migration helpers for <see cref="TenantIsolationSql"/>. Migrations name their tables literally.</summary>
internal static class TenantIsolationMigrationBuilderExtensions
{
    public static void CreateTenantIsolationFunctions(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(TenantIsolationSql.CreateFunctions(schema));

    public static void DropTenantIsolationFunctions(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(TenantIsolationSql.DropFunctions(schema));

    public static void EnableTenantIsolation(this MigrationBuilder migrationBuilder, string schema, params string[] tables)
    {
        foreach (var table in tables)
        {
            migrationBuilder.Sql(TenantIsolationSql.EnableForTenantColumn(schema, table));
        }
    }

    public static void DisableTenantIsolation(this MigrationBuilder migrationBuilder, string schema, params string[] tables)
    {
        foreach (var table in tables)
        {
            migrationBuilder.Sql(TenantIsolationSql.Disable(schema, table, TenantIsolationSql.IsolationPolicy));
        }
    }
}
