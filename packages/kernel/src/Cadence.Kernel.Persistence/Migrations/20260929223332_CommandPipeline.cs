using System;

using Cadence.Kernel.Persistence.Commands;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// The kernel command pipeline's write set (spec §5.1-5.3, ADR-2449): provenance activities, domain events, the
/// per-tenant audit hash chain and its heads, the transactional outbox and idempotency records. Audit, event,
/// provenance and idempotency tables are made insert-only by trigger. Replaces the per-entity
/// <c>audit_log_entries</c> table from the initial identity migration, which no deployment has written to.
/// </summary>
public partial class CommandPipeline : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries",
            schema: "kernel");

        migrationBuilder.CreateTable(
            name: "audit_chain_heads",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                last_sequence = table.Column<long>(type: "bigint", nullable: false),
                last_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_chain_heads", x => x.tenant_id);
            });

        migrationBuilder.CreateTable(
            name: "audit_entries",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sequence = table.Column<long>(type: "bigint", nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                actor_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                aggregate_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                aggregate_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                aggregate_version = table.Column<long>(type: "bigint", nullable: true),
                reason_for_change = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                changes = table.Column<string>(type: "text", nullable: false),
                predecessor_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_entries", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "domain_events",
            schema: "kernel",
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                occurred_valid = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                recorded = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                actor_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                causation_id = table.Column<Guid>(type: "uuid", nullable: true),
                payload = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_domain_events", x => x.event_id);
            });

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                command_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                result_json = table.Column<string>(type: "text", nullable: false),
                activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_idempotency_records", x => new { x.tenant_id, x.idempotency_key });
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                envelope = table.Column<string>(type: "jsonb", nullable: false),
                enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "provenance_activities",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                command_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                actor_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                actor_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                aggregate_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                aggregate_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                causation_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_provenance_activities", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_entries_activity_id",
            schema: "kernel",
            table: "audit_entries",
            column: "activity_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_entries_tenant_id_sequence",
            schema: "kernel",
            table: "audit_entries",
            columns: new[] { "tenant_id", "sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_domain_events_activity_id",
            schema: "kernel",
            table: "domain_events",
            column: "activity_id");

        migrationBuilder.CreateIndex(
            name: "IX_domain_events_tenant_id_recorded",
            schema: "kernel",
            table: "domain_events",
            columns: new[] { "tenant_id", "recorded" });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_enqueued_at",
            schema: "kernel",
            table: "outbox_messages",
            column: "enqueued_at",
            filter: "dispatched_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_event_id",
            schema: "kernel",
            table: "outbox_messages",
            column: "event_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_provenance_activities_correlation_id",
            schema: "kernel",
            table: "provenance_activities",
            column: "correlation_id");

        migrationBuilder.CreateIndex(
            name: "IX_provenance_activities_tenant_id_recorded_at",
            schema: "kernel",
            table: "provenance_activities",
            columns: new[] { "tenant_id", "recorded_at" });

        migrationBuilder.CreateInsertOnlyGuards("kernel");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel");

        migrationBuilder.DropTable(
            name: "audit_chain_heads",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "audit_entries",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "domain_events",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "idempotency_records",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "provenance_activities",
            schema: "kernel");

        migrationBuilder.CreateTable(
            name: "audit_log_entries",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                block_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                changes_json = table.Column<string>(type: "text", nullable: true),
                entity_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                entity_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                predecessor_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_log_entries", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_entity_name_timestamp",
            schema: "kernel",
            table: "audit_log_entries",
            columns: new[] { "entity_name", "timestamp" });
    }
}
