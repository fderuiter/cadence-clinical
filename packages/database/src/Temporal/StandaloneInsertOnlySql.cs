using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Database.Temporal;

/// <summary>
/// K-XS-2-compliant PostgreSQL insert-only trigger DDL helpers for standalone clinical schemas (KA001, ADR-2621).
/// Rejects every <c>UPDATE</c>, <c>DELETE</c>, and <c>TRUNCATE</c> on historical revision tables, financial ledger
/// entries, and GxP audit records with SQLSTATE <see cref="ViolationSqlState"/> (<c>KA001</c>).
/// </summary>
public static class StandaloneInsertOnlySql
{
    public const string FunctionName = "enforce_insert_only";
    public const string ViolationSqlState = "KA001";

    public static string CreateInsertOnlyFunctionSql(string schema)
    {
        string s = StandaloneTemporalSql.Quote(schema);
        return $$"""
            CREATE OR REPLACE FUNCTION {{s}}.{{FunctionName}}() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                RAISE EXCEPTION 'K-AUDIT: %.% is insert-only; % is not allowed',
                    TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = '{{ViolationSqlState}}';
            END;
            $fn$;
            """;
    }

    public static string DropInsertOnlyFunctionSql(string schema) =>
        $"DROP FUNCTION IF EXISTS {StandaloneTemporalSql.Quote(schema)}.{FunctionName}();";

    public static string CreateInsertOnlyGuardsSql(string schema, string table)
    {
        string s = StandaloneTemporalSql.Quote(schema);
        string qualified = $"{s}.{StandaloneTemporalSql.Quote(table)}";
        string rowTrigger = StandaloneTemporalSql.Quote(RowTriggerName(table));
        string truncateTrigger = StandaloneTemporalSql.Quote(TruncateTriggerName(table));

        return $"""
            DROP TRIGGER IF EXISTS {rowTrigger} ON {qualified};
            CREATE TRIGGER {rowTrigger} BEFORE UPDATE OR DELETE ON {qualified}
                FOR EACH ROW EXECUTE FUNCTION {s}.{FunctionName}();
            DROP TRIGGER IF EXISTS {truncateTrigger} ON {qualified};
            CREATE TRIGGER {truncateTrigger} BEFORE TRUNCATE ON {qualified}
                FOR EACH STATEMENT EXECUTE FUNCTION {s}.{FunctionName}();
            """;
    }

    public static string DropInsertOnlyGuardsSql(string schema, string table)
    {
        string qualified = $"{StandaloneTemporalSql.Quote(schema)}.{StandaloneTemporalSql.Quote(table)}";
        return $"""
            DROP TRIGGER IF EXISTS {StandaloneTemporalSql.Quote(TruncateTriggerName(table))} ON {qualified};
            DROP TRIGGER IF EXISTS {StandaloneTemporalSql.Quote(RowTriggerName(table))} ON {qualified};
            """;
    }

    public static void EnableStandaloneInsertOnlyGuards(
        this MigrationBuilder migrationBuilder,
        string schema,
        IEnumerable<string> tables)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentNullException.ThrowIfNull(tables);

        migrationBuilder.Sql(CreateInsertOnlyFunctionSql(schema));
        foreach (string table in tables)
        {
            migrationBuilder.Sql(CreateInsertOnlyGuardsSql(schema, table));
        }
    }

    public static void DisableStandaloneInsertOnlyGuards(
        this MigrationBuilder migrationBuilder,
        string schema,
        IEnumerable<string> tables)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentNullException.ThrowIfNull(tables);

        foreach (string table in tables)
        {
            migrationBuilder.Sql(DropInsertOnlyGuardsSql(schema, table));
        }
    }

    public static string RowTriggerName(string table) =>
        StandaloneTemporalSql.Truncate($"trg_{table}_insert_only");

    public static string TruncateTriggerName(string table) =>
        StandaloneTemporalSql.Truncate($"trg_{table}_no_truncate");
}
