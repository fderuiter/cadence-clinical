using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Re-creates the reader-facing <c>kernel.activity_entries</c> projection (ADR-2485 §4.2, ADR-2659) with nullable
/// <c>reason_for_change</c>, <c>audit_entry_ref</c>, and <c>attestation_ref</c> columns joined on <c>ActivityId</c>
/// so <c>KernelActivityProjector</c> rebuilds it cleanly from <c>kernel.outbox_messages</c> while preserving
/// <see cref="InsertOnlySql"/> (<c>KA001</c>) and tenant RLS guards.
/// </summary>
public partial class EnrichActivityEntriesWithAuditAndAttestationRefs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", "activity_entries");
        migrationBuilder.DisableTenantIsolation("kernel", "activity_entries");

        migrationBuilder.DropTable(
            name: "activity_entries",
            schema: "kernel");

        migrationBuilder.CreateTable(
            name: "activity_entries",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                recorded = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                occurred_valid = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                actor_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                family = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                subject_kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                scope = table.Column<string[]>(type: "text[]", nullable: false),
                correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                audit_entry_ref = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                attestation_ref = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_activity_entries", x => new { x.tenant_id, x.event_id });
                table.ForeignKey(
                    name: "FK_activity_entries_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_activity_entries_scope",
            schema: "kernel",
            table: "activity_entries",
            column: "scope")
            .Annotation("Npgsql:IndexMethod", "gin");

        migrationBuilder.CreateIndex(
            name: "IX_activity_entries_tenant_id_recorded_event_id",
            schema: "kernel",
            table: "activity_entries",
            columns: new[] { "tenant_id", "recorded", "event_id" },
            descending: new[] { false, true, true });

        migrationBuilder.EnableTenantIsolation("kernel", "activity_entries");
        migrationBuilder.CreateInsertOnlyGuards("kernel", "activity_entries");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", "activity_entries");
        migrationBuilder.DisableTenantIsolation("kernel", "activity_entries");

        migrationBuilder.DropTable(
            name: "activity_entries",
            schema: "kernel");

        migrationBuilder.CreateTable(
            name: "activity_entries",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                recorded = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                occurred_valid = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                actor_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                family = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                subject_kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                scope = table.Column<string[]>(type: "text[]", nullable: false),
                correlation_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_activity_entries", x => new { x.tenant_id, x.event_id });
                table.ForeignKey(
                    name: "FK_activity_entries_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_activity_entries_scope",
            schema: "kernel",
            table: "activity_entries",
            column: "scope")
            .Annotation("Npgsql:IndexMethod", "gin");

        migrationBuilder.CreateIndex(
            name: "IX_activity_entries_tenant_id_recorded_event_id",
            schema: "kernel",
            table: "activity_entries",
            columns: new[] { "tenant_id", "recorded", "event_id" },
            descending: new[] { false, true, true });

        migrationBuilder.EnableTenantIsolation("kernel", "activity_entries");
        migrationBuilder.CreateInsertOnlyGuards("kernel", "activity_entries");
    }
}
