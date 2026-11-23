using System;

using Cadence.Kernel.Modules;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Modules.Execution.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddFormSdvAlerts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "form_sdv_alerts",
            schema: "execution",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_site_id = table.Column<Guid>(type: "uuid", nullable: true),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                form_id = table.Column<Guid>(type: "uuid", nullable: true),
                item_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                observation_id = table.Column<Guid>(type: "uuid", nullable: false),
                superseded_revision = table.Column<int>(type: "integer", nullable: false),
                new_revision = table.Column<int>(type: "integer", nullable: false),
                verified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                modified_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_form_sdv_alerts", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_form_sdv_alerts_tenant_enrollment_modified",
            schema: "execution",
            table: "form_sdv_alerts",
            columns: new[] { "tenant_id", "enrollment_id", "modified_at" });

        migrationBuilder.CreateIndex(
            name: "ix_form_sdv_alerts_tenant_study_modified",
            schema: "execution",
            table: "form_sdv_alerts",
            columns: new[] { "tenant_id", "study_id", "modified_at" });

        migrationBuilder.CreateIndex(
            name: "ux_form_sdv_alerts_tenant_observation_revision",
            schema: "execution",
            table: "form_sdv_alerts",
            columns: new[] { "tenant_id", "observation_id", "new_revision" },
            unique: true);

        migrationBuilder.Sql(ModuleInsertOnlySql.CreateFunction(ExecutionModuleDbContext.Schema));
        migrationBuilder.Sql(ModuleInsertOnlySql.CreateGuards(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormSdvAlertsTable));
        migrationBuilder.Sql(ModuleTenantIsolationSql.EnableForTenantColumn(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormSdvAlertsTable));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ModuleInsertOnlySql.DropGuards(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormSdvAlertsTable));

        migrationBuilder.DropTable(
            name: "form_sdv_alerts",
            schema: "execution");
    }
}
