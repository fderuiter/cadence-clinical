using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddRweEntities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "rwe_cohort_profiles",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CohortCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TherapeuticArea = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Indication = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                BaselineDemographicsJson = table.Column<string>(type: "text", nullable: false),
                TotalPatientCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_rwe_cohort_profiles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "rwe_patient_records",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CohortProfileId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                PatientIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Age = table.Column<double>(type: "double precision", nullable: false),
                Sex = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Bmi = table.Column<double>(type: "double precision", nullable: false),
                SystolicBp = table.Column<double>(type: "double precision", nullable: false),
                DiastolicBp = table.Column<double>(type: "double precision", nullable: false),
                BiomarkerValue = table.Column<double>(type: "double precision", nullable: false),
                ComorbiditiesJson = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_rwe_patient_records", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_rwe_cohort_profiles_CohortCode",
            table: "rwe_cohort_profiles",
            column: "CohortCode",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_rwe_patient_records_CohortProfileId",
            table: "rwe_patient_records",
            column: "CohortProfileId");

        migrationBuilder.CreateIndex(
            name: "IX_rwe_patient_records_CohortProfileId_PatientIdentifier",
            table: "rwe_patient_records",
            columns: new[] { "CohortProfileId", "PatientIdentifier" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "rwe_cohort_profiles");

        migrationBuilder.DropTable(
            name: "rwe_patient_records");
    }
}
