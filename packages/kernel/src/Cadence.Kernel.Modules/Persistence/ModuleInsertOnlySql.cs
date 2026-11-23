namespace Cadence.Kernel.Modules;

/// <summary>
/// Shared PostgreSQL DDL making audit, message, and digest tables strictly insert-only (spec §5.3, ADR-2625):
/// rejects every <c>UPDATE</c>, <c>DELETE</c>, and <c>TRUNCATE</c> with SQLSTATE <see cref="ViolationSqlState"/>.
/// </summary>
public static class ModuleInsertOnlySql
{
    /// <summary>Name of the shared insert-only trigger function in a schema.</summary>
    public const string FunctionName = "enforce_insert_only";

    /// <summary>SQLSTATE raised when an insert-only table is modified.</summary>
    public const string ViolationSqlState = "KA001";

    /// <summary>Creates the schema-local insert-only trigger function.</summary>
    public static string CreateFunction(string schema) => $$"""
        CREATE OR REPLACE FUNCTION {{ModuleBitemporalSql.Quote(schema)}}.{{FunctionName}}() RETURNS trigger
        LANGUAGE plpgsql AS $fn$
        BEGIN
            RAISE EXCEPTION 'K-AUDIT: %.% is insert-only; % is not allowed',
                TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = '{{ViolationSqlState}}';
        END;
        $fn$;
        """;

    /// <summary>Drops the schema-local insert-only trigger function.</summary>
    public static string DropFunction(string schema) =>
        $"DROP FUNCTION IF EXISTS {ModuleBitemporalSql.Quote(schema)}.{FunctionName}();";

    /// <summary>Attaches <c>BEFORE UPDATE OR DELETE</c> and <c>BEFORE TRUNCATE</c> insert-only triggers to <paramref name="table"/>.</summary>
    public static string CreateGuards(string schema, string table)
    {
        var qualified = $"{ModuleBitemporalSql.Quote(schema)}.{ModuleBitemporalSql.Quote(table)}";
        return $"""
            CREATE TRIGGER {ModuleBitemporalSql.Quote(RowTriggerName(table))} BEFORE UPDATE OR DELETE ON {qualified}
                FOR EACH ROW EXECUTE FUNCTION {ModuleBitemporalSql.Quote(schema)}.{FunctionName}();
            CREATE TRIGGER {ModuleBitemporalSql.Quote(TruncateTriggerName(table))} BEFORE TRUNCATE ON {qualified}
                FOR EACH STATEMENT EXECUTE FUNCTION {ModuleBitemporalSql.Quote(schema)}.{FunctionName}();
            """;
    }

    /// <summary>Drops the insert-only triggers from <paramref name="table"/>.</summary>
    public static string DropGuards(string schema, string table)
    {
        var qualified = $"{ModuleBitemporalSql.Quote(schema)}.{ModuleBitemporalSql.Quote(table)}";
        return $"""
            DROP TRIGGER IF EXISTS {ModuleBitemporalSql.Quote(TruncateTriggerName(table))} ON {qualified};
            DROP TRIGGER IF EXISTS {ModuleBitemporalSql.Quote(RowTriggerName(table))} ON {qualified};
            """;
    }

    /// <summary>Name of the row-level insert-only trigger for <paramref name="table"/>.</summary>
    public static string RowTriggerName(string table) => ModuleBitemporalSql.Truncate($"trg_{table}_insert_only");

    /// <summary>Name of the statement-level truncate trigger for <paramref name="table"/>.</summary>
    public static string TruncateTriggerName(string table) => ModuleBitemporalSql.Truncate($"trg_{table}_no_truncate");
}
