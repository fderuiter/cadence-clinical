using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddExecutionObservationsSlice : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "clinical_observations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                VisitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Domain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ObservationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                TestCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TestName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Value = table.Column<double>(type: "double precision", nullable: true),
                ValueString = table.Column<string>(type: "text", nullable: true),
                Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                NormalizedValue = table.Column<double>(type: "double precision", nullable: true),
                NormalizedUnit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                IsOutlier = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                IsSdvVerified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                SdvVerifiedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SdvVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                PageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                IsSdvFlagged = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                SdvFlagReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ProtocolVersionTag = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ProtocolVersionIndex = table.Column<int>(type: "integer", nullable: true),
                additional_properties = table.Column<string>(type: "jsonb", nullable: true),
                _version = table.Column<long>(type: "bigint", rowVersion: true, nullable: false, defaultValue: 0L),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
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
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VisitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FormId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ProtocolVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                payload = table.Column<string>(type: "jsonb", nullable: true),
                signature_manifest = table.Column<string>(type: "jsonb", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                IsReadonly = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                ClonedFromId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                _version = table.Column<long>(type: "bigint", rowVersion: true, nullable: false, defaultValue: 0L),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_form_submissions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "clinical_queries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VisitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Domain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                TestCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Response = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ObservationId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FieldLink = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Origin = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                RuleId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Responder = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Resolver = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                EscalatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                FormId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FieldId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                QueryType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ActionRequired = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                _version = table.Column<long>(type: "bigint", rowVersion: true, nullable: false, defaultValue: 0L),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_clinical_queries", x => x.Id);
            });

        // Observation indexes
        migrationBuilder.CreateIndex(
            name: "idx_observation_coords",
            table: "clinical_observations",
            columns: new[] { "StudyId", "SubjectId", "VisitId", "Domain", "TestCode" });

        migrationBuilder.CreateIndex(
            name: "ix_clinical_observations_site_id",
            table: "clinical_observations",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "idx_observation_subject_date",
            table: "clinical_observations",
            columns: new[] { "SubjectId", "ObservationDate" });

        // Form submission indexes
        migrationBuilder.CreateIndex(
            name: "idx_form_submissions_coords",
            table: "form_submissions",
            columns: new[] { "StudyId", "SubjectId", "VisitId", "FormId" });

        migrationBuilder.CreateIndex(
            name: "ix_form_submissions_site_id",
            table: "form_submissions",
            column: "SiteId");

        // Clinical query indexes
        migrationBuilder.CreateIndex(
            name: "idx_query_target",
            table: "clinical_queries",
            columns: new[] { "StudyId", "SubjectId", "VisitId", "Domain", "TestCode" });

        migrationBuilder.CreateIndex(
            name: "idx_query_subject_status",
            table: "clinical_queries",
            columns: new[] { "SubjectId", "Status" });

        migrationBuilder.CreateIndex(
            name: "idx_query_observation_id",
            table: "clinical_queries",
            column: "ObservationId");

        migrationBuilder.CreateIndex(
            name: "ix_clinical_queries_site_id",
            table: "clinical_queries",
            column: "SiteId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "clinical_queries");
        migrationBuilder.DropTable(name: "form_submissions");
        migrationBuilder.DropTable(name: "clinical_observations");
    }
}
