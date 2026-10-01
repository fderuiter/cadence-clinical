using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SoftInactivateObservations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "form_submission_id",
            table: "clinical_observations",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "is_active",
            table: "clinical_observations",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.CreateIndex(
            name: "ix_clinical_observations_form_submission_id",
            table: "clinical_observations",
            column: "form_submission_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_clinical_observations_form_submission_id",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "form_submission_id",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "is_active",
            table: "clinical_observations");
    }
}
