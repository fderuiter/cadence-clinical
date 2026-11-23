using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Notifications.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "notification_deliveries",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "_version",
            table: "notification_audit_logs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.EnableStandaloneInsertOnlyGuards("notifications", ["audit_log_entries", "notification_audit_logs"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneInsertOnlyGuards("notifications", ["audit_log_entries", "notification_audit_logs"]);

        migrationBuilder.DropColumn(
            name: "_version",
            table: "notification_deliveries");

        migrationBuilder.DropColumn(
            name: "_version",
            table: "notification_audit_logs");
    }
}
