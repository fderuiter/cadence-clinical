using Cadence.Kernel.Sql;

namespace Cadence.Kernel.Persistence.Bitemporal;

/// <summary>
/// Kernel wrapper over <see cref="PostgresBitemporalSql"/> for <c>K-TIME-1</c> and <c>K-TIME-2</c> (spec §4.1, ADR-2246).
/// </summary>
internal static class BitemporalSql
{
    public const string AppendOnlyFunctionName = PostgresBitemporalSql.AppendOnlyFunctionName;

    public const string AppendOnlyViolationSqlState = PostgresBitemporalSql.AppendOnlyViolationSqlState;

    public static string CreateAppendOnlyFunction(string schema) => PostgresBitemporalSql.CreateAppendOnlyFunctionSql(schema);

    public static string DropAppendOnlyFunction(string schema) => PostgresBitemporalSql.DropAppendOnlyFunctionSql(schema);

    public static string CreateGuards(string schema, string table, params string[] entityKeyColumns) =>
        PostgresBitemporalSql.CreateGuardsSql(schema, table, entityKeyColumns);

    public static string DropGuards(string schema, string table) => PostgresBitemporalSql.DropGuardsSql(schema, table);

    public static string ExclusionConstraintName(string table) => PostgresBitemporalSql.ExclusionConstraintName(table);

    public static string RowTriggerName(string table) => PostgresBitemporalSql.RowTriggerName(table);

    public static string TruncateTriggerName(string table) => PostgresBitemporalSql.TruncateTriggerName(table);
}
