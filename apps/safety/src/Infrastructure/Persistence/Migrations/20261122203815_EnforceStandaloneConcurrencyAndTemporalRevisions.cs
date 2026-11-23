using System;

using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Safety.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "periodic_safety_report_revisions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ReportId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                ReportType = table.Column<string>(type: "text", nullable: false),
                StudyCode = table.Column<string>(type: "text", nullable: false),
                CompoundName = table.Column<string>(type: "text", nullable: false),
                PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TotalPatientYearsExposure = table.Column<double>(type: "double precision", nullable: false),
                TotalSaes = table.Column<int>(type: "integer", nullable: false),
                ReportPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                ApprovedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                EsignatureManifestId = table.Column<string>(type: "text", nullable: true),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RecordedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_periodic_safety_report_revisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_periodic_safety_report_revisions_periodic_safety_reports_Re~",
                    column: x => x.ReportId,
                    principalTable: "periodic_safety_reports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "safety_narrative_revisions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SafetyNarrativeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                SectionsJson = table.Column<string>(type: "jsonb", nullable: false),
                RawNarrativeText = table.Column<string>(type: "text", nullable: false),
                TimelineEventsJson = table.Column<string>(type: "jsonb", nullable: false),
                GroundedClaimsJson = table.Column<string>(type: "jsonb", nullable: false),
                ReviewStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ApprovedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                EsignatureManifestId = table.Column<string>(type: "text", nullable: true),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RecordedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_safety_narrative_revisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_safety_narrative_revisions_safety_narratives_SafetyNarrativ~",
                    column: x => x.SafetyNarrativeId,
                    principalTable: "safety_narratives",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_periodic_safety_report_revisions_ReportId_RevisionNumber",
            table: "periodic_safety_report_revisions",
            columns: new[] { "ReportId", "RevisionNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_periodic_safety_report_revisions_tenant_id",
            table: "periodic_safety_report_revisions",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_safety_narrative_revisions_SafetyNarrativeId_RevisionNumber",
            table: "safety_narrative_revisions",
            columns: new[] { "SafetyNarrativeId", "RevisionNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_safety_narrative_revisions_tenant_id",
            table: "safety_narrative_revisions",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("safety", ["periodic_safety_report_revisions", "safety_narrative_revisions"]);
        migrationBuilder.EnableStandaloneInsertOnlyGuards("safety", ["audit_log_entries", "periodic_safety_report_revisions", "safety_as2_transmission_audit_logs", "safety_audit_logs", "safety_narrative_revisions"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("safety", ["periodic_safety_report_revisions", "safety_narrative_revisions"]);
        migrationBuilder.DisableStandaloneInsertOnlyGuards("safety", ["audit_log_entries", "periodic_safety_report_revisions", "safety_as2_transmission_audit_logs", "safety_audit_logs", "safety_narrative_revisions"]);

        migrationBuilder.DropTable(
            name: "periodic_safety_report_revisions");

        migrationBuilder.DropTable(
            name: "safety_narrative_revisions");
    }
}
