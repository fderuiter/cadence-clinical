using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Interop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "sensor_telemetry_streams",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "sensor_safety_alerts",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "sensor_daily_summaries",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "sce_biostat_export_audit_logs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "interop_audit_logs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "epro_quarantined_submissions",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "epro_defeated_submissions",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "clinical_queries",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.EnableStandaloneInsertOnlyGuards("interop", ["audit_log_entries", "interop_audit_logs", "sce_biostat_export_audit_logs"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneInsertOnlyGuards("interop", ["audit_log_entries", "interop_audit_logs", "sce_biostat_export_audit_logs"]);

        migrationBuilder.DropColumn(
            name: "_version",
            table: "sensor_telemetry_streams");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "sensor_safety_alerts");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "sensor_daily_summaries");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "sce_biostat_export_audit_logs");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "interop_audit_logs");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "epro_quarantined_submissions");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "epro_defeated_submissions");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "clinical_queries");
    }
}
