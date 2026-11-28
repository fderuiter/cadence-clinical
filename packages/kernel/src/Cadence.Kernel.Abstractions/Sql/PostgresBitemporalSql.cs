using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Kernel.Sql;

/// <summary>
/// Pure BCL PostgreSQL bitemporal DDL builders (<c>K-TIME-1</c>, <c>K-TIME-2</c>, SQLSTATE <c>KT001</c>, ADR-2246)
/// shared across kernel, module, and standalone persistence layers.
/// </summary>
public static class PostgresBitemporalSql
{
    /// <summary>Name of the schema-local bitemporal append-only trigger function.</summary>
    public const string AppendOnlyFunctionName = "enforce_bitemporal_append_only";

    /// <summary>SQLSTATE raised when <c>K-TIME-1</c> is violated.</summary>
    public const string AppendOnlyViolationSqlState = "KT001";

    /// <summary>Column name for valid time (<c>tstzrange</c>).</summary>
    public const string ValidPeriodColumn = "valid_period";

    /// <summary>Column name for recorded time (<c>tstzrange</c>).</summary>
    public const string RecordedPeriodColumn = "recorded_period";

    /// <summary>Idempotent DDL ensuring <c>btree_gist</c> is available in <c>public</c>.</summary>
    public const string EnsureBtreeGistExtensionSql = "CREATE EXTENSION IF NOT EXISTS btree_gist SCHEMA public;";

    /// <summary>Default mutable columns for lower-case <c>snake_case</c> kernel and module bitemporal tables.</summary>
    public static IReadOnlyList<string> StrictMutableColumns { get; } =
    [
        RecordedPeriodColumn,
        "version_index",
    ];

    /// <summary>Default mutable columns for standalone bitemporal tables supporting mixed-case optimistic concurrency tokens.</summary>
    public static IReadOnlyList<string> StandaloneMutableColumns { get; } =
    [
        RecordedPeriodColumn,
        "_version",
        "VersionIndex",
        "version_index",
    ];

    /// <summary>Creates the schema-local <c>enforce_bitemporal_append_only()</c> trigger function enforcing <c>K-TIME-1</c>.</summary>
    public static string CreateAppendOnlyFunctionSql(
        string schemaName,
        IReadOnlyList<string>? mutableColumns = null,
        bool allowMixedCaseIdentifiers = false,
        string? immutableRowMessage = null)
    {
        string quotedSchema = PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema");
        var allowedColumns = mutableColumns ?? StrictMutableColumns;
        foreach (string columnName in allowedColumns)
        {
            PostgresIdentifier.Validate(columnName, allowMixedCase: true, paramName: nameof(mutableColumns));
        }

        string strippedColumns = string.Concat(allowedColumns.Select(col => $" - '{col}'"));
        string rowMessage = immutableRowMessage
            ?? "K-TIME-1: %.% rows are never updated; record a new row and close recorded_period instead";

        return $$"""
            CREATE OR REPLACE FUNCTION {{quotedSchema}}.{{AppendOnlyFunctionName}}() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
                    RAISE EXCEPTION 'K-TIME-1: %.% is bitemporal and append-only; % is not allowed',
                        TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = '{{AppendOnlyViolationSqlState}}';
                END IF;
                IF (to_jsonb(NEW){{strippedColumns}}) IS DISTINCT FROM (to_jsonb(OLD){{strippedColumns}}) THEN
                    RAISE EXCEPTION '{{rowMessage}}',
                        TG_TABLE_SCHEMA, TG_TABLE_NAME USING ERRCODE = '{{AppendOnlyViolationSqlState}}';
                END IF;
                IF NOT upper_inf(OLD.recorded_period)
                    OR upper_inf(NEW.recorded_period)
                    OR isempty(NEW.recorded_period)
                    OR lower(NEW.recorded_period) IS DISTINCT FROM lower(OLD.recorded_period) THEN
                    RAISE EXCEPTION 'K-TIME-1: %.% may only close an open recorded_period at a later instant',
                        TG_TABLE_SCHEMA, TG_TABLE_NAME USING ERRCODE = '{{AppendOnlyViolationSqlState}}';
                END IF;
                RETURN NEW;
            END;
            $fn$;
            """;
    }

