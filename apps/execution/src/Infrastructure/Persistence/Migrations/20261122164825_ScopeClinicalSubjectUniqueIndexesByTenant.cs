using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ScopeClinicalSubjectUniqueIndexesByTenant : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_clinical_subjects_StudyCode_EnrollmentIndex",
            table: "clinical_subjects");

        migrationBuilder.DropIndex(
            name: "IX_clinical_subjects_SubjectCode",
            table: "clinical_subjects");

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_tenant_id_StudyCode_EnrollmentIndex",
            table: "clinical_subjects",
            columns: new[] { "tenant_id", "StudyCode", "EnrollmentIndex" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_tenant_id_SubjectCode",
            table: "clinical_subjects",
            columns: new[] { "tenant_id", "SubjectCode" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_clinical_subjects_tenant_id_StudyCode_EnrollmentIndex",
            table: "clinical_subjects");

        migrationBuilder.DropIndex(
            name: "IX_clinical_subjects_tenant_id_SubjectCode",
            table: "clinical_subjects");

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_StudyCode_EnrollmentIndex",
            table: "clinical_subjects",
            columns: new[] { "StudyCode", "EnrollmentIndex" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_SubjectCode",
            table: "clinical_subjects",
            column: "SubjectCode",
            unique: true);
    }
}
