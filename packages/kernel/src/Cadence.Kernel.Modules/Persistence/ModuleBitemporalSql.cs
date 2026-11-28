using Cadence.Kernel.Sql;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Shared PostgreSQL DDL enforcing bitemporal invariants (<c>K-TIME-1</c>, <c>K-TIME-2</c>, ADR-2246, ADR-2625) across
/// kernel and module schemas:
/// <list type="bullet">
/// <item><b>K-TIME-1</b>: a row is never updated except to close its open <c>recorded_period</c> at a later instant,
/// and is never deleted or truncated (raises SQLSTATE <see cref="AppendOnlyViolationSqlState"/>).</item>
/// <item><b>K-TIME-2</b>: no two rows for the same tenant and entity key overlap in both valid and recorded time
/// (<c>EXCLUDE USING gist</c>, deferred to commit).</item>
/// </list>
/// </summary>
public static class ModuleBitemporalSql
{
    /// <summary>Name of the shared bitemporal append-only trigger function in a schema.</summary>
    public const string AppendOnlyFunctionName = PostgresBitemporalSql.AppendOnlyFunctionName;

    /// <summary>SQLSTATE raised when <c>K-TIME-1</c> is violated.</summary>
    public const string AppendOnlyViolationSqlState = PostgresBitemporalSql.AppendOnlyViolationSqlState;

    /// <summary>Column name for valid time (<c>tstzrange</c>).</summary>
    public const string ValidPeriodColumn = PostgresBitemporalSql.ValidPeriodColumn;

    /// <summary>Column name for recorded time (<c>tstzrange</c>).</summary>
    public const string RecordedPeriodColumn = PostgresBitemporalSql.RecordedPeriodColumn;

    /// <summary>Creates the schema-local trigger function enforcing <c>K-TIME-1</c>.</summary>
    public static string CreateAppendOnlyFunction(string schema) =>
        PostgresBitemporalSql.CreateAppendOnlyFunctionSql(schema);

    /// <summary>Drops the schema-local <c>K-TIME-1</c> trigger function.</summary>
    public static string DropAppendOnlyFunction(string schema) =>
        PostgresBitemporalSql.DropAppendOnlyFunctionSql(schema);

    /// <summary>Adds the <c>K-TIME-2</c> GiST exclusion constraint and <c>K-TIME-1</c> triggers to a bitemporal table.</summary>
    public static string CreateGuards(string schema, string table, params string[] entityKeyColumns) =>
        PostgresBitemporalSql.CreateGuardsSql(schema, table, entityKeyColumns);

    /// <summary>Drops the <c>K-TIME-1</c> triggers and <c>K-TIME-2</c> exclusion constraint from a bitemporal table.</summary>
    public static string DropGuards(string schema, string table) =>
        PostgresBitemporalSql.DropGuardsSql(schema, table);

    /// <summary>Name of the <c>K-TIME-2</c> exclusion constraint for <paramref name="table"/>.</summary>
    public static string ExclusionConstraintName(string table) => PostgresBitemporalSql.ExclusionConstraintName(table);

    /// <summary>Name of the row-level <c>K-TIME-1</c> trigger for <paramref name="table"/>.</summary>
    public static string RowTriggerName(string table) => PostgresBitemporalSql.RowTriggerName(table);

    /// <summary>Name of the statement-level truncate trigger for <paramref name="table"/>.</summary>
    public static string TruncateTriggerName(string table) => PostgresBitemporalSql.TruncateTriggerName(table);

    internal static string Quote(string identifier) => PostgresIdentifier.Quote(identifier);

    internal static string Truncate(string name) => PostgresIdentifier.Truncate(name);
}
