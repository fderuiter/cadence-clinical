using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddStagedAdverseEventReview : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "staged_adverse_event_reviews",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                study_id = table.Column<string>(type: "text", nullable: false),
                site_id = table.Column<string>(type: "text", nullable: true),
                subject_id = table.Column<string>(type: "text", nullable: false),
                visit_id = table.Column<string>(type: "text", nullable: true),
                observation_id = table.Column<string>(type: "text", nullable: true),
                form_submission_id = table.Column<string>(type: "text", nullable: true),
                query_id = table.Column<string>(type: "text", nullable: true),
                test_code = table.Column<string>(type: "text", nullable: false),
                test_name = table.Column<string>(type: "text", nullable: false),
                lab_value = table.Column<double>(type: "double precision", nullable: false),
                unit = table.Column<string>(type: "text", nullable: false),
                ctcae_grade = table.Column<int>(type: "integer", nullable: false),
                grading_scale = table.Column<string>(type: "text", nullable: false),
                toxicity_term = table.Column<string>(type: "text", nullable: false),
                clinical_description = table.Column<string>(type: "text", nullable: false),
                is_hys_law_warning = table.Column<bool>(type: "boolean", nullable: false),
                is_delta_check_alert = table.Column<bool>(type: "boolean", nullable: false),
                delta_percentage = table.Column<double>(type: "double precision", nullable: true),
                review_status = table.Column<string>(type: "text", nullable: false),
                reviewed_by = table.Column<string>(type: "text", nullable: true),
                reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                investigator_notes = table.Column<string>(type: "text", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: false),
                reason_for_change = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_staged_adverse_event_reviews", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_staged_adverse_event_reviews_study_id_review_status",
            table: "staged_adverse_event_reviews",
            columns: new[] { "study_id", "review_status" });

        migrationBuilder.CreateIndex(
            name: "IX_staged_adverse_event_reviews_subject_id",
            table: "staged_adverse_event_reviews",
            column: "subject_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "staged_adverse_event_reviews");
    }
}
