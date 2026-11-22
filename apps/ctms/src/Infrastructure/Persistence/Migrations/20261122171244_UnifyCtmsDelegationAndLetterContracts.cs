using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class UnifyCtmsDelegationAndLetterContracts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ActualDate",
            table: "ctms_generated_letters",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FindingsJson",
            table: "ctms_generated_letters",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScheduledDate",
            table: "ctms_generated_letters",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SignerName",
            table: "ctms_generated_letters",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SigningPurpose",
            table: "ctms_generated_letters",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VisitType",
            table: "ctms_generated_letters",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "site_code",
            table: "ctms_generated_letters",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "study_code",
            table: "ctms_generated_letters",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Role",
            table: "ctms_delegations",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StaffName",
            table: "ctms_delegations",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "consumed_signature_tokens",
            columns: table => new
            {
                TokenId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consumed_signature_tokens", x => x.TokenId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ctms_generated_letters_site_code",
            table: "ctms_generated_letters",
            column: "site_code");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_generated_letters_study_code",
            table: "ctms_generated_letters",
            column: "study_code");

        migrationBuilder.CreateIndex(
            name: "IX_consumed_signature_tokens_RetainUntil",
            table: "consumed_signature_tokens",
            column: "RetainUntil");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "consumed_signature_tokens");

        migrationBuilder.DropIndex(
            name: "IX_ctms_generated_letters_site_code",
            table: "ctms_generated_letters");

        migrationBuilder.DropIndex(
            name: "IX_ctms_generated_letters_study_code",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "ActualDate",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "FindingsJson",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "ScheduledDate",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "SignerName",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "SigningPurpose",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "VisitType",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "site_code",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "study_code",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "Role",
            table: "ctms_delegations");

        migrationBuilder.DropColumn(
            name: "StaffName",
            table: "ctms_delegations");
    }
}
