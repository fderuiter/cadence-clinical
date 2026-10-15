using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTripReportEntities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "QuestionKey",
            table: "ctms_monitoring_visit_findings",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SectionKey",
            table: "ctms_monitoring_visit_findings",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TripReportId",
            table: "ctms_monitoring_visit_findings",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ctms_trip_report_responses",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TripReportId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SectionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                QuestionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                QuestionText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ResponseType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                Commentary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                FindingId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_trip_report_responses", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_trip_report_sections",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TripReportId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SectionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                OrderIndex = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_trip_report_sections", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_trip_reports",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                VisitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CraId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VisitType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TemplateName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IsSigned = table.Column<bool>(type: "boolean", nullable: false),
                SignatureManifestation = table.Column<string>(type: "text", nullable: true),
                Signer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SigningTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SubmittedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                EtmfDocumentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                PdfObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                PdfSha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_trip_reports", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ctms_trip_report_responses_TripReportId",
            table: "ctms_trip_report_responses",
            column: "TripReportId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_trip_report_responses_TripReportId_SectionKey",
            table: "ctms_trip_report_responses",
            columns: new[] { "TripReportId", "SectionKey" });

        migrationBuilder.CreateIndex(
            name: "IX_ctms_trip_report_sections_TripReportId",
            table: "ctms_trip_report_sections",
            column: "TripReportId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_trip_report_sections_TripReportId_SectionKey",
            table: "ctms_trip_report_sections",
            columns: new[] { "TripReportId", "SectionKey" });

        migrationBuilder.CreateIndex(
            name: "IX_ctms_trip_reports_SiteId",
            table: "ctms_trip_reports",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_trip_reports_StudyId",
            table: "ctms_trip_reports",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_trip_reports_VisitId",
            table: "ctms_trip_reports",
            column: "VisitId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ctms_trip_report_responses");

        migrationBuilder.DropTable(
            name: "ctms_trip_report_sections");

        migrationBuilder.DropTable(
            name: "ctms_trip_reports");

        migrationBuilder.DropColumn(
            name: "QuestionKey",
            table: "ctms_monitoring_visit_findings");

        migrationBuilder.DropColumn(
            name: "SectionKey",
            table: "ctms_monitoring_visit_findings");

        migrationBuilder.DropColumn(
            name: "TripReportId",
            table: "ctms_monitoring_visit_findings");
    }
}
