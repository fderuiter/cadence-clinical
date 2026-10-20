using System;

using Cadence.Kernel.Modules;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Modules.Intelligence.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class TaskQualifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "configuration_hash",
            schema: "intelligence",
            table: "agent_runs",
            type: "character varying(71)",
            maxLength: 71,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64);

        migrationBuilder.CreateTable(
            name: "task_qualifications",
            schema: "intelligence",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                task_key = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                task_version = table.Column<int>(type: "integer", nullable: false),
                configuration_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                risk_class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                evaluation_run_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                evaluation_set_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                evaluation_summary_json = table.Column<string>(type: "text", nullable: false),
                attestation_id = table.Column<Guid>(type: "uuid", nullable: false),
                qualified_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                qualified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                suspension_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_task_qualifications", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ux_task_qualifications_version",
            schema: "intelligence",
            table: "task_qualifications",
            columns: new[] { "tenant_id", "task_key", "task_version" },
            unique: true);

        // Tenant isolation (K-ID-2): forced Row-Level Security keyed on tenant_id, as on the module's other tables.
        migrationBuilder.Sql(ModuleTenantIsolationSql.EnableForTenantColumn(IntelligenceDbContext.Schema, IntelligenceDbContext.TaskQualificationsTable));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "task_qualifications",
            schema: "intelligence");

        migrationBuilder.AlterColumn<string>(
            name: "configuration_hash",
            schema: "intelligence",
            table: "agent_runs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(71)",
            oldMaxLength: 71);
    }
}
