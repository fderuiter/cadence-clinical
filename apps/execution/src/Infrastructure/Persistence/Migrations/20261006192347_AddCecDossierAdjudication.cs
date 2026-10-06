using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCecDossierAdjudication : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DossierDigest",
            table: "cec_dossiers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsDeidentified",
            table: "cec_dossiers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "MedicalNarrative",
            table: "cec_dossiers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Reader1Id",
            table: "cec_dossiers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Reader1Name",
            table: "cec_dossiers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Reader2Id",
            table: "cec_dossiers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Reader2Name",
            table: "cec_dossiers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VerbatimDescription",
            table: "cec_dossiers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "diagnostic_attachments",
            table: "cec_dossiers",
            type: "jsonb",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "ecg_records",
            table: "cec_dossiers",
            type: "jsonb",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "lab_panels",
            table: "cec_dossiers",
            type: "jsonb",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "reader_1_assessment",
            table: "cec_dossiers",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "reader_2_assessment",
            table: "cec_dossiers",
            type: "jsonb",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DossierDigest",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "IsDeidentified",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "MedicalNarrative",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "Reader1Id",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "Reader1Name",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "Reader2Id",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "Reader2Name",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "VerbatimDescription",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "diagnostic_attachments",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "ecg_records",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "lab_panels",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "reader_1_assessment",
            table: "cec_dossiers");

        migrationBuilder.DropColumn(
            name: "reader_2_assessment",
            table: "cec_dossiers");
    }
}

