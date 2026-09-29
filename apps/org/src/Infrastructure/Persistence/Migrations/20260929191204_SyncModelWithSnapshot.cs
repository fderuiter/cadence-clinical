using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Org.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SyncModelWithSnapshot : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "organizations");

        migrationBuilder.DropTable(
            name: "personnel");

        migrationBuilder.DropTable(
            name: "sites");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "organizations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                OrgType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_organizations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "personnel",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                KeycloakUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                OrganizationId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_personnel", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "sites",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                OrganizationId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sites", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_organizations_OrgType",
            table: "organizations",
            column: "OrgType");

        migrationBuilder.CreateIndex(
            name: "IX_personnel_Email",
            table: "personnel",
            column: "Email");

        migrationBuilder.CreateIndex(
            name: "IX_personnel_KeycloakUserId",
            table: "personnel",
            column: "KeycloakUserId");

        migrationBuilder.CreateIndex(
            name: "IX_personnel_SiteId",
            table: "personnel",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_personnel_StudyId",
            table: "personnel",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_sites_SiteId",
            table: "sites",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_sites_StudyId",
            table: "sites",
            column: "StudyId");
    }
}
