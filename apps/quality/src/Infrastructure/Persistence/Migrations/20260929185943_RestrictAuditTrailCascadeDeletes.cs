using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Quality.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RestrictAuditTrailCascadeDeletes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_quality_capa_records_quality_deviations_DeviationId",
            table: "quality_capa_records");

        migrationBuilder.DropForeignKey(
            name: "FK_quality_root_cause_analyses_quality_deviations_DeviationId",
            table: "quality_root_cause_analyses");

        migrationBuilder.AddForeignKey(
            name: "FK_quality_capa_records_quality_deviations_DeviationId",
            table: "quality_capa_records",
            column: "DeviationId",
            principalTable: "quality_deviations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_quality_root_cause_analyses_quality_deviations_DeviationId",
            table: "quality_root_cause_analyses",
            column: "DeviationId",
            principalTable: "quality_deviations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_quality_capa_records_quality_deviations_DeviationId",
            table: "quality_capa_records");

        migrationBuilder.DropForeignKey(
            name: "FK_quality_root_cause_analyses_quality_deviations_DeviationId",
            table: "quality_root_cause_analyses");

        migrationBuilder.AddForeignKey(
            name: "FK_quality_capa_records_quality_deviations_DeviationId",
            table: "quality_capa_records",
            column: "DeviationId",
            principalTable: "quality_deviations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_quality_root_cause_analyses_quality_deviations_DeviationId",
            table: "quality_root_cause_analyses",
            column: "DeviationId",
            principalTable: "quality_deviations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
