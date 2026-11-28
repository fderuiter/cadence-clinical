using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Cadence.Kernel.Sql;

/// <summary>
/// Pure BCL PostgreSQL Row-Level Security (RLS) policy, directory function, and application-role grant DDL builders
/// (<c>K-ID-2</c>, ADR-2250, ADR-2548, ADR-2600) shared across kernel, module, and standalone persistence layers.
/// </summary>
public static class PostgresTenantRlsSql
{
    /// <summary>PostgreSQL session setting storing the active tenant UUID.</summary>
    public const string TenantSetting = "cadence.tenant_id";

    /// <summary>Standard RLS policy name on tenant-scoped tables.</summary>
    public const string IsolationPolicy = "tenant_isolation";

    /// <summary>Unqualified function name returning the current tenant UUID.</summary>
    public const string CurrentTenantFunctionName = "current_tenant_id";

    /// <summary>Qualified kernel function expression returning the current tenant UUID.</summary>
    public const string KernelCurrentTenantExpression = "kernel.current_tenant_id()";

    /// <summary>Policy name for narrow <c>SECURITY DEFINER</c> pending-work tenant directory functions.</summary>
    public const string PendingWorkDirectoryPolicy = "pending_work_directory";

    /// <summary>Function-local GUC enabling pending-work tenant directory reads.</summary>
    public const string PendingWorkDirectorySetting = "cadence.pending_work_directory";

    /// <summary>
    /// Idempotent bootstrap block ensuring <c>kernel.current_tenant_id()</c> exists when a standalone migration runs
    /// before <c>KernelDatabase.MigrateAsync</c>.
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

    /// <summary>Creates <c>&lt;schema&gt;.current_tenant_id()</c> reading <see cref="TenantSetting"/>.</summary>
    public static string CreateCurrentTenantFunctionSql(string schemaName, bool allowMixedCaseIdentifiers = false)
    {
        string quotedSchema = PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema");
        return $$"""
            CREATE OR REPLACE FUNCTION {{quotedSchema}}.{{CurrentTenantFunctionName}}() RETURNS uuid
            LANGUAGE sql STABLE PARALLEL SAFE AS $fn$
                SELECT NULLIF(pg_catalog.current_setting('{{TenantSetting}}', true), '')::uuid
            $fn$;
            """;
    }

