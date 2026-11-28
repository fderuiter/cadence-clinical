using Cadence.Kernel.Sql;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Shared PostgreSQL DDL making audit, message, and digest tables strictly insert-only (spec §5.3, ADR-2625):
/// rejects every <c>UPDATE</c>, <c>DELETE</c>, and <c>TRUNCATE</c> with SQLSTATE <see cref="ViolationSqlState"/>.
/// </summary>
public static class ModuleInsertOnlySql
{
    /// <summary>Name of the shared insert-only trigger function in a schema.</summary>
    public const string FunctionName = PostgresInsertOnlySql.FunctionName;

    /// <summary>SQLSTATE raised when an insert-only table is modified.</summary>
    public const string ViolationSqlState = PostgresInsertOnlySql.ViolationSqlState;

    /// <summary>Creates the schema-local insert-only trigger function.</summary>
    public static string CreateFunction(string schema) => PostgresInsertOnlySql.CreateFunctionSql(schema);

    /// <summary>Drops the schema-local insert-only trigger function.</summary>
    public static string DropFunction(string schema) => PostgresInsertOnlySql.DropFunctionSql(schema);

    /// <summary>Attaches <c>BEFORE UPDATE OR DELETE</c> and <c>BEFORE TRUNCATE</c> insert-only triggers to <paramref name="table"/>.</summary>
    public static string CreateGuards(string schema, string table) => PostgresInsertOnlySql.CreateGuardsSql(schema, table);

    /// <summary>Drops the insert-only triggers from <paramref name="table"/>.</summary>
    public static string DropGuards(string schema, string table) => PostgresInsertOnlySql.DropGuardsSql(schema, table);

    /// <summary>Name of the row-level insert-only trigger for <paramref name="table"/>.</summary>
    public static string RowTriggerName(string table) => PostgresInsertOnlySql.RowTriggerName(table);

    /// <summary>Name of the statement-level truncate trigger for <paramref name="table"/>.</summary>
    public static string TruncateTriggerName(string table) => PostgresInsertOnlySql.TruncateTriggerName(table);
}
