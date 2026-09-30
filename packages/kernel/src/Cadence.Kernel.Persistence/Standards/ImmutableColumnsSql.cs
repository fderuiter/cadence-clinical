using System.Linq;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Kernel.Persistence.Standards;

/// <summary>
/// DDL that fixes the content of governed records in PostgreSQL itself (spec §6.3): once a standard version or library
/// item version is registered, a trigger rejects any <c>UPDATE</c> that changes its identity or content columns with
/// SQLSTATE <see cref="ViolationSqlState"/>. Only governance columns (status, attestations, audit fields) may change.
/// Their definition tables are insert-only (<c>InsertOnlySql</c>).
/// </summary>
internal static class ImmutableColumnsSql
{
    public const string FunctionName = "enforce_immutable_columns";

    /// <summary>SQLSTATE raised when a fixed column is changed.</summary>
    public const string ViolationSqlState = "KS001";

    public static string CreateFunction(string schema) => $$"""
        CREATE OR REPLACE FUNCTION {{SqlIdentifier.Quote(schema)}}.{{FunctionName}}() RETURNS trigger
        LANGUAGE plpgsql AS $fn$
        DECLARE
            col text;
        BEGIN
            FOREACH col IN ARRAY TG_ARGV LOOP
                IF (to_jsonb(OLD) -> col) IS DISTINCT FROM (to_jsonb(NEW) -> col) THEN
                    RAISE EXCEPTION 'K-STD: %.%.% is fixed once registered', TG_TABLE_SCHEMA, TG_TABLE_NAME, col
                        USING ERRCODE = '{{ViolationSqlState}}';
                END IF;
            END LOOP;
            RETURN NEW;
        END;
        $fn$;
        """;

    public static string DropFunction(string schema) =>
        $"DROP FUNCTION IF EXISTS {SqlIdentifier.Quote(schema)}.{FunctionName}();";

    public static string CreateGuard(string schema, string table, params string[] columns) => $"""
        CREATE TRIGGER {SqlIdentifier.Quote(TriggerName(table))} BEFORE UPDATE ON {SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(table)}
            FOR EACH ROW EXECUTE FUNCTION {SqlIdentifier.Quote(schema)}.{FunctionName}({string.Join(", ", columns.Select(c => $"'{c}'"))});
        """;

    public static string DropGuard(string schema, string table) =>
        $"DROP TRIGGER IF EXISTS {SqlIdentifier.Quote(TriggerName(table))} ON {SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(table)};";

    public static string TriggerName(string table) => SqlIdentifier.Truncate($"trg_{table}_immutable");
}

/// <summary>Migration helpers for <see cref="ImmutableColumnsSql"/>.</summary>
internal static class ImmutableColumnsMigrationBuilderExtensions
{
    public static void CreateImmutableColumnsFunction(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(ImmutableColumnsSql.CreateFunction(schema));

    public static void DropImmutableColumnsFunction(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(ImmutableColumnsSql.DropFunction(schema));

    public static void CreateImmutableColumnsGuard(this MigrationBuilder migrationBuilder, string schema, string table, params string[] columns) =>
        migrationBuilder.Sql(ImmutableColumnsSql.CreateGuard(schema, table, columns));

    public static void DropImmutableColumnsGuard(this MigrationBuilder migrationBuilder, string schema, string table) =>
        migrationBuilder.Sql(ImmutableColumnsSql.DropGuard(schema, table));
}
