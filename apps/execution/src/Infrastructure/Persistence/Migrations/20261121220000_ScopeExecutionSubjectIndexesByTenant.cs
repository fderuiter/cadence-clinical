using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ScopeExecutionSubjectIndexesByTenant : Migration
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
            name: "IX_subject_consents_tenant_id_SubjectCode",
            table: "subject_consents",
            columns: ["tenant_id", "SubjectCode"]);

        migrationBuilder.CreateIndex(
            name: "IX_site_compliance_caches_tenant_id_StudyCode_SiteCode_Milestone",
            table: "site_compliance_caches",
            columns: ["tenant_id", "StudyCode", "SiteCode", "Milestone"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_tenant_id_StudyCode_EnrollmentIndex",
            table: "clinical_subjects",
            columns: ["tenant_id", "StudyCode", "EnrollmentIndex"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_tenant_id_StudyCode_SiteCode_Status_EnrollmentIndex",
            table: "clinical_subjects",
            columns: ["tenant_id", "StudyCode", "SiteCode", "Status", "EnrollmentIndex"]);

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_tenant_id_SubjectCode",
            table: "clinical_subjects",
            columns: ["tenant_id", "SubjectCode"],
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_subject_consents_tenant_id_SubjectCode",
            table: "subject_consents");

        migrationBuilder.DropIndex(
            name: "IX_site_compliance_caches_tenant_id_StudyCode_SiteCode_Milestone",
            table: "site_compliance_caches");

        migrationBuilder.DropIndex(
            name: "IX_clinical_subjects_tenant_id_StudyCode_EnrollmentIndex",
            table: "clinical_subjects");

        migrationBuilder.DropIndex(
            name: "IX_clinical_subjects_tenant_id_StudyCode_SiteCode_Status_EnrollmentIndex",
            table: "clinical_subjects");

        migrationBuilder.DropIndex(
            name: "IX_clinical_subjects_tenant_id_SubjectCode",
            table: "clinical_subjects");

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_StudyCode_EnrollmentIndex",
            table: "clinical_subjects",
            columns: ["StudyCode", "EnrollmentIndex"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_SubjectCode",
            table: "clinical_subjects",
            column: "SubjectCode",
            unique: true);
    }
}
