using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Quality.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "quality_audit_logs",
        "quality_capa_records",
        "quality_deviations",
        "quality_root_cause_analyses",
    ];

protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "quality_root_cause_analyses",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "quality_deviations",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "quality_capa_records",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "quality_audit_logs",
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
            name: "IX_quality_root_cause_analyses_tenant_id",
            table: "quality_root_cause_analyses",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_quality_deviations_tenant_id",
            table: "quality_deviations",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_quality_capa_records_tenant_id",
            table: "quality_capa_records",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_quality_audit_logs_tenant_id",
            table: "quality_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
            migrationBuilder.EnableStandaloneTenantIsolation("quality", TenantTables);
}

protected override void Down(MigrationBuilder migrationBuilder)
    {
    migrationBuilder.DisableStandaloneTenantIsolation("quality", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_quality_root_cause_analyses_tenant_id",
            table: "quality_root_cause_analyses");

        migrationBuilder.DropIndex(
            name: "IX_quality_deviations_tenant_id",
            table: "quality_deviations");

        migrationBuilder.DropIndex(
            name: "IX_quality_capa_records_tenant_id",
            table: "quality_capa_records");

        migrationBuilder.DropIndex(
            name: "IX_quality_audit_logs_tenant_id",
            table: "quality_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "quality_root_cause_analyses");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "quality_deviations");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "quality_capa_records");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "quality_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");
    }
}
