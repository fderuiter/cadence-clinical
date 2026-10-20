using System;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Kernel.Persistence.Commands;

/// <summary>
/// DDL that makes the audit, event, provenance and idempotency tables insert-only in PostgreSQL itself (spec §5.3
/// item 2): a trigger rejects every <c>UPDATE</c>, <c>DELETE</c> and <c>TRUNCATE</c> with SQLSTATE
/// <see cref="ViolationSqlState"/>, whoever the database user is.
/// </summary>
internal static class InsertOnlySql
{
    /// <summary>Name of the shared trigger function, created once in the kernel schema.</summary>
    public const string FunctionName = "enforce_insert_only";

    /// <summary>SQLSTATE raised when an insert-only table is changed.</summary>
    public const string ViolationSqlState = "KA001";

    /// <summary>
    /// Every kernel table that is insert-only today. Migrations name their own tables explicitly, so adding one here
    /// never changes an applied migration; the schema test checks each table in this list carries the guards.
    /// </summary>
    public static readonly string[] Tables =
    [
        "audit_entries", "domain_events", "provenance_activities", "idempotency_records", "audit_chain_seals",
        "code_system_versions", "code_system_entries", "concept_revisions", "attestations", "lineage_nodes", "lineage_edges",

        // Evidence: an artifact version names its bytes by hash and never changes (spec §8.3).
        "artifact_versions",

        // Evidence: an observation revision is never overwritten; a correction appends the next one (K-FACT-1, ADR-2620).
        "observation_revisions",

        // Evidence: an assertion is never changed or removed; a later one retracts it, and a lock's release is one (ADR-2621).
        "assertions",

        // Evidence: an observation joins one form instance for one item and never leaves it (K-FACT-6, ADR-2622).
        "collection_instance_members",

        // Authority: a role never changes once defined, so grants under it keep their meaning (spec §9.3).
        "roles",

        // Work-queue definitions (ADR-2486): a version never changes; defining the code again adds the next one.
        "work_queue_definitions",

        // Standards Registry definitions: fixed once their standard version is registered (spec §6.3).
        "standard_datasets", "standard_variables", "standard_value_level_definitions", "standard_terminology",

        // Activity stream projection (ADR-2485): a projected event never changes.
        "activity_entries",

        // Validation runs: fixed once recorded; findings are derived from them (spec §10.3).
        "validation_runs", "validation_run_packs", "validation_run_datasets", "validation_run_inputs", "validation_detections",
    ];

    public static string CreateFunction(string schema) => $$"""
        CREATE OR REPLACE FUNCTION {{SqlIdentifier.Quote(schema)}}.{{FunctionName}}() RETURNS trigger
        LANGUAGE plpgsql AS $fn$
        BEGIN
            RAISE EXCEPTION 'K-AUDIT: %.% is insert-only; % is not allowed',
                TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = '{{ViolationSqlState}}';
        END;
        $fn$;
        """;

    public static string DropFunction(string schema) =>
        $"DROP FUNCTION IF EXISTS {SqlIdentifier.Quote(schema)}.{FunctionName}();";

    public static string CreateGuards(string schema, string table)
    {
        var qualified = $"{SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(table)}";
        return $"""
            CREATE TRIGGER {SqlIdentifier.Quote(RowTriggerName(table))} BEFORE UPDATE OR DELETE ON {qualified}
                FOR EACH ROW EXECUTE FUNCTION {SqlIdentifier.Quote(schema)}.{FunctionName}();
            CREATE TRIGGER {SqlIdentifier.Quote(TruncateTriggerName(table))} BEFORE TRUNCATE ON {qualified}
                FOR EACH STATEMENT EXECUTE FUNCTION {SqlIdentifier.Quote(schema)}.{FunctionName}();
            """;
    }

    public static string DropGuards(string schema, string table)
    {
        var qualified = $"{SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(table)}";
        return $"""
            DROP TRIGGER IF EXISTS {SqlIdentifier.Quote(TruncateTriggerName(table))} ON {qualified};
            DROP TRIGGER IF EXISTS {SqlIdentifier.Quote(RowTriggerName(table))} ON {qualified};
            """;
    }

    public static string RowTriggerName(string table) => SqlIdentifier.Truncate($"trg_{table}_insert_only");

    public static string TruncateTriggerName(string table) => SqlIdentifier.Truncate($"trg_{table}_no_truncate");


}

/// <summary>Migration helpers for <see cref="InsertOnlySql"/>.</summary>
internal static class InsertOnlyMigrationBuilderExtensions
{
    public static void CreateInsertOnlyFunction(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(InsertOnlySql.CreateFunction(schema));

    public static void DropInsertOnlyFunction(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(InsertOnlySql.DropFunction(schema));

    /// <summary>Guards the named tables. Migrations list their tables literally so they never change once applied.</summary>
    public static void CreateInsertOnlyGuards(this MigrationBuilder migrationBuilder, string schema, params string[] tables)
    {
        foreach (var table in tables)
        {
            migrationBuilder.Sql(InsertOnlySql.CreateGuards(schema, table));
        }
    }

    public static void DropInsertOnlyGuards(this MigrationBuilder migrationBuilder, string schema, params string[] tables)
    {
        foreach (var table in tables)
        {
            migrationBuilder.Sql(InsertOnlySql.DropGuards(schema, table));
        }
    }
}
