using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Econsent.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ConvertEconsentScoreToDecimal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "Score",
            table: "comprehension_results",
            type: "numeric(5,2)",
            precision: 5,
            scale: 2,
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<double>(
            name: "Score",
            table: "comprehension_results",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(5,2)",
            oldPrecision: 5,
            oldScale: 2);
    }
}