    /// <summary>Drops <c>&lt;schema&gt;.current_tenant_id()</c>.</summary>
    public static string DropCurrentTenantFunctionSql(string schemaName, bool allowMixedCaseIdentifiers = false) =>
        $"DROP FUNCTION IF EXISTS {PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema")}.{CurrentTenantFunctionName}();";

    /// <summary>Enables and forces RLS on a table with a custom <c>USING</c> and <c>WITH CHECK</c> policy.</summary>
    public static string EnablePolicySql(
        string schemaName,
        string tableName,
        string policyName,
        string predicate,
        string? withCheck = null,
        bool allowMixedCaseIdentifiers = false,
        bool dropIfExists = false,
        bool quotePolicyName = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(predicate);
        string qualifiedTable =
            $"{PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema")}.{PostgresIdentifier.Quote(tableName, allowMixedCaseIdentifiers, paramName: "table")}";
        PostgresIdentifier.Validate(policyName, allowMixedCaseIdentifiers, paramName: "policy");
        string formattedPolicy = quotePolicyName ? $"\"{policyName}\"" : policyName;
        string effectiveCheck = withCheck ?? predicate;

        if (dropIfExists)
        {
            return $"""
                ALTER TABLE {qualifiedTable} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {qualifiedTable} FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS {formattedPolicy} ON {qualifiedTable};
                CREATE POLICY {formattedPolicy} ON {qualifiedTable}
                    USING ({predicate})
                    WITH CHECK ({effectiveCheck});
                """;
        }

        return $"""
            ALTER TABLE {qualifiedTable} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {qualifiedTable} FORCE ROW LEVEL SECURITY;
            CREATE POLICY {formattedPolicy} ON {qualifiedTable}
                USING ({predicate})
                WITH CHECK ({effectiveCheck});
            """;
    }

    /// <summary>Enables and forces RLS on a table keyed by <c>tenant_id</c>.</summary>
    public static string EnableForTenantColumnSql(
        string schemaName,
        string tableName,
        string currentTenantExpression = KernelCurrentTenantExpression,
        bool allowMixedCaseIdentifiers = false,
        bool dropIfExists = false,
        bool quotePolicyName = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentTenantExpression);
        string predicate = $"tenant_id = {currentTenantExpression}";
        return EnablePolicySql(
            schemaName,
            tableName,
            IsolationPolicy,
            predicate,
            withCheck: predicate,
            allowMixedCaseIdentifiers: allowMixedCaseIdentifiers,
            dropIfExists: dropIfExists,
            quotePolicyName: quotePolicyName);
    }

    /// <summary>Drops the named policies and disables forced RLS on a table.</summary>
    public static string DisablePoliciesSql(
        string schemaName,
        string tableName,
        IReadOnlyList<string> policyNames,
        bool allowMixedCaseIdentifiers = false,
        bool quotePolicyNames = false)
    {
        ArgumentNullException.ThrowIfNull(policyNames);
        string qualifiedTable =
            $"{PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema")}.{PostgresIdentifier.Quote(tableName, allowMixedCaseIdentifiers, paramName: "table")}";
        var drops = new StringBuilder();
        foreach (string policyName in policyNames)
        {
            PostgresIdentifier.Validate(policyName, allowMixedCaseIdentifiers, paramName: "policies");
            string formattedPolicy = quotePolicyNames ? $"\"{policyName}\"" : policyName;
            drops.Append($"DROP POLICY IF EXISTS {formattedPolicy} ON {qualifiedTable};\n");
        }

        return drops.ToString() + $"""
            ALTER TABLE {qualifiedTable} NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE {qualifiedTable} DISABLE ROW LEVEL SECURITY;
            """;
    }

    /// <summary>
    /// Creates a narrow <c>SECURITY DEFINER</c> directory function returning distinct <c>tenant_id</c> UUIDs matching
    /// <paramref name="predicateSql"/> alongside an owner-gated <c>FOR SELECT</c> policy (ADR-2548).
    /// </summary>
    public static string EnablePendingWorkDirectoryFunctionSql(
        string schemaName,
        string tableName,
        string functionName,
        string predicateSql,
        string roleName = "cadence_app",
        bool allowMixedCaseIdentifiers = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(predicateSql);
        string quotedSchema = PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema");
        string quotedTable = PostgresIdentifier.Quote(tableName, allowMixedCaseIdentifiers, paramName: "table");
        string quotedFunction = PostgresIdentifier.Quote(functionName, allowMixedCaseIdentifiers, paramName: nameof(functionName));
        string quotedRole = PostgresIdentifier.Quote(roleName, allowMixedCaseIdentifiers, paramName: "role");
        string qualifiedTable = $"{quotedSchema}.{quotedTable}";
        string regclassLiteral = $"{schemaName}.\"{tableName}\"";

        return $"""
            DROP POLICY IF EXISTS {PendingWorkDirectoryPolicy} ON {qualifiedTable};
            CREATE POLICY {PendingWorkDirectoryPolicy} ON {qualifiedTable} FOR SELECT
                USING (pg_catalog.current_setting('{PendingWorkDirectorySetting}', true) = 'on'
                   AND pg_catalog.pg_has_role(current_user, (SELECT c.relowner FROM pg_catalog.pg_class c WHERE c.oid = '{regclassLiteral}'::regclass), 'MEMBER'));

            CREATE OR REPLACE FUNCTION {quotedSchema}.{quotedFunction}() RETURNS SETOF uuid
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
            AS $fn$
            DECLARE
                prev_setting text := pg_catalog.current_setting('{PendingWorkDirectorySetting}', true);
            BEGIN
                PERFORM pg_catalog.set_config('{PendingWorkDirectorySetting}', 'on', true);
                RETURN QUERY
                    SELECT DISTINCT t.tenant_id
                    FROM {qualifiedTable} t
                    WHERE {predicateSql}
                    ORDER BY t.tenant_id;
                PERFORM pg_catalog.set_config('{PendingWorkDirectorySetting}', COALESCE(prev_setting, ''), true);
            EXCEPTION
                WHEN OTHERS THEN
                    PERFORM pg_catalog.set_config('{PendingWorkDirectorySetting}', COALESCE(prev_setting, ''), true);
                    RAISE;
            END;
            $fn$;
            REVOKE ALL ON FUNCTION {quotedSchema}.{quotedFunction}() FROM PUBLIC;
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{roleName}') THEN
                    GRANT EXECUTE ON FUNCTION {quotedSchema}.{quotedFunction}() TO {quotedRole};
                END IF;
            END $$;
            """;
    }

    /// <summary>Drops a pending-work tenant directory function and optionally its <c>FOR SELECT</c> policy.</summary>
    public static string DisablePendingWorkDirectoryFunctionSql(
        string schemaName,
        string tableName,
        string functionName,
        bool dropPolicy = true,
        bool allowMixedCaseIdentifiers = true)
    {
        string quotedSchema = PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema");
        string quotedTable = PostgresIdentifier.Quote(tableName, allowMixedCaseIdentifiers, paramName: "table");
        string quotedFunction = PostgresIdentifier.Quote(functionName, allowMixedCaseIdentifiers, paramName: nameof(functionName));
        string qualifiedTable = $"{quotedSchema}.{quotedTable}";
        string dropPolicySql = dropPolicy
            ? $"DROP POLICY IF EXISTS {PendingWorkDirectoryPolicy} ON {qualifiedTable};"
            : string.Empty;

        return $"""
            DROP FUNCTION IF EXISTS {quotedSchema}.{quotedFunction}();
            {dropPolicySql}
            """;
    }

    /// <summary>
    /// Builds schema-level application role DML grants for kernel and module schemas (ADR-2250 rule 1), revoking
    /// privileges on the migrations history table and applying optional per-table <c>REVOKE UPDATE</c>,
    /// <c>GRANT DELETE</c>, and <c>GRANT EXECUTE</c> clauses.
    /// </summary>
    public static string GrantStrictSchemaRoleSql(
        string schemaName,
        string roleName,
        string migrationsHistoryTable,
        IReadOnlyList<string>? insertOnlyTables = null,
        IReadOnlyList<string>? deletableTables = null,
        IReadOnlyList<string>? executableFunctionSignatures = null,
        string? trailingSql = null,
        string invalidRoleLabel = "lower-case role name")
    {
        string quotedRole = PostgresIdentifier.Quote(roleName, allowMixedCase: false, invalidKindLabel: invalidRoleLabel, paramName: "role");
        string quotedSchema = PostgresIdentifier.Quote(schemaName, allowMixedCase: false, paramName: "schema");
        PostgresIdentifier.Validate(migrationsHistoryTable, allowMixedCase: true, paramName: nameof(migrationsHistoryTable));

        var lines = new List<string>
        {
            $"GRANT USAGE ON SCHEMA {quotedSchema} TO {quotedRole};",
            $"GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA {quotedSchema} TO {quotedRole};",
        };

        if (insertOnlyTables is { Count: > 0 })
        {
            string qualifiedInsertOnly = string.Join(
                ", ",
                insertOnlyTables.Select(tableName =>
                    $"{quotedSchema}.{PostgresIdentifier.Quote(tableName, allowMixedCase: false, paramName: nameof(insertOnlyTables))}"));
            lines.Add($"REVOKE UPDATE ON {qualifiedInsertOnly} FROM {quotedRole};");
        }

        if (deletableTables is { Count: > 0 })
        {
            foreach (string tableName in deletableTables)
            {
                string quotedTable = PostgresIdentifier.Quote(tableName, allowMixedCase: false, paramName: nameof(deletableTables));
                lines.Add($"GRANT DELETE ON {quotedSchema}.{quotedTable} TO {quotedRole};");
            }
        }

        lines.Add($"REVOKE ALL ON {quotedSchema}.\"{migrationsHistoryTable}\" FROM {quotedRole};");

        if (executableFunctionSignatures is { Count: > 0 })
        {
            foreach (string signature in executableFunctionSignatures)
            {
                lines.Add($"GRANT EXECUTE ON FUNCTION {quotedSchema}.{signature} TO {quotedRole};");
            }
        }

        if (!string.IsNullOrWhiteSpace(trailingSql))
        {
            lines.Add(trailingSql);
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Builds conditional <c>DO $$</c> application role DML grants for standalone module schemas, revoking access to
    /// <paramref name="migrationsHistoryTable"/> and granting <c>EXECUTE</c> on zero-argument <c>*_tenant_ids()</c>
    /// directory functions.
    /// </summary>
    public static string GrantConditionalStandaloneSchemaRoleSql(
        string schemaName,
        string roleName = "cadence_app",
        string migrationsHistoryTable = "__EFMigrationsHistory")
    {
        string quotedSchema = PostgresIdentifier.Quote(schemaName, allowMixedCase: true, paramName: "schema");
        string quotedRole = PostgresIdentifier.Quote(roleName, allowMixedCase: true, paramName: "role");
        PostgresIdentifier.Validate(migrationsHistoryTable, allowMixedCase: true, paramName: nameof(migrationsHistoryTable));

        return $"""
            DO $$
            DECLARE
                fn_sig text;
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{roleName}') THEN
                    GRANT USAGE ON SCHEMA {quotedSchema} TO {quotedRole};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {quotedSchema} TO {quotedRole};
                    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {quotedSchema} TO {quotedRole};
                    IF to_regclass('{schemaName}."{migrationsHistoryTable}"') IS NOT NULL THEN
                        REVOKE ALL ON {quotedSchema}."{migrationsHistoryTable}" FROM {quotedRole};
                    END IF;
                    FOR fn_sig IN
                        SELECT p.oid::regprocedure::text
                        FROM pg_catalog.pg_proc p
                        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = '{schemaName}'
                          AND p.proname LIKE '%\_tenant\_ids' ESCAPE '\'
                          AND p.pronargs = 0
                    LOOP
                        EXECUTE format('GRANT EXECUTE ON FUNCTION %s TO %I', fn_sig, '{roleName}');
                    END LOOP;
                END IF;
            END $$;
            """;
    }
}
