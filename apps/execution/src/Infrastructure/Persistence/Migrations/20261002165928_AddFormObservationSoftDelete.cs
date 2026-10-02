using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddFormObservationSoftDelete : Migration
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
            name: "idx_observation_form_submission_active",
            table: "clinical_observations",
            columns: new[] { "form_submission_id", "is_active" });

        migrationBuilder.CreateIndex(
            name: "ix_clinical_observations_form_submission_id",
            table: "clinical_observations",
            column: "form_submission_id");

        migrationBuilder.CreateIndex(
            name: "ix_clinical_observations_is_active",
            table: "clinical_observations",
            column: "is_active");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "idx_observation_form_submission_active",
            table: "clinical_observations");

        migrationBuilder.DropIndex(
            name: "ix_clinical_observations_form_submission_id",
            table: "clinical_observations");

        migrationBuilder.DropIndex(
            name: "ix_clinical_observations_is_active",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "form_submission_id",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "is_active",
            table: "clinical_observations");
    }
}
