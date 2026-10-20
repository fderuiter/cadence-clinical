using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Interop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddEhrEndpointSigningKeyRef : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SigningKeyId",
            table: "ehr_endpoints",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SigningKeyRef",
            table: "ehr_endpoints",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SigningKeyId",
            table: "ehr_endpoints");

        migrationBuilder.DropColumn(
            name: "SigningKeyRef",
            table: "ehr_endpoints");
    }
}
