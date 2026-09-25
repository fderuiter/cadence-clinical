using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SyncExecutionModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LabSource",
            table: "clinical_observations",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LabSiteId",
            table: "clinical_observations",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LabIndicator",
            table: "clinical_observations",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "LabOutOfRange",
            table: "clinical_observations",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MatchedNormalBounds",
            table: "clinical_observations",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RangeIndicator",
            table: "clinical_observations",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsOutOfRange",
            table: "clinical_observations",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "ReferenceRangeLow",
            table: "clinical_observations",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "ReferenceRangeHigh",
            table: "clinical_observations",
            type: "double precision",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LabSource",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "LabSiteId",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "LabIndicator",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "LabOutOfRange",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "MatchedNormalBounds",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "RangeIndicator",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "IsOutOfRange",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "ReferenceRangeLow",
            table: "clinical_observations");

        migrationBuilder.DropColumn(
            name: "ReferenceRangeHigh",
            table: "clinical_observations");
    }
}
