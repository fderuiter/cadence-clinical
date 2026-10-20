using System;

using Cadence.Kernel.Modules;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Modules.Execution.Infrastructure.Migrations;

/// <summary>
/// Execution form instances (ADR-2622 §4.1) in the module's own <c>execution</c> schema, tenant-isolated like every kernel
/// table (ADR-2250, ADR-2600).
/// </summary>
public partial class FormInstances : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "execution");

        migrationBuilder.CreateTable(
            name: "form_instances",
            schema: "execution",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                protocol_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                collection_spec_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                planned_activity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                item_order = table.Column<string[]>(type: "text[]", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_form_instances", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_form_instances_tenant_enrollment",
            schema: "execution",
            table: "form_instances",
            columns: new[] { "tenant_id", "enrollment_id" });

        migrationBuilder.Sql(ModuleTenantIsolationSql.EnableForTenantColumn(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormInstancesTable));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "form_instances",
            schema: "execution");
    }
}
