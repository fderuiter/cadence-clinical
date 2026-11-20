using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Tickets.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "ticket_attachments",
        "ticket_audit_logs",
        "ticket_comments",
        "tickets",
    ];

protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "tickets",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ticket_comments",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ticket_audit_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ticket_attachments",
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
            name: "IX_tickets_tenant_id",
            table: "tickets",
            column: "tenant_id");

        migrationBuilder.AddCheckConstraint(
            name: "ck_tickets_status",
            table: "tickets",
            sql: "\"Status\" IN ('OPEN', 'IN_PROGRESS', 'WAITING_ON_SITE', 'WAITING_ON_SPONSOR', 'PENDING_REGULATORY_REVIEW', 'RESOLVED', 'CLOSED', 'REOPENED', 'CANCELLED')");

        migrationBuilder.CreateIndex(
            name: "IX_ticket_comments_tenant_id",
            table: "ticket_comments",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ticket_audit_logs_tenant_id",
            table: "ticket_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ticket_attachments_tenant_id",
            table: "ticket_attachments",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
            migrationBuilder.EnableStandaloneTenantIsolation("tickets", TenantTables);
}

protected override void Down(MigrationBuilder migrationBuilder)
    {
    migrationBuilder.DisableStandaloneTenantIsolation("tickets", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_tickets_tenant_id",
            table: "tickets");

        migrationBuilder.DropCheckConstraint(
            name: "ck_tickets_status",
            table: "tickets");

        migrationBuilder.DropIndex(
            name: "IX_ticket_comments_tenant_id",
            table: "ticket_comments");

        migrationBuilder.DropIndex(
            name: "IX_ticket_audit_logs_tenant_id",
            table: "ticket_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_ticket_attachments_tenant_id",
            table: "ticket_attachments");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "tickets");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ticket_comments");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ticket_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ticket_attachments");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");
    }
}
