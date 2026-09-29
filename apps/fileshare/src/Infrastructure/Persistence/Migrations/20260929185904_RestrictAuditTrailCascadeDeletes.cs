using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Fileshare.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RestrictAuditTrailCascadeDeletes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_file_audit_logs_file_records_FileRecordId",
            table: "file_audit_logs");

        migrationBuilder.DropForeignKey(
            name: "FK_guest_links_file_records_FileRecordId",
            table: "guest_links");

        migrationBuilder.DropForeignKey(
            name: "FK_playback_audit_logs_file_records_FileRecordId",
            table: "playback_audit_logs");

        migrationBuilder.DropForeignKey(
            name: "FK_share_grants_file_records_FileRecordId",
            table: "share_grants");

        migrationBuilder.AddForeignKey(
            name: "FK_file_audit_logs_file_records_FileRecordId",
            table: "file_audit_logs",
            column: "FileRecordId",
            principalTable: "file_records",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_guest_links_file_records_FileRecordId",
            table: "guest_links",
            column: "FileRecordId",
            principalTable: "file_records",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_playback_audit_logs_file_records_FileRecordId",
            table: "playback_audit_logs",
            column: "FileRecordId",
            principalTable: "file_records",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_share_grants_file_records_FileRecordId",
            table: "share_grants",
            column: "FileRecordId",
            principalTable: "file_records",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_file_audit_logs_file_records_FileRecordId",
            table: "file_audit_logs");

        migrationBuilder.DropForeignKey(
            name: "FK_guest_links_file_records_FileRecordId",
            table: "guest_links");

        migrationBuilder.DropForeignKey(
            name: "FK_playback_audit_logs_file_records_FileRecordId",
            table: "playback_audit_logs");

        migrationBuilder.DropForeignKey(
            name: "FK_share_grants_file_records_FileRecordId",
            table: "share_grants");

        migrationBuilder.AddForeignKey(
            name: "FK_file_audit_logs_file_records_FileRecordId",
            table: "file_audit_logs",
            column: "FileRecordId",
            principalTable: "file_records",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_guest_links_file_records_FileRecordId",
            table: "guest_links",
            column: "FileRecordId",
            principalTable: "file_records",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_playback_audit_logs_file_records_FileRecordId",
            table: "playback_audit_logs",
            column: "FileRecordId",
            principalTable: "file_records",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_share_grants_file_records_FileRecordId",
            table: "share_grants",
            column: "FileRecordId",
            principalTable: "file_records",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
