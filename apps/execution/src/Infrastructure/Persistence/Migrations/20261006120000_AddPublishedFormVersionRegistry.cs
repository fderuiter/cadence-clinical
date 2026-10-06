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
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                study_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                protocol_version_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                protocol_version = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                form_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                form_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                open_rosa_xml = table.Column<string>(type: "text", nullable: false),
                ddf_ir = table.Column<string>(type: "jsonb", nullable: false),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_published_form_version_registries", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_published_form_version_registries_study_id_protocol_versio",
            table: "published_form_version_registries",
            columns: new[] { "study_id", "protocol_version", "form_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "published_form_version_registries");
    }
}
