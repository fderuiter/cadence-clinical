using System;

using Cadence.Kernel.Modules;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Modules.Intelligence.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EvaluationRuns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "evaluation_run_hash",
            schema: "intelligence",
            table: "task_qualifications",
            type: "character varying(71)",
            maxLength: 71,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateTable(
            name: "evaluation_runs",
            schema: "intelligence",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                task_key = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                task_version = table.Column<int>(type: "integer", nullable: false),
                configuration_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                evaluation_set_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                evaluation_set_version = table.Column<int>(type: "integer", nullable: false),
                evaluation_set_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                accepted = table.Column<bool>(type: "boolean", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                record_json = table.Column<string>(type: "text", nullable: false),
                record_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                summary_json = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_evaluation_runs", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_evaluation_runs_task",
            schema: "intelligence",
            table: "evaluation_runs",
            columns: new[] { "tenant_id", "task_key", "task_version", "completed_at" });

        migrationBuilder.CreateIndex(
            name: "ux_evaluation_runs_run",
            schema: "intelligence",
            table: "evaluation_runs",
            columns: new[] { "tenant_id", "run_id" },
            unique: true);

        // Tenant isolation (K-ID-2): forced Row-Level Security keyed on tenant_id, as on the module's other tables.
        migrationBuilder.Sql(ModuleTenantIsolationSql.EnableForTenantColumn(IntelligenceDbContext.Schema, IntelligenceDbContext.EvaluationRunsTable));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "evaluation_runs",
            schema: "intelligence");

        migrationBuilder.DropColumn(
            name: "evaluation_run_hash",
            schema: "intelligence",
            table: "task_qualifications");
    }
}
