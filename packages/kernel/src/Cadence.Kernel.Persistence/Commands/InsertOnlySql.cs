using System;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Kernel.Persistence.Commands;

/// <summary>
/// DDL that makes the audit, event, provenance and idempotency tables insert-only in PostgreSQL itself (spec §5.3
/// item 2): a trigger rejects every <c>UPDATE</c>, <c>DELETE</c> and <c>TRUNCATE</c> with SQLSTATE
/// <see cref="ViolationSqlState"/>, whoever the database user is.
/// @req:PRD-SYS-053
/// </summary>
internal static partial class InsertOnlySql
{
    /// <summary>Name of the shared trigger function, created once in the kernel schema.</summary>
    public const string FunctionName = "enforce_insert_only";

    /// <summary>SQLSTATE raised when an insert-only table is changed.</summary>
    public const string ViolationSqlState = "KA001";

    /// <summary>The kernel tables that are insert-only.</summary>
    public static readonly string[] Tables = ["audit_entries", "domain_events", "provenance_activities", "idempotency_records"];

    public static string CreateFunction(string schema) => $$"""
        CREATE OR REPLACE FUNCTION {{Quote(schema)}}.{{FunctionName}}() RETURNS trigger
        LANGUAGE plpgsql AS $fn$
        BEGIN
            RAISE EXCEPTION 'K-AUDIT: %.% is insert-only; % is not allowed',
                TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = '{{ViolationSqlState}}';
        END;
        $fn$;
        """;

    public static string DropFunction(string schema) =>
        $"DROP FUNCTION IF EXISTS {Quote(schema)}.{FunctionName}();";

    public static string CreateGuards(string schema, string table)
    {
        var qualified = $"{Quote(schema)}.{Quote(table)}";
        return $"""
            CREATE TRIGGER {Quote(RowTriggerName(table))} BEFORE UPDATE OR DELETE ON {qualified}
                FOR EACH ROW EXECUTE FUNCTION {Quote(schema)}.{FunctionName}();
            CREATE TRIGGER {Quote(TruncateTriggerName(table))} BEFORE TRUNCATE ON {qualified}
                FOR EACH STATEMENT EXECUTE FUNCTION {Quote(schema)}.{FunctionName}();
            """;
    }

    public static string DropGuards(string schema, string table)
    {
        var qualified = $"{Quote(schema)}.{Quote(table)}";
        return $"""
            DROP TRIGGER IF EXISTS {Quote(TruncateTriggerName(table))} ON {qualified};
            DROP TRIGGER IF EXISTS {Quote(RowTriggerName(table))} ON {qualified};
            """;
    }

    public static string RowTriggerName(string table) => Truncate($"trg_{table}_insert_only");

    public static string TruncateTriggerName(string table) => Truncate($"trg_{table}_no_truncate");

    private static string Truncate(string name) => name.Length <= 63 ? name : name[..63];

    private static string Quote(string identifier) =>
        SafeIdentifier().IsMatch(identifier)
            ? $"\"{identifier}\""
            : throw new ArgumentException($"'{identifier}' is not a lower-case snake_case identifier.", nameof(identifier));

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SafeIdentifier();
}

/// <summary>Migration helpers for <see cref="InsertOnlySql"/>.</summary>
internal static class InsertOnlyMigrationBuilderExtensions
{
    /// <summary>Creates the trigger function and guards every table in <see cref="InsertOnlySql.Tables"/>.</summary>
    public static void CreateInsertOnlyGuards(this MigrationBuilder migrationBuilder, string schema)
    {
        migrationBuilder.Sql(InsertOnlySql.CreateFunction(schema));
        foreach (var table in InsertOnlySql.Tables)
        {
            migrationBuilder.Sql(InsertOnlySql.CreateGuards(schema, table));
        }
    }

    public static void DropInsertOnlyGuards(this MigrationBuilder migrationBuilder, string schema)
    {
        foreach (var table in InsertOnlySql.Tables)
        {
            migrationBuilder.Sql(InsertOnlySql.DropGuards(schema, table));
        }

        migrationBuilder.Sql(InsertOnlySql.DropFunction(schema));
    }
}
