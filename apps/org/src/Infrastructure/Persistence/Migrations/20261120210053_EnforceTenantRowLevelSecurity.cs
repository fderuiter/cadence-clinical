using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Org.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "delegations_of_authority",
        "org_audit_logs",
        "org_training_logs",
        "personnel_assignments",
    ];

protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "personnel_assignments",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "org_training_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "org_audit_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "delegations_of_authority",
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
            name: "IX_personnel_assignments_tenant_id",
            table: "personnel_assignments",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_org_training_logs_tenant_id",
            table: "org_training_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_org_audit_logs_tenant_id",
            table: "org_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_delegations_of_authority_tenant_id",
            table: "delegations_of_authority",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
            migrationBuilder.EnableStandaloneTenantIsolation("org", TenantTables);
}

protected override void Down(MigrationBuilder migrationBuilder)
    {
    migrationBuilder.DisableStandaloneTenantIsolation("org", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_personnel_assignments_tenant_id",
            table: "personnel_assignments");

        migrationBuilder.DropIndex(
            name: "IX_org_training_logs_tenant_id",
            table: "org_training_logs");

        migrationBuilder.DropIndex(
            name: "IX_org_audit_logs_tenant_id",
            table: "org_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_delegations_of_authority_tenant_id",
            table: "delegations_of_authority");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "personnel_assignments");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "org_training_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "org_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "delegations_of_authority");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");
    }
}
