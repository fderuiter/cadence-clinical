using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Fileshare.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "file_audit_logs",
        "file_records",
        "guest_links",
        "playback_audit_logs",
        "share_grants",
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "share_grants",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "playback_audit_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "guest_links",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "file_records",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "file_audit_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "audit_log_entries",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.CreateIndex(
            name: "IX_share_grants_tenant_id",
            table: "share_grants",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_playback_audit_logs_tenant_id",
            table: "playback_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_guest_links_tenant_id",
            table: "guest_links",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_file_records_tenant_id",
            table: "file_records",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_file_audit_logs_tenant_id",
            table: "file_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
        migrationBuilder.EnableStandaloneTenantIsolation("fileshare", TenantTables);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("fileshare", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_share_grants_tenant_id",
            table: "share_grants");

        migrationBuilder.DropIndex(
            name: "IX_playback_audit_logs_tenant_id",
            table: "playback_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_guest_links_tenant_id",
            table: "guest_links");

        migrationBuilder.DropIndex(
            name: "IX_file_records_tenant_id",
            table: "file_records");

        migrationBuilder.DropIndex(
            name: "IX_file_audit_logs_tenant_id",
            table: "file_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "share_grants");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "playback_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "guest_links");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "file_records");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "file_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");
    }
}
