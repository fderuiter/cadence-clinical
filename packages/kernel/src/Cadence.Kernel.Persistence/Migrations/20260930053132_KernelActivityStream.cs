using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// The activity stream projection (ADR-2485): <c>activity_entries</c>, tenant-isolated (ADR-2250) and insert-only
/// (<see cref="InsertOnlySql"/>), with a GIN index on the scope array so a study's activity is one index lookup.
/// </summary>
public partial class KernelActivityStream : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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

        // Named literally, never read from a shared list, so this migration cannot change once applied.
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
    }
}
