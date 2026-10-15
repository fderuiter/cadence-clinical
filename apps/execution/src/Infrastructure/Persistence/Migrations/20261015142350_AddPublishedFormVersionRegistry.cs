using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddPublishedFormVersionRegistry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "published_form_version_registries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProtocolVersionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProtocolVersion = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FormId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FormHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                OpenRosaXml = table.Column<string>(type: "text", nullable: false),
                DdfIr = table.Column<string>(type: "jsonb", nullable: false),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_published_form_version_registries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_published_form_version_registries_StudyId_ProtocolVersion_F~",
            table: "published_form_version_registries",
            columns: new[] { "StudyId", "ProtocolVersion", "FormId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "published_form_version_registries");
    }
}
