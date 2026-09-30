using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Work-queue definitions (Canvas D10, ADR-2486): one row per version of a tenant's queue code, keyed by a capability.
/// The table is tenant-isolated (ADR-2250) and insert-only (<see cref="InsertOnlySql"/>), so a version never changes.
/// </summary>
public partial class WorkQueueDefinitions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "work_queue_definitions",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                capability = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                verb_class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_work_queue_definitions", x => x.id);
                table.UniqueConstraint("AK_work_queue_definitions_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_work_queue_definitions_version", "version > 0");
                table.ForeignKey(
                    name: "FK_work_queue_definitions_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_work_queue_definitions_tenant_id_code_version",
            schema: "kernel",
            table: "work_queue_definitions",
            columns: new[] { "tenant_id", "code", "version" },
            unique: true);

        migrationBuilder.EnableTenantIsolation("kernel", "work_queue_definitions");
        migrationBuilder.CreateInsertOnlyGuards("kernel", "work_queue_definitions");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", "work_queue_definitions");
        migrationBuilder.DisableTenantIsolation("kernel", "work_queue_definitions");

        migrationBuilder.DropTable(
            name: "work_queue_definitions",
            schema: "kernel");
    }
}
