using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.Kernel.Persistence.Bitemporal;

/// <summary>
/// DDL that enforces the bitemporal invariants in PostgreSQL itself (spec §4.1, ADR-2246):
/// <list type="bullet">
/// <item><b>K-TIME-1</b>: a row is never updated except to close its open <c>recorded_period</c> at a later instant, and
/// is never deleted or truncated (<see cref="AppendOnlyFunctionName"/> trigger).</item>
/// <item><b>K-TIME-2</b>: no two rows for the same tenant and entity key overlap in both valid and recorded time
/// (<c>EXCLUDE USING gist</c>, deferred to commit so a correction can close the old row and insert the new one in
/// either order).</item>
/// </list>
/// Used from migrations through <see cref="BitemporalMigrationBuilderExtensions"/>.
/// @req:PRD-SYS-053
/// </summary>
internal static partial class BitemporalSql
{
    /// <summary>Name of the shared trigger function, created once in the kernel schema.</summary>
    public const string AppendOnlyFunctionName = "enforce_bitemporal_append_only";

    /// <summary>SQLSTATE raised when K-TIME-1 is violated.</summary>
    public const string AppendOnlyViolationSqlState = "KT001";

    /// <summary>Columns an update may change: the closing of <c>recorded_period</c> and the audit version counter.</summary>
    private static readonly string[] MutableColumns =
    [
        BitemporalEntityTypeBuilderExtensions.RecordedPeriodColumn,
        "version_index",
    ];

    public static string CreateAppendOnlyFunction(string schema)
    {
        var excluded = string.Concat(MutableColumns.Select(c => $" - '{c}'"));
        return $$"""
            CREATE OR REPLACE FUNCTION {{Quote(schema)}}.{{AppendOnlyFunctionName}}() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
                    RAISE EXCEPTION 'K-TIME-1: %.% is bitemporal and append-only; % is not allowed',
                        TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = '{{AppendOnlyViolationSqlState}}';
                END IF;
                IF (to_jsonb(NEW){{excluded}}) IS DISTINCT FROM (to_jsonb(OLD){{excluded}}) THEN
                    RAISE EXCEPTION 'K-TIME-1: %.% rows are never updated; record a new row and close recorded_period instead',
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

    public static string DropAppendOnlyFunction(string schema) =>
        $"DROP FUNCTION IF EXISTS {Quote(schema)}.{AppendOnlyFunctionName}();";

    /// <summary>
    /// Adds the K-TIME-2 exclusion constraint and the K-TIME-1 triggers to a bitemporal table.
    /// </summary>
    /// <param name="schema">Table schema (normally <c>kernel</c>).</param>
    /// <param name="table">Table name.</param>
    /// <param name="entityKeyColumns">
    /// Columns identifying "the same fact" (for example <c>participant_id</c>, <c>concept_id</c>); <c>tenant_id</c>
    /// is always included first.
    /// </param>
    public static string CreateGuards(string schema, string table, params string[] entityKeyColumns)
    {
        ArgumentNullException.ThrowIfNull(entityKeyColumns);
        if (entityKeyColumns.Length == 0)
        {
            throw new ArgumentException("A single-truth constraint needs at least one entity key column.", nameof(entityKeyColumns));
        }

        var qualified = $"{Quote(schema)}.{Quote(table)}";
        var keys = string.Join(", ", new[] { "tenant_id" }.Concat(entityKeyColumns).Select(c => $"{Quote(c)} WITH ="));
        return $"""
            ALTER TABLE {qualified} ADD CONSTRAINT {Quote(ExclusionConstraintName(table))}
                EXCLUDE USING gist ({keys}, {Quote(BitemporalEntityTypeBuilderExtensions.ValidPeriodColumn)} WITH &&, {Quote(BitemporalEntityTypeBuilderExtensions.RecordedPeriodColumn)} WITH &&)
                DEFERRABLE INITIALLY DEFERRED;
            CREATE TRIGGER {Quote(RowTriggerName(table))} BEFORE UPDATE OR DELETE ON {qualified}
                FOR EACH ROW EXECUTE FUNCTION {Quote(schema)}.{AppendOnlyFunctionName}();
            CREATE TRIGGER {Quote(TruncateTriggerName(table))} BEFORE TRUNCATE ON {qualified}
                FOR EACH STATEMENT EXECUTE FUNCTION {Quote(schema)}.{AppendOnlyFunctionName}();
            """;
    }

    public static string DropGuards(string schema, string table)
    {
        var qualified = $"{Quote(schema)}.{Quote(table)}";
        return $"""
            DROP TRIGGER IF EXISTS {Quote(TruncateTriggerName(table))} ON {qualified};
            DROP TRIGGER IF EXISTS {Quote(RowTriggerName(table))} ON {qualified};
            ALTER TABLE {qualified} DROP CONSTRAINT IF EXISTS {Quote(ExclusionConstraintName(table))};
            """;
    }

    public static string ExclusionConstraintName(string table) => Truncate($"ex_{table}_single_truth");

    public static string RowTriggerName(string table) => Truncate($"trg_{table}_append_only");

    public static string TruncateTriggerName(string table) => Truncate($"trg_{table}_no_truncate");

    private static string Truncate(string name) => name.Length <= 63 ? name : name[..63];

    private static string Quote(string identifier) =>
        SafeIdentifier().IsMatch(identifier)
            ? $"\"{identifier}\""
            : throw new ArgumentException($"'{identifier}' is not a lower-case snake_case identifier.", nameof(identifier));

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SafeIdentifier();
}
