using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Safety.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "device_safety_investigations",
        "periodic_safety_reports",
        "sae_discrepancies",
        "sae_reconciliation_jobs",
        "sae_reconciliation_runs",
        "safety_as2_transmission_audit_logs",
        "safety_as2_transmissions",
        "safety_audit_logs",
        "safety_cases",
        "safety_export_jobs",
        "safety_narratives",
        "safety_reporting_evaluations",
        "safety_signal_alerts",
        "safety_signal_detection_runs",
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "safety_signal_detection_runs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "safety_signal_alerts",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "safety_reporting_evaluations",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "safety_narratives",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "safety_export_jobs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "safety_cases",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "safety_audit_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "safety_as2_transmissions",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "safety_as2_transmission_audit_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "sae_reconciliation_runs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "sae_reconciliation_jobs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "sae_discrepancies",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "periodic_safety_reports",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "device_safety_investigations",
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
            name: "IX_safety_signal_detection_runs_tenant_id",
            table: "safety_signal_detection_runs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_safety_signal_alerts_tenant_id",
            table: "safety_signal_alerts",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_safety_reporting_evaluations_tenant_id",
            table: "safety_reporting_evaluations",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_safety_narratives_tenant_id",
            table: "safety_narratives",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_safety_export_jobs_tenant_id",
            table: "safety_export_jobs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_safety_cases_tenant_id",
            table: "safety_cases",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_safety_audit_logs_tenant_id",
            table: "safety_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmissions_tenant_id",
            table: "safety_as2_transmissions",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmission_audit_logs_tenant_id",
            table: "safety_as2_transmission_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_sae_reconciliation_runs_tenant_id",
            table: "sae_reconciliation_runs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_sae_reconciliation_jobs_tenant_id",
            table: "sae_reconciliation_jobs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_sae_discrepancies_tenant_id",
            table: "sae_discrepancies",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_periodic_safety_reports_tenant_id",
            table: "periodic_safety_reports",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_device_safety_investigations_tenant_id",
            table: "device_safety_investigations",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
        migrationBuilder.EnableStandaloneTenantIsolation("safety", TenantTables);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("safety", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_safety_signal_detection_runs_tenant_id",
            table: "safety_signal_detection_runs");

        migrationBuilder.DropIndex(
            name: "IX_safety_signal_alerts_tenant_id",
            table: "safety_signal_alerts");

        migrationBuilder.DropIndex(
            name: "IX_safety_reporting_evaluations_tenant_id",
            table: "safety_reporting_evaluations");

        migrationBuilder.DropIndex(
            name: "IX_safety_narratives_tenant_id",
            table: "safety_narratives");

        migrationBuilder.DropIndex(
            name: "IX_safety_export_jobs_tenant_id",
            table: "safety_export_jobs");

        migrationBuilder.DropIndex(
            name: "IX_safety_cases_tenant_id",
            table: "safety_cases");

        migrationBuilder.DropIndex(
            name: "IX_safety_audit_logs_tenant_id",
            table: "safety_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_safety_as2_transmissions_tenant_id",
            table: "safety_as2_transmissions");

        migrationBuilder.DropIndex(
            name: "IX_safety_as2_transmission_audit_logs_tenant_id",
            table: "safety_as2_transmission_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_sae_reconciliation_runs_tenant_id",
            table: "sae_reconciliation_runs");

        migrationBuilder.DropIndex(
            name: "IX_sae_reconciliation_jobs_tenant_id",
            table: "sae_reconciliation_jobs");

        migrationBuilder.DropIndex(
            name: "IX_sae_discrepancies_tenant_id",
            table: "sae_discrepancies");

        migrationBuilder.DropIndex(
            name: "IX_periodic_safety_reports_tenant_id",
            table: "periodic_safety_reports");

        migrationBuilder.DropIndex(
            name: "IX_device_safety_investigations_tenant_id",
            table: "device_safety_investigations");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "safety_signal_detection_runs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "safety_signal_alerts");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "safety_reporting_evaluations");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "safety_narratives");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "safety_export_jobs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "safety_cases");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "safety_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "safety_as2_transmissions");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "safety_as2_transmission_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "sae_reconciliation_runs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "sae_reconciliation_jobs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "sae_discrepancies");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "periodic_safety_reports");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "device_safety_investigations");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");
    }
}
