using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RetireFormSubmissionsAndObservations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "clinical_observations");

        migrationBuilder.DropTable(
            name: "form_submissions");

        migrationBuilder.DropTable(
            name: "published_form_version_registries");

        migrationBuilder.DropColumn(
            name: "form_submission_id",
            table: "staged_adverse_event_reviews");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "form_submission_id",
            table: "staged_adverse_event_reviews",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "clinical_observations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                additional_properties = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                Domain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                form_submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                IsOutOfRange = table.Column<bool>(type: "boolean", nullable: true),
                IsOutlier = table.Column<bool>(type: "boolean", nullable: false),
                IsSdvFlagged = table.Column<bool>(type: "boolean", nullable: false),
                IsSdvVerified = table.Column<bool>(type: "boolean", nullable: false),
                LabIndicator = table.Column<string>(type: "text", nullable: true),
                LabOutOfRange = table.Column<bool>(type: "boolean", nullable: true),
                LabSiteId = table.Column<string>(type: "text", nullable: true),
                LabSource = table.Column<string>(type: "text", nullable: true),
                MatchedNormalBounds = table.Column<string>(type: "text", nullable: true),
                NormalizedUnit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                NormalizedValue = table.Column<double>(type: "double precision", nullable: true),
                ObservationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ProtocolVersionIndex = table.Column<int>(type: "integer", nullable: true),
                ProtocolVersionTag = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                RangeIndicator = table.Column<string>(type: "text", nullable: true),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                ReferenceRangeHigh = table.Column<double>(type: "double precision", nullable: true),
                ReferenceRangeLow = table.Column<double>(type: "double precision", nullable: true),
                SdvFlagReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                SdvVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SdvVerifiedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TestCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TestName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Value = table.Column<double>(type: "double precision", nullable: true),
                ValueString = table.Column<string>(type: "text", nullable: true),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false),
                VisitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_clinical_observations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "form_submissions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClonedFromId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                FormId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                IsReadonly = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                payload = table.Column<string>(type: "jsonb", nullable: true),
                ProtocolVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                signature_manifest = table.Column<string>(type: "jsonb", nullable: true),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false),
                VisitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_form_submissions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "published_form_version_registries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DdfIr = table.Column<string>(type: "jsonb", nullable: false),
                FormHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FormId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                OpenRosaXml = table.Column<string>(type: "text", nullable: false),
                ProtocolVersion = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProtocolVersionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TenantId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_published_form_version_registries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "idx_observation_coords",
            table: "clinical_observations",
            columns: new[] { "StudyId", "SubjectId", "VisitId", "Domain", "TestCode" });

        migrationBuilder.CreateIndex(
            name: "idx_observation_form_submission_active",
            table: "clinical_observations",
            columns: new[] { "form_submission_id", "is_active" });

        migrationBuilder.CreateIndex(
            name: "idx_observation_subject_date",
            table: "clinical_observations",
            columns: new[] { "SubjectId", "ObservationDate" });

        migrationBuilder.CreateIndex(
            name: "ix_clinical_observations_form_submission_id",
            table: "clinical_observations",
            column: "form_submission_id");

        migrationBuilder.CreateIndex(
            name: "ix_clinical_observations_is_active",
            table: "clinical_observations",
            column: "is_active");

        migrationBuilder.CreateIndex(
            name: "ix_clinical_observations_site_id",
            table: "clinical_observations",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "idx_form_submissions_coords",
            table: "form_submissions",
            columns: new[] { "StudyId", "SubjectId", "VisitId", "FormId" });

        migrationBuilder.CreateIndex(
            name: "ix_form_submissions_site_id",
            table: "form_submissions",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_published_form_version_registries_StudyId_ProtocolVersion_F~",
            table: "published_form_version_registries",
            columns: new[] { "StudyId", "ProtocolVersion", "FormId" },
            unique: true);
    }
}
