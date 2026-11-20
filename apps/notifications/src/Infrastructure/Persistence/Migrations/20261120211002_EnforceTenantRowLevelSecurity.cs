using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Notifications.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "notification_audit_logs",
        "notification_deliveries",
        "notification_records",
    ];

protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "notification_records",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "notification_deliveries",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "notification_audit_logs",
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
            name: "IX_notification_records_tenant_id",
            table: "notification_records",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_tenant_id",
            table: "notification_deliveries",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_notification_audit_logs_tenant_id",
            table: "notification_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
            migrationBuilder.EnableStandaloneTenantIsolation("notifications", TenantTables);
}

protected override void Down(MigrationBuilder migrationBuilder)
    {
    migrationBuilder.DisableStandaloneTenantIsolation("notifications", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_notification_records_tenant_id",
            table: "notification_records");

        migrationBuilder.DropIndex(
            name: "IX_notification_deliveries_tenant_id",
            table: "notification_deliveries");

        migrationBuilder.DropIndex(
            name: "IX_notification_audit_logs_tenant_id",
            table: "notification_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "notification_records");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "notification_deliveries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "notification_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");
    }
}
