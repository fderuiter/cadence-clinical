using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Interop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "clinical_queries",
        "device_registrations",
        "ehr_endpoints",
        "ehr_reconciliation_candidates",
        "epro_defeated_submissions",
        "epro_quarantined_submissions",
        "epro_submissions",
        "instruments",
        "interop_audit_logs",
        "sce_biostat_export_audit_logs",
        "sce_database_lock_auth_tokens",
        "sce_execution_runs",
        "sce_statistical_programs",
        "sensor_daily_summaries",
        "sensor_safety_alerts",
        "sensor_telemetry_streams",
        "subject_assignments",
        "subject_notifications",
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "subject_notifications",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "subject_assignments",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "sensor_telemetry_streams",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "sensor_safety_alerts",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "sensor_daily_summaries",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "sce_statistical_programs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "sce_execution_runs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "sce_database_lock_auth_tokens",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "sce_biostat_export_audit_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "interop_audit_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "instruments",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "epro_submissions",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "epro_quarantined_submissions",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "epro_defeated_submissions",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ehr_reconciliation_candidates",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ehr_endpoints",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "device_registrations",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "clinical_queries",
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
            name: "IX_subject_notifications_tenant_id",
            table: "subject_notifications",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_subject_assignments_tenant_id",
            table: "subject_assignments",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_telemetry_streams_tenant_id",
            table: "sensor_telemetry_streams",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_safety_alerts_tenant_id",
            table: "sensor_safety_alerts",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_daily_summaries_tenant_id",
            table: "sensor_daily_summaries",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_sce_statistical_programs_tenant_id",
            table: "sce_statistical_programs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_sce_execution_runs_tenant_id",
            table: "sce_execution_runs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_sce_database_lock_auth_tokens_tenant_id",
            table: "sce_database_lock_auth_tokens",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_sce_biostat_export_audit_logs_tenant_id",
            table: "sce_biostat_export_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_interop_audit_logs_tenant_id",
            table: "interop_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_instruments_tenant_id",
            table: "instruments",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_epro_submissions_tenant_id",
            table: "epro_submissions",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_epro_quarantined_submissions_tenant_id",
            table: "epro_quarantined_submissions",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_epro_defeated_submissions_tenant_id",
            table: "epro_defeated_submissions",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_tenant_id",
            table: "ehr_reconciliation_candidates",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_endpoints_tenant_id",
            table: "ehr_endpoints",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_device_registrations_tenant_id",
            table: "device_registrations",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_clinical_queries_tenant_id",
            table: "clinical_queries",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
        migrationBuilder.EnableStandaloneTenantIsolation("interop", TenantTables);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("interop", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_subject_notifications_tenant_id",
            table: "subject_notifications");

        migrationBuilder.DropIndex(
            name: "IX_subject_assignments_tenant_id",
            table: "subject_assignments");

        migrationBuilder.DropIndex(
            name: "IX_sensor_telemetry_streams_tenant_id",
            table: "sensor_telemetry_streams");

        migrationBuilder.DropIndex(
            name: "IX_sensor_safety_alerts_tenant_id",
            table: "sensor_safety_alerts");

        migrationBuilder.DropIndex(
            name: "IX_sensor_daily_summaries_tenant_id",
            table: "sensor_daily_summaries");

        migrationBuilder.DropIndex(
            name: "IX_sce_statistical_programs_tenant_id",
            table: "sce_statistical_programs");

        migrationBuilder.DropIndex(
            name: "IX_sce_execution_runs_tenant_id",
            table: "sce_execution_runs");

        migrationBuilder.DropIndex(
            name: "IX_sce_database_lock_auth_tokens_tenant_id",
            table: "sce_database_lock_auth_tokens");

        migrationBuilder.DropIndex(
            name: "IX_sce_biostat_export_audit_logs_tenant_id",
            table: "sce_biostat_export_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_interop_audit_logs_tenant_id",
            table: "interop_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_instruments_tenant_id",
            table: "instruments");

        migrationBuilder.DropIndex(
            name: "IX_epro_submissions_tenant_id",
            table: "epro_submissions");

        migrationBuilder.DropIndex(
            name: "IX_epro_quarantined_submissions_tenant_id",
            table: "epro_quarantined_submissions");

        migrationBuilder.DropIndex(
            name: "IX_epro_defeated_submissions_tenant_id",
            table: "epro_defeated_submissions");

        migrationBuilder.DropIndex(
            name: "IX_ehr_reconciliation_candidates_tenant_id",
            table: "ehr_reconciliation_candidates");

        migrationBuilder.DropIndex(
            name: "IX_ehr_endpoints_tenant_id",
            table: "ehr_endpoints");

        migrationBuilder.DropIndex(
            name: "IX_device_registrations_tenant_id",
            table: "device_registrations");

        migrationBuilder.DropIndex(
            name: "IX_clinical_queries_tenant_id",
            table: "clinical_queries");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "subject_notifications");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "subject_assignments");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "sensor_telemetry_streams");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "sensor_safety_alerts");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "sensor_daily_summaries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "sce_statistical_programs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "sce_execution_runs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "sce_database_lock_auth_tokens");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "sce_biostat_export_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "interop_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "instruments");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "epro_submissions");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "epro_quarantined_submissions");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "epro_defeated_submissions");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ehr_reconciliation_candidates");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ehr_endpoints");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "device_registrations");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "clinical_queries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");
    }
}
