using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Safety.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class WidenDsurSignatureManifest : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "EsignatureManifestId",
            table: "periodic_safety_reports",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(255)",
            oldMaxLength: 255,
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "EsignatureManifestId",
            table: "periodic_safety_reports",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);
    }
}
