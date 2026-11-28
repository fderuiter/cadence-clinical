namespace Cadence.Kernel.Sql;

/// <summary>
/// Pure BCL PostgreSQL insert-only trigger DDL builders (SQLSTATE <c>KA001</c>, ADR-2621, ADR-2625) shared across
/// kernel, module, and standalone persistence layers.
/// </summary>
public static class PostgresInsertOnlySql
{
    /// <summary>Name of the schema-local insert-only trigger function.</summary>
    public const string FunctionName = "enforce_insert_only";

    /// <summary>SQLSTATE raised when an insert-only table is updated, deleted, or truncated.</summary>
    public const string ViolationSqlState = "KA001";

    /// <summary>Creates the schema-local <c>enforce_insert_only()</c> trigger function.</summary>
    public static string CreateFunctionSql(string schemaName, bool allowMixedCaseIdentifiers = false)
    {
        string quotedSchema = PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema");
        return $$"""
            CREATE OR REPLACE FUNCTION {{quotedSchema}}.{{FunctionName}}() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                RAISE EXCEPTION 'K-AUDIT: %.% is insert-only; % is not allowed',
                    TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = '{{ViolationSqlState}}';
            END;
            $fn$;
            """;
    }

    /// <summary>Drops the schema-local <c>enforce_insert_only()</c> trigger function.</summary>
    public static string DropFunctionSql(string schemaName, bool allowMixedCaseIdentifiers = false) =>
        $"DROP FUNCTION IF EXISTS {PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema")}.{FunctionName}();";

    /// <summary>Attaches <c>BEFORE UPDATE OR DELETE</c> and <c>BEFORE TRUNCATE</c> insert-only triggers.</summary>
    public static string CreateGuardsSql(
        string schemaName,
        string tableName,
        bool allowMixedCaseIdentifiers = false,
        bool dropIfExists = false)
    {
        string quotedSchema = PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema");
        string qualifiedTable = $"{quotedSchema}.{PostgresIdentifier.Quote(tableName, allowMixedCaseIdentifiers, paramName: "table")}";
        string rowTrigger = PostgresIdentifier.Quote(RowTriggerName(tableName), allowMixedCaseIdentifiers, paramName: "table");
        string truncateTrigger = PostgresIdentifier.Quote(TruncateTriggerName(tableName), allowMixedCaseIdentifiers, paramName: "table");

        if (dropIfExists)
        {
            return $"""
                DROP TRIGGER IF EXISTS {rowTrigger} ON {qualifiedTable};
                CREATE TRIGGER {rowTrigger} BEFORE UPDATE OR DELETE ON {qualifiedTable}
                    FOR EACH ROW EXECUTE FUNCTION {quotedSchema}.{FunctionName}();
                DROP TRIGGER IF EXISTS {truncateTrigger} ON {qualifiedTable};
                CREATE TRIGGER {truncateTrigger} BEFORE TRUNCATE ON {qualifiedTable}
                    FOR EACH STATEMENT EXECUTE FUNCTION {quotedSchema}.{FunctionName}();
                """;
        }

        return $"""
            CREATE TRIGGER {rowTrigger} BEFORE UPDATE OR DELETE ON {qualifiedTable}
                FOR EACH ROW EXECUTE FUNCTION {quotedSchema}.{FunctionName}();
            CREATE TRIGGER {truncateTrigger} BEFORE TRUNCATE ON {qualifiedTable}
                FOR EACH STATEMENT EXECUTE FUNCTION {quotedSchema}.{FunctionName}();
            """;
    }

    /// <summary>Drops the insert-only triggers from a table.</summary>
    public static string DropGuardsSql(string schemaName, string tableName, bool allowMixedCaseIdentifiers = false)
    {
        string qualifiedTable =
            $"{PostgresIdentifier.Quote(schemaName, allowMixedCaseIdentifiers, paramName: "schema")}.{PostgresIdentifier.Quote(tableName, allowMixedCaseIdentifiers, paramName: "table")}";
        string truncateTrigger = PostgresIdentifier.Quote(TruncateTriggerName(tableName), allowMixedCaseIdentifiers, paramName: "table");
        string rowTrigger = PostgresIdentifier.Quote(RowTriggerName(tableName), allowMixedCaseIdentifiers, paramName: "table");

        return $"""
            DROP TRIGGER IF EXISTS {truncateTrigger} ON {qualifiedTable};
            DROP TRIGGER IF EXISTS {rowTrigger} ON {qualifiedTable};
            """;
    }

    /// <summary>Name of the row-level insert-only trigger for <paramref name="tableName"/>.</summary>
    public static string RowTriggerName(string tableName) =>
        PostgresIdentifier.Truncate($"trg_{tableName}_insert_only");

    /// <summary>Name of the statement-level truncate trigger for <paramref name="tableName"/>.</summary>
    public static string TruncateTriggerName(string tableName) =>
        PostgresIdentifier.Truncate($"trg_{tableName}_no_truncate");
}
