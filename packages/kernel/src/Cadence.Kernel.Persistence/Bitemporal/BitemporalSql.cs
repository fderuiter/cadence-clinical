using Cadence.Kernel.Modules;

namespace Cadence.Kernel.Persistence.Bitemporal;

/// <summary>
/// Kernel wrapper over <see cref="ModuleBitemporalSql"/> for <c>K-TIME-1</c> and <c>K-TIME-2</c> (spec §4.1, ADR-2246).
/// </summary>
internal static class BitemporalSql
{
    public const string AppendOnlyFunctionName = ModuleBitemporalSql.AppendOnlyFunctionName;

    public const string AppendOnlyViolationSqlState = ModuleBitemporalSql.AppendOnlyViolationSqlState;

    public static string CreateAppendOnlyFunction(string schema) => ModuleBitemporalSql.CreateAppendOnlyFunction(schema);

    public static string DropAppendOnlyFunction(string schema) => ModuleBitemporalSql.DropAppendOnlyFunction(schema);

    public static string CreateGuards(string schema, string table, params string[] entityKeyColumns) =>
        ModuleBitemporalSql.CreateGuards(schema, table, entityKeyColumns);

    public static string DropGuards(string schema, string table) => ModuleBitemporalSql.DropGuards(schema, table);

    public static string ExclusionConstraintName(string table) => ModuleBitemporalSql.ExclusionConstraintName(table);

    public static string RowTriggerName(string table) => ModuleBitemporalSql.RowTriggerName(table);

    public static string TruncateTriggerName(string table) => ModuleBitemporalSql.TruncateTriggerName(table);
}
