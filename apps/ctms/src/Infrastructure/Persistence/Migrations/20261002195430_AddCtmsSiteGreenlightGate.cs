using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCtmsSiteGreenlightGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ctms_site_greenlight_gates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SiteName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OverallStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReadinessPercentage = table.Column<int>(type: "integer", nullable: false),
                    FacetRegulatoryIrb = table.Column<bool>(type: "boolean", nullable: false),
                    FacetCtaBudget = table.Column<bool>(type: "boolean", nullable: false),
                    FacetEisfBaseline = table.Column<bool>(type: "boolean", nullable: false),
                    FacetDoaLog = table.Column<bool>(type: "boolean", nullable: false),
                    FacetStaffTraining = table.Column<bool>(type: "boolean", nullable: false),
                    FacetIpRelease = table.Column<bool>(type: "boolean", nullable: false),
                    GreenlightToken = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    GreenlightCertifiedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    GreenlightCertifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsOverridden = table.Column<bool>(type: "boolean", nullable: false),
                    OverrideSponsorUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OverrideMedicalUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OverrideJustification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OverrideAuthorizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UnfulfilledReasons = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    VersionIndex = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ctms_site_greenlight_gates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ctms_site_greenlight_gates_CountryCode",
                table: "ctms_site_greenlight_gates",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_ctms_site_greenlight_gates_SiteId",
                table: "ctms_site_greenlight_gates",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ctms_site_greenlight_gates_StudyId",
                table: "ctms_site_greenlight_gates",
                column: "StudyId");

            migrationBuilder.CreateIndex(
                name: "IX_ctms_site_greenlight_gates_StudyId_CountryCode",
                table: "ctms_site_greenlight_gates",
                columns: new[] { "StudyId", "CountryCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ctms_site_greenlight_gates");
        }
    }
