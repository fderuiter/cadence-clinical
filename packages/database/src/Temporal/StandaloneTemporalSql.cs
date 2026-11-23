using System;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Database.Temporal;

/// <summary>
/// K-XS-2-compliant PostgreSQL bitemporal DDL helpers for standalone clinical schemas (ADR-2246, K-TIME-1, K-TIME-2):
/// <list type="bullet">
/// <item>Ensures <c>btree_gist</c> is installed in <c>public</c> so <c>uuid</c>/<c>text</c> keys compose with <c>tstzrange</c>.</item>
/// <item>Enforces <c>KT001</c> (<c>enforce_bitemporal_append_only</c>): bitemporal period rows are never deleted or truncated,
/// and may only be updated once to close an open <c>recorded_period</c> at a later instant.</item>
/// <item>Enforces deferred <c>EXCLUDE USING gist</c> single-truth non-overlap across <c>(tenant_id, ...entityKeys, valid_period, recorded_period)</c>.</item>
/// </list>
/// </summary>
public static partial class StandaloneTemporalSql
{
    public const string AppendOnlyFunctionName = "enforce_bitemporal_append_only";
    public const string AppendOnlyViolationSqlState = "KT001";
    public const string ValidPeriodColumn = "valid_period";
    public const string RecordedPeriodColumn = "recorded_period";

    public const string EnsureBtreeGistExtensionSql = "CREATE EXTENSION IF NOT EXISTS btree_gist SCHEMA public;";

    private static readonly string[] AllowedUpdateColumns =
    [
        RecordedPeriodColumn,
        "_version",
        "VersionIndex",
        "version_index",
    ];

    public static string CreateAppendOnlyFunctionSql(string schema)
    {
        string s = Quote(schema);
        string stripped = string.Concat(AllowedUpdateColumns.Select(c => $" - '{c}'"));
        return $$"""
            CREATE OR REPLACE FUNCTION {{s}}.{{AppendOnlyFunctionName}}() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
                    RAISE EXCEPTION 'K-TIME-1: %.% is bitemporal and append-only; % is not allowed',
                        TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = '{{AppendOnlyViolationSqlState}}';
                END IF;
                IF (to_jsonb(NEW){{stripped}}) IS DISTINCT FROM (to_jsonb(OLD){{stripped}}) THEN
                    RAISE EXCEPTION 'K-TIME-1: %.% rows are immutable; append a new period row and close recorded_period instead',
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

    public static string DropAppendOnlyFunctionSql(string schema) =>
        $"DROP FUNCTION IF EXISTS {Quote(schema)}.{AppendOnlyFunctionName}();";

    public static string CreateBitemporalGuardsSql(string schema, string table, params string[] entityKeyColumns)
    {
        ArgumentNullException.ThrowIfNull(entityKeyColumns);
        if (entityKeyColumns.Length == 0)
        {
            throw new ArgumentException("At least one entity key column is required for bitemporal exclusion.", nameof(entityKeyColumns));
        }

        string s = Quote(schema);
        string qualified = $"{s}.{Quote(table)}";
        string equalityClauses = string.Join(
            ", ",
            new[] { "tenant_id" }.Concat(entityKeyColumns).Select(col => $"{Quote(col)} WITH ="));
        string exclusionName = Quote(ExclusionConstraintName(table));
        string rowTrigger = Quote(RowTriggerName(table));
        string truncateTrigger = Quote(TruncateTriggerName(table));

        return $"""
            ALTER TABLE {qualified} DROP CONSTRAINT IF EXISTS {exclusionName};
            ALTER TABLE {qualified} ADD CONSTRAINT {exclusionName}
                EXCLUDE USING gist ({equalityClauses}, "{ValidPeriodColumn}" WITH &&, "{RecordedPeriodColumn}" WITH &&)
                DEFERRABLE INITIALLY DEFERRED;
            DROP TRIGGER IF EXISTS {rowTrigger} ON {qualified};
            CREATE TRIGGER {rowTrigger} BEFORE UPDATE OR DELETE ON {qualified}
                FOR EACH ROW EXECUTE FUNCTION {s}.{AppendOnlyFunctionName}();
            DROP TRIGGER IF EXISTS {truncateTrigger} ON {qualified};
            CREATE TRIGGER {truncateTrigger} BEFORE TRUNCATE ON {qualified}
                FOR EACH STATEMENT EXECUTE FUNCTION {s}.{AppendOnlyFunctionName}();
            """;
    }

    public static string DropBitemporalGuardsSql(string schema, string table)
    {
        string qualified = $"{Quote(schema)}.{Quote(table)}";
        return $"""
            DROP TRIGGER IF EXISTS {Quote(TruncateTriggerName(table))} ON {qualified};
            DROP TRIGGER IF EXISTS {Quote(RowTriggerName(table))} ON {qualified};
            ALTER TABLE {qualified} DROP CONSTRAINT IF EXISTS {Quote(ExclusionConstraintName(table))};
            """;
    }

    public static void EnableStandaloneBitemporalGuards(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        params string[] entityKeyColumns)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(EnsureBtreeGistExtensionSql);
        migrationBuilder.Sql(CreateAppendOnlyFunctionSql(schema));
        migrationBuilder.Sql(CreateBitemporalGuardsSql(schema, table, entityKeyColumns));
    }

    public static void DisableStandaloneBitemporalGuards(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(DropBitemporalGuardsSql(schema, table));
    }

    public static string ExclusionConstraintName(string table) => Truncate($"ex_{table}_single_truth");

    public static string RowTriggerName(string table) => Truncate($"trg_{table}_append_only");

    public static string TruncateTriggerName(string table) => Truncate($"trg_{table}_no_truncate");

    internal static string Quote(string identifier) =>
        identifier is not null && SafeIdentifier().IsMatch(identifier)
            ? $"\"{identifier}\""
            : throw new ArgumentException($"'{identifier}' is not a valid PostgreSQL identifier.", nameof(identifier));

    internal static string Truncate(string identifier) =>
        identifier.Length <= 63 ? identifier : identifier[..63];

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifier();
}
