using System;
using System.Linq;
using System.Text.RegularExpressions;

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
public static partial class ModuleBitemporalSql
{
    /// <summary>Name of the shared bitemporal append-only trigger function in a schema.</summary>
    public const string AppendOnlyFunctionName = "enforce_bitemporal_append_only";

    /// <summary>SQLSTATE raised when <c>K-TIME-1</c> is violated.</summary>
    public const string AppendOnlyViolationSqlState = "KT001";

    /// <summary>Column name for valid time (<c>tstzrange</c>).</summary>
    public const string ValidPeriodColumn = "valid_period";

    /// <summary>Column name for recorded time (<c>tstzrange</c>).</summary>
    public const string RecordedPeriodColumn = "recorded_period";

    private static readonly string[] MutableColumns =
    [
        RecordedPeriodColumn,
        "version_index",
    ];

    /// <summary>Creates the schema-local trigger function enforcing <c>K-TIME-1</c>.</summary>
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

    /// <summary>Drops the schema-local <c>K-TIME-1</c> trigger function.</summary>
    public static string DropAppendOnlyFunction(string schema) =>
        $"DROP FUNCTION IF EXISTS {Quote(schema)}.{AppendOnlyFunctionName}();";

    /// <summary>Adds the <c>K-TIME-2</c> GiST exclusion constraint and <c>K-TIME-1</c> triggers to a bitemporal table.</summary>
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
                EXCLUDE USING gist ({keys}, {Quote(ValidPeriodColumn)} WITH &&, {Quote(RecordedPeriodColumn)} WITH &&)
                DEFERRABLE INITIALLY DEFERRED;
            CREATE TRIGGER {Quote(RowTriggerName(table))} BEFORE UPDATE OR DELETE ON {qualified}
                FOR EACH ROW EXECUTE FUNCTION {Quote(schema)}.{AppendOnlyFunctionName}();
            CREATE TRIGGER {Quote(TruncateTriggerName(table))} BEFORE TRUNCATE ON {qualified}
                FOR EACH STATEMENT EXECUTE FUNCTION {Quote(schema)}.{AppendOnlyFunctionName}();
            """;
    }

    /// <summary>Drops the <c>K-TIME-1</c> triggers and <c>K-TIME-2</c> exclusion constraint from a bitemporal table.</summary>
    public static string DropGuards(string schema, string table)
    {
        var qualified = $"{Quote(schema)}.{Quote(table)}";
        return $"""
            DROP TRIGGER IF EXISTS {Quote(TruncateTriggerName(table))} ON {qualified};
            DROP TRIGGER IF EXISTS {Quote(RowTriggerName(table))} ON {qualified};
            ALTER TABLE {qualified} DROP CONSTRAINT IF EXISTS {Quote(ExclusionConstraintName(table))};
            """;
    }

    /// <summary>Name of the <c>K-TIME-2</c> exclusion constraint for <paramref name="table"/>.</summary>
    public static string ExclusionConstraintName(string table) => Truncate($"ex_{table}_single_truth");

    /// <summary>Name of the row-level <c>K-TIME-1</c> trigger for <paramref name="table"/>.</summary>
    public static string RowTriggerName(string table) => Truncate($"trg_{table}_append_only");

    /// <summary>Name of the statement-level truncate trigger for <paramref name="table"/>.</summary>
    public static string TruncateTriggerName(string table) => Truncate($"trg_{table}_no_truncate");

    internal static string Quote(string identifier) =>
        identifier is not null && SafeIdentifier().IsMatch(identifier)
            ? $"\"{identifier}\""
            : throw new ArgumentException($"'{identifier}' is not a lower-case snake_case identifier.", nameof(identifier));

    internal static string Truncate(string name) => name.Length <= 63 ? name : name[..63];

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifier();
}
