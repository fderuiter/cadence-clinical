using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Interop.Infrastructure.Persistence.Migrations;

/// <summary>
/// #4770: device tokens are no longer stored. Each <c>DeviceToken</c> column (device registrations, telemetry streams,
/// daily summaries, safety alerts) is replaced by an indexed <c>DeviceTokenHash</c>: the keyed HMAC-SHA256 computed by
/// <c>DeviceTokenHasher</c>. No encrypted copy is kept because nothing needs the raw token back: a presented token is
/// hashed and compared.
/// <para>
/// Existing rows: the raw tokens are cleared, not transformed. The HMAC key is not available to SQL migrations (and
/// must never be embedded in one), and an unkeyed hash would be both brute-forceable and unmatchable by the
/// application. The clinical content of every row is kept; only the device linkage of pre-existing rows is lost, and
/// their <c>DeviceTokenHash</c> is empty until the device is re-registered. No code path wrote these tables before
/// this change (the service exposed only health endpoints). After applying, run <c>VACUUM FULL</c> on the four
/// tables to purge dead row versions; see the ADR.
/// </para>
/// @req:PRD-SYS-053
/// </summary>
public partial class HashDeviceTokensAtRest : Migration
{
    private static readonly string[] DeviceTokenTables =
    [
        "device_registrations",
        "sensor_telemetry_streams",
        "sensor_daily_summaries",
        "sensor_safety_alerts",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (string table in DeviceTokenTables)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceTokenHash",
                table: table,
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Clear: blank the raw token before dropping it, so no live row version still holds it.
            migrationBuilder.Sql($"UPDATE {table} SET \"DeviceToken\" = '' WHERE \"DeviceToken\" <> '';");

            migrationBuilder.DropIndex(
                name: $"IX_{table}_DeviceToken",
                table: table);

            migrationBuilder.DropColumn(
                name: "DeviceToken",
                table: table);

            migrationBuilder.CreateIndex(
                name: $"IX_{table}_DeviceTokenHash",
                table: table,
                column: "DeviceTokenHash");
        }
    }

    /// <inheritdoc />
    /// <remarks>Restores the old columns empty: the cleared tokens are not recoverable by design.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (string table in DeviceTokenTables)
        {
            migrationBuilder.DropIndex(
                name: $"IX_{table}_DeviceTokenHash",
                table: table);

            migrationBuilder.DropColumn(
                name: "DeviceTokenHash",
                table: table);

            migrationBuilder.AddColumn<string>(
                name: "DeviceToken",
                table: table,
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: $"IX_{table}_DeviceToken",
                table: table,
                column: "DeviceToken");
        }
    }
}
