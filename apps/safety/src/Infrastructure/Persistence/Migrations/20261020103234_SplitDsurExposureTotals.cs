using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Safety.Infrastructure.Persistence.Migrations;

/// <summary>
/// <c>periodic_safety_reports.TotalSubjectExposure</c> held patient-years rounded to an integer under a name that, in the
/// report payload, meant the subject count (#5573). The column becomes <c>TotalPatientYearsExposure</c>, a
/// <c>double precision</c> holding the unrounded patient-years; a stored value keeps its (rounded) number.
/// </summary>
public partial class SplitDsurExposureTotals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "TotalSubjectExposure",
            table: "periodic_safety_reports",
            newName: "TotalPatientYearsExposure");

        migrationBuilder.AlterColumn<double>(
            name: "TotalPatientYearsExposure",
            table: "periodic_safety_reports",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Rounds the patient-years back to the integer the former column held.
        migrationBuilder.Sql(
            "ALTER TABLE periodic_safety_reports ALTER COLUMN \"TotalPatientYearsExposure\" TYPE integer USING round(\"TotalPatientYearsExposure\")::integer;");

        migrationBuilder.RenameColumn(
            name: "TotalPatientYearsExposure",
            table: "periodic_safety_reports",
            newName: "TotalSubjectExposure");
    }
}
