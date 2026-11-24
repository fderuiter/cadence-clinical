using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PersistKernelEnrollmentIdOnClinicalSubjects : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "kernel_enrollment_id",
            table: "clinical_subjects",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_tenant_id_kernel_enrollment_id",
            table: "clinical_subjects",
            columns: ["tenant_id", "kernel_enrollment_id"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_clinical_subjects_tenant_id_kernel_enrollment_id",
            table: "clinical_subjects");

        migrationBuilder.DropColumn(
            name: "kernel_enrollment_id",
            table: "clinical_subjects");
    }
}
