using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SyncModelWithSnapshot : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_doa_delegation_records_site_staff_members_StaffUserId",
            table: "doa_delegation_records");

        migrationBuilder.DropTable(
            name: "ctms_studies");

        migrationBuilder.DropTable(
            name: "site_staff_members");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ctms_studies",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_studies", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "site_staff_members",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                Email = table.Column<string>(type: "text", nullable: false),
                FirstName = table.Column<string>(type: "text", nullable: false),
                GcpCertified = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                LastName = table.Column<string>(type: "text", nullable: false),
                LicenseNumber = table.Column<string>(type: "text", nullable: true),
                PrimaryRole = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                SiteId = table.Column<string>(type: "text", nullable: false),
                UserId = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_site_staff_members", x => x.Id);
                table.UniqueConstraint("AK_site_staff_members_UserId", x => x.UserId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ctms_studies_StudyId",
            table: "ctms_studies",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_site_staff_members_SiteId",
            table: "site_staff_members",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_site_staff_members_UserId",
            table: "site_staff_members",
            column: "UserId",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_doa_delegation_records_site_staff_members_StaffUserId",
            table: "doa_delegation_records",
            column: "StaffUserId",
            principalTable: "site_staff_members",
            principalColumn: "UserId",
            onDelete: ReferentialAction.Cascade);
    }
}
