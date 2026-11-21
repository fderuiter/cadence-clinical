using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Etmf.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "tmf_audit_ledger_seals",
        "tmf_audit_logs",
        "tmf_document_expiration_alert_states",
        "tmf_document_qc_transitions",
        "tmf_documents",
        "tmf_expected_documents",
        "tmf_export_jobs",
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "tmf_export_jobs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "tmf_expected_documents",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "tmf_documents",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "tmf_document_qc_transitions",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "tmf_document_expiration_alert_states",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "tmf_audit_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "tmf_audit_ledger_seals",
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
            name: "IX_tmf_export_jobs_tenant_id",
            table: "tmf_export_jobs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_expected_documents_tenant_id",
            table: "tmf_expected_documents",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_tenant_id",
            table: "tmf_documents",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_qc_transitions_tenant_id",
            table: "tmf_document_qc_transitions",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_expiration_alert_states_tenant_id",
            table: "tmf_document_expiration_alert_states",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_audit_logs_tenant_id",
            table: "tmf_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_audit_ledger_seals_tenant_id",
            table: "tmf_audit_ledger_seals",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
        migrationBuilder.EnableStandaloneTenantIsolation("etmf", TenantTables);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("etmf", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_tmf_export_jobs_tenant_id",
            table: "tmf_export_jobs");

        migrationBuilder.DropIndex(
            name: "IX_tmf_expected_documents_tenant_id",
            table: "tmf_expected_documents");

        migrationBuilder.DropIndex(
            name: "IX_tmf_documents_tenant_id",
            table: "tmf_documents");

        migrationBuilder.DropIndex(
            name: "IX_tmf_document_qc_transitions_tenant_id",
            table: "tmf_document_qc_transitions");

        migrationBuilder.DropIndex(
            name: "IX_tmf_document_expiration_alert_states_tenant_id",
            table: "tmf_document_expiration_alert_states");

        migrationBuilder.DropIndex(
            name: "IX_tmf_audit_logs_tenant_id",
            table: "tmf_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_tmf_audit_ledger_seals_tenant_id",
            table: "tmf_audit_ledger_seals");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "tmf_export_jobs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "tmf_expected_documents");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "tmf_documents");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "tmf_document_qc_transitions");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "tmf_document_expiration_alert_states");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "tmf_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "tmf_audit_ledger_seals");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");
    }
}