    /// <summary>Drops the schema-local <c>K-TIME-1</c> trigger function.</summary>
    public static string DropAppendOnlyFunctionSql(string schemaName, bool allowMixedCaseIdentifiers = false) =>
        $"DROP FUNCTION IF EXISTS {PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema")}.{AppendOnlyFunctionName}();";

    /// <summary>Creates the <c>K-TIME-2</c> GiST exclusion constraint and <c>K-TIME-1</c> row/statement triggers.</summary>
    public static string CreateGuardsSql(
        string schemaName,
        string tableName,
        IReadOnlyList<string> entityKeyColumns,
        bool allowMixedCaseIdentifiers = false,
        bool dropIfExists = false,
        string? emptyKeysMessage = null)
    {
        ArgumentNullException.ThrowIfNull(entityKeyColumns);
        if (entityKeyColumns.Count == 0)
        {
            throw new ArgumentException(
                emptyKeysMessage ?? "A single-truth constraint needs at least one entity key column.",
                nameof(entityKeyColumns));
        }

        string quotedSchema = PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema");
        string qualifiedTable = $"{quotedSchema}.{PostgresIdentifier.Quote(tableName, allowMixedCaseIdentifiers, paramName: "table")}";
        string equalityClauses = string.Join(
            ", ",
            new[] { "tenant_id" }
                .Concat(entityKeyColumns)
                .Select(col => $"{PostgresIdentifier.Quote(col, allowMixedCaseIdentifiers, paramName: nameof(entityKeyColumns))} WITH ="));
        string exclusionName = PostgresIdentifier.Quote(ExclusionConstraintName(tableName), allowMixedCaseIdentifiers, paramName: "table");
        string rowTrigger = PostgresIdentifier.Quote(RowTriggerName(tableName), allowMixedCaseIdentifiers, paramName: "table");
        string truncateTrigger = PostgresIdentifier.Quote(TruncateTriggerName(tableName), allowMixedCaseIdentifiers, paramName: "table");
        string validPeriod = PostgresIdentifier.Quote(ValidPeriodColumn);
        string recordedPeriod = PostgresIdentifier.Quote(RecordedPeriodColumn);

        if (dropIfExists)
        {
            return $"""
                ALTER TABLE {qualifiedTable} DROP CONSTRAINT IF EXISTS {exclusionName};
                ALTER TABLE {qualifiedTable} ADD CONSTRAINT {exclusionName}
                    EXCLUDE USING gist ({equalityClauses}, {validPeriod} WITH &&, {recordedPeriod} WITH &&)
                    DEFERRABLE INITIALLY DEFERRED;
                DROP TRIGGER IF EXISTS {rowTrigger} ON {qualifiedTable};
                CREATE TRIGGER {rowTrigger} BEFORE UPDATE OR DELETE ON {qualifiedTable}
                    FOR EACH ROW EXECUTE FUNCTION {quotedSchema}.{AppendOnlyFunctionName}();
                DROP TRIGGER IF EXISTS {truncateTrigger} ON {qualifiedTable};
                CREATE TRIGGER {truncateTrigger} BEFORE TRUNCATE ON {qualifiedTable}
                    FOR EACH STATEMENT EXECUTE FUNCTION {quotedSchema}.{AppendOnlyFunctionName}();
                """;
        }

        return $"""
            ALTER TABLE {qualifiedTable} ADD CONSTRAINT {exclusionName}
                EXCLUDE USING gist ({equalityClauses}, {validPeriod} WITH &&, {recordedPeriod} WITH &&)
                DEFERRABLE INITIALLY DEFERRED;
            CREATE TRIGGER {rowTrigger} BEFORE UPDATE OR DELETE ON {qualifiedTable}
                FOR EACH ROW EXECUTE FUNCTION {quotedSchema}.{AppendOnlyFunctionName}();
            CREATE TRIGGER {truncateTrigger} BEFORE TRUNCATE ON {qualifiedTable}
                FOR EACH STATEMENT EXECUTE FUNCTION {quotedSchema}.{AppendOnlyFunctionName}();
            """;
    }

    /// <summary>Drops the <c>K-TIME-1</c> triggers and <c>K-TIME-2</c> exclusion constraint from a bitemporal table.</summary>
    public static string DropGuardsSql(string schemaName, string tableName, bool allowMixedCaseIdentifiers = false)
    {
        string qualifiedTable =
            $"{PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema")}.{PostgresIdentifier.Quote(tableName, allowMixedCaseIdentifiers, paramName: "table")}";
        string truncateTrigger = PostgresIdentifier.Quote(TruncateTriggerName(tableName), allowMixedCaseIdentifiers, paramName: "table");
        string rowTrigger = PostgresIdentifier.Quote(RowTriggerName(tableName), allowMixedCaseIdentifiers, paramName: "table");
        string exclusionName = PostgresIdentifier.Quote(ExclusionConstraintName(tableName), allowMixedCaseIdentifiers, paramName: "table");

        return $"""
            DROP TRIGGER IF EXISTS {truncateTrigger} ON {qualifiedTable};
            DROP TRIGGER IF EXISTS {rowTrigger} ON {qualifiedTable};
            ALTER TABLE {qualifiedTable} DROP CONSTRAINT IF EXISTS {exclusionName};
            """;
    }

    /// <summary>Name of the <c>K-TIME-2</c> exclusion constraint for <paramref name="tableName"/>.</summary>
    public static string ExclusionConstraintName(string tableName) =>
        PostgresIdentifier.Truncate($"ex_{tableName}_single_truth");

    /// <summary>Name of the row-level <c>K-TIME-1</c> trigger for <paramref name="tableName"/>.</summary>
    public static string RowTriggerName(string tableName) =>
        PostgresIdentifier.Truncate($"trg_{tableName}_append_only");

    /// <summary>Name of the statement-level truncate trigger for <paramref name="tableName"/>.</summary>
    public static string TruncateTriggerName(string tableName) =>
        PostgresIdentifier.Truncate($"trg_{tableName}_no_truncate");
}
