using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSubjectCompositeIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_StudyCode_SiteCode_Status_EnrollmentIndex",
            table: "clinical_subjects",
            columns: new[] { "StudyCode", "SiteCode", "Status", "EnrollmentIndex" });

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_StudyCode_Status_EnrollmentIndex",
            table: "clinical_subjects",
            columns: new[] { "StudyCode", "Status", "EnrollmentIndex" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_clinical_subjects_StudyCode_SiteCode_Status_EnrollmentIndex",
            table: "clinical_subjects");

        migrationBuilder.DropIndex(
            name: "IX_clinical_subjects_StudyCode_Status_EnrollmentIndex",
            table: "clinical_subjects");
    }
}
