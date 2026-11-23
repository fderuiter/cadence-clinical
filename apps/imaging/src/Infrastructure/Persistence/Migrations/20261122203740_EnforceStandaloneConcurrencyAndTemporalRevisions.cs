using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Imaging.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "imaging_series_envelopes",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "imaging_instance_envelopes",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "imaging_deid_certificates",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "imaging_audit_logs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.EnableStandaloneInsertOnlyGuards("imaging", ["audit_log_entries", "imaging_audit_logs"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneInsertOnlyGuards("imaging", ["audit_log_entries", "imaging_audit_logs"]);

        migrationBuilder.DropColumn(
            name: "_version",
            table: "imaging_series_envelopes");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "imaging_instance_envelopes");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "imaging_deid_certificates");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "imaging_audit_logs");
    }
}
