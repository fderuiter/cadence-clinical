using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Modules.Intelligence.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AgentRunInferenceRoute : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "inference_locality",
            schema: "intelligence",
            table: "agent_runs",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "inference_policy",
            schema: "intelligence",
            table: "agent_runs",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "inference_residency",
            schema: "intelligence",
            table: "agent_runs",
            type: "character varying(8)",
            maxLength: 8,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "inference_supplier",
            schema: "intelligence",
            table: "agent_runs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "scrub_counts",
            schema: "intelligence",
            table: "agent_runs",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "inference_locality",
            schema: "intelligence",
            table: "agent_runs");

        migrationBuilder.DropColumn(
            name: "inference_policy",
            schema: "intelligence",
            table: "agent_runs");

        migrationBuilder.DropColumn(
            name: "inference_residency",
            schema: "intelligence",
            table: "agent_runs");

        migrationBuilder.DropColumn(
            name: "inference_supplier",
            schema: "intelligence",
            table: "agent_runs");

        migrationBuilder.DropColumn(
            name: "scrub_counts",
            schema: "intelligence",
            table: "agent_runs");
    }
}
