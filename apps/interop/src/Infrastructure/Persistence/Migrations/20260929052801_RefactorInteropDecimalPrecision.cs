using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Interop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RefactorInteropDecimalPrecision : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "ThresholdValue",
            table: "sensor_safety_alerts",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "ObservedValue",
            table: "sensor_safety_alerts",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TirPercentage",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "TbrLevel2Percentage",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "TbrLevel1Percentage",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "TarLevel2Percentage",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "TarLevel1Percentage",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "SleepEfficiencyPercent",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "SleepDurationMinutes",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "SedentaryMinutes",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "RestingHeartRateBpm",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "PeakExpiratoryFlowLMin",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "PeakCadenceSpm",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "MeanSystolicMmhg",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "MeanGlucoseMgDl",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "MeanDiastolicMmhg",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "MeanCadenceSpm",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "MeanArterialPressureMmhg",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "GlucoseSd",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "GlucoseManagementIndicator",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "GlucoseCvPercent",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "Fev1PercentPredicted",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "Fev1FvcRatio",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "BestFvcLiters",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "BestFev1Liters",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "ActiveMinutes",
            table: "sensor_daily_summaries",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<double>(
            name: "ThresholdValue",
            table: "sensor_safety_alerts",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<double>(
            name: "ObservedValue",
            table: "sensor_safety_alerts",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4);

        migrationBuilder.AlterColumn<double>(
            name: "TirPercentage",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "TbrLevel2Percentage",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "TbrLevel1Percentage",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "TarLevel2Percentage",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "TarLevel1Percentage",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "SleepEfficiencyPercent",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "SleepDurationMinutes",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "SedentaryMinutes",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "RestingHeartRateBpm",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "PeakExpiratoryFlowLMin",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "PeakCadenceSpm",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "MeanSystolicMmhg",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "MeanGlucoseMgDl",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "MeanDiastolicMmhg",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "MeanCadenceSpm",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "MeanArterialPressureMmhg",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "GlucoseSd",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "GlucoseManagementIndicator",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "GlucoseCvPercent",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "Fev1PercentPredicted",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "Fev1FvcRatio",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "BestFvcLiters",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "BestFev1Liters",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "ActiveMinutes",
            table: "sensor_daily_summaries",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4,
            oldNullable: true);
    }
}
