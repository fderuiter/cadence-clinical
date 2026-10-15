using System;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(CtmsDbContext))]
[Migration("20261001180000_AddVisitLetterPdfAndDispatchFields")]
public partial class AddVisitLetterPdfAndDispatchFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DispatchedAt",
            table: "ctms_generated_letters",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EtmfDocumentId",
            table: "ctms_generated_letters",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsSigned",
            table: "ctms_generated_letters",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "PdfObjectKey",
            table: "ctms_generated_letters",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PdfSha256Hash",
            table: "ctms_generated_letters",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RecipientEmail",
            table: "ctms_generated_letters",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SignatureManifestation",
            table: "ctms_generated_letters",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "ctms_generated_letters",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "DRAFT");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DispatchedAt",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "EtmfDocumentId",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "IsSigned",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "PdfObjectKey",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "PdfSha256Hash",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "RecipientEmail",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "SignatureManifestation",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "Status",
            table: "ctms_generated_letters");
    }
}
