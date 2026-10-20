using System;

using Cadence.Kernel.Modules;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Modules.Intelligence.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialIntelligence : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "intelligence");

        migrationBuilder.CreateTable(
            name: "agent_controls",
            schema: "intelligence",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                scope = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                monthly_token_limit = table.Column<long>(type: "bigint", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_controls", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "agent_runs",
            schema: "intelligence",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                task_key = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                task_version = table.Column<int>(type: "integer", nullable: false),
                configuration_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                requested_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                request_json = table.Column<string>(type: "text", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                response_text = table.Column<string>(type: "text", nullable: true),
                failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                input_tokens = table.Column<long>(type: "bigint", nullable: false),
                output_tokens = table.Column<long>(type: "bigint", nullable: false),
                cost_usd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_runs", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ux_agent_controls_scope",
            schema: "intelligence",
            table: "agent_controls",
            columns: new[] { "tenant_id", "scope" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_agent_runs_requester",
            schema: "intelligence",
            table: "agent_runs",
            columns: new[] { "tenant_id", "requested_by", "started_at" });

        migrationBuilder.CreateIndex(
            name: "ix_agent_runs_task",
            schema: "intelligence",
            table: "agent_runs",
            columns: new[] { "tenant_id", "task_key", "started_at" });

        // Tenant isolation (K-ID-2): forced Row-Level Security keyed on tenant_id, as on kernel tables.
        migrationBuilder.Sql(ModuleTenantIsolationSql.EnableForTenantColumn(IntelligenceDbContext.Schema, IntelligenceDbContext.AgentRunsTable));
        migrationBuilder.Sql(ModuleTenantIsolationSql.EnableForTenantColumn(IntelligenceDbContext.Schema, IntelligenceDbContext.AgentControlsTable));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "agent_controls",
            schema: "intelligence");

        migrationBuilder.DropTable(
            name: "agent_runs",
            schema: "intelligence");
    }
}
