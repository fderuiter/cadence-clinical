using System;
using System.Collections.Generic;

using Cadence.Kernel.Sql;

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
public static class StandaloneTemporalSql
{
    public const string AppendOnlyFunctionName = PostgresBitemporalSql.AppendOnlyFunctionName;
    public const string AppendOnlyViolationSqlState = PostgresBitemporalSql.AppendOnlyViolationSqlState;
    public const string ValidPeriodColumn = PostgresBitemporalSql.ValidPeriodColumn;
    public const string RecordedPeriodColumn = PostgresBitemporalSql.RecordedPeriodColumn;

    public const string EnsureBtreeGistExtensionSql = PostgresBitemporalSql.EnsureBtreeGistExtensionSql;

    private const string ImmutableRowMessage =
        "K-TIME-1: %.% rows are immutable; append a new period row and close recorded_period instead";

    private const string EmptyKeysMessage =
        "At least one entity key column is required for bitemporal exclusion.";

    private static readonly IReadOnlyList<string> AllowedUpdateColumns = PostgresBitemporalSql.StandaloneMutableColumns;

    public static string CreateAppendOnlyFunctionSql(string schema) =>
        PostgresBitemporalSql.CreateAppendOnlyFunctionSql(
            schema,
            AllowedUpdateColumns,
            allowMixedCaseIdentifiers: true,
            immutableRowMessage: ImmutableRowMessage);

    public static string DropAppendOnlyFunctionSql(string schema) =>
        PostgresBitemporalSql.DropAppendOnlyFunctionSql(schema, allowMixedCaseIdentifiers: true);

    public static string CreateBitemporalGuardsSql(string schema, string table, params string[] entityKeyColumns) =>
        PostgresBitemporalSql.CreateGuardsSql(
            schema,
            table,
            entityKeyColumns,
            allowMixedCaseIdentifiers: true,
            dropIfExists: true,
            emptyKeysMessage: EmptyKeysMessage);

    public static string DropBitemporalGuardsSql(string schema, string table) =>
        PostgresBitemporalSql.DropGuardsSql(schema, table, allowMixedCaseIdentifiers: true);

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

    public static string ExclusionConstraintName(string table) => PostgresBitemporalSql.ExclusionConstraintName(table);

    public static string RowTriggerName(string table) => PostgresBitemporalSql.RowTriggerName(table);

    public static string TruncateTriggerName(string table) => PostgresBitemporalSql.TruncateTriggerName(table);

    internal static string Quote(string identifier) => PostgresIdentifier.Quote(identifier, allowMixedCase: true);

    internal static string Truncate(string identifier) => PostgresIdentifier.Truncate(identifier);
}
