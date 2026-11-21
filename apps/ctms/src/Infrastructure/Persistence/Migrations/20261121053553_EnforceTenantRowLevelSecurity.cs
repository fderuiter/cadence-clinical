using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "ctms_audit_logs",
        "ctms_budget_line_items",
        "ctms_clinical_queries",
        "ctms_cra_allocations",
        "ctms_defeated_monitoring_visits",
        "ctms_delegations",
        "ctms_generated_letters",
        "ctms_investigator_grants",
        "ctms_investigator_payables",
        "ctms_monitoring_visit_findings",
        "ctms_monitoring_visits",
        "ctms_payment_milestones",
        "ctms_recruitment_records",
        "ctms_regulatory_forms",
        "ctms_site_greenlight_gates",
        "ctms_site_milestones",
        "doa_delegation_records",
        "transactional_outbox",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "transactional_outbox",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "doa_delegation_records",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_site_milestones",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_site_greenlight_gates",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_regulatory_forms",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_recruitment_records",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_payment_milestones",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_monitoring_visits",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_monitoring_visit_findings",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_investigator_payables",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_investigator_grants",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_generated_letters",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_delegations",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_defeated_monitoring_visits",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_cra_allocations",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_clinical_queries",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_budget_line_items",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "ctms_audit_logs",
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
            name: "IX_transactional_outbox_tenant_id",
            table: "transactional_outbox",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_doa_delegation_records_tenant_id",
            table: "doa_delegation_records",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_site_milestones_tenant_id",
            table: "ctms_site_milestones",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_site_greenlight_gates_tenant_id",
            table: "ctms_site_greenlight_gates",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_regulatory_forms_tenant_id",
            table: "ctms_regulatory_forms",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_recruitment_records_tenant_id",
            table: "ctms_recruitment_records",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_payment_milestones_tenant_id",
            table: "ctms_payment_milestones",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_monitoring_visits_tenant_id",
            table: "ctms_monitoring_visits",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_monitoring_visit_findings_tenant_id",
            table: "ctms_monitoring_visit_findings",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_investigator_payables_tenant_id",
            table: "ctms_investigator_payables",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_investigator_grants_tenant_id",
            table: "ctms_investigator_grants",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_generated_letters_tenant_id",
            table: "ctms_generated_letters",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_delegations_tenant_id",
            table: "ctms_delegations",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_defeated_monitoring_visits_tenant_id",
            table: "ctms_defeated_monitoring_visits",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_cra_allocations_tenant_id",
            table: "ctms_cra_allocations",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_clinical_queries_tenant_id",
            table: "ctms_clinical_queries",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_budget_line_items_tenant_id",
            table: "ctms_budget_line_items",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_audit_logs_tenant_id",
            table: "ctms_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("ctms", TenantTables);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("ctms", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_transactional_outbox_tenant_id",
            table: "transactional_outbox");

        migrationBuilder.DropIndex(
            name: "IX_doa_delegation_records_tenant_id",
            table: "doa_delegation_records");

        migrationBuilder.DropIndex(
            name: "IX_ctms_site_milestones_tenant_id",
            table: "ctms_site_milestones");

        migrationBuilder.DropIndex(
            name: "IX_ctms_site_greenlight_gates_tenant_id",
            table: "ctms_site_greenlight_gates");

        migrationBuilder.DropIndex(
            name: "IX_ctms_regulatory_forms_tenant_id",
            table: "ctms_regulatory_forms");

        migrationBuilder.DropIndex(
            name: "IX_ctms_recruitment_records_tenant_id",
            table: "ctms_recruitment_records");

        migrationBuilder.DropIndex(
            name: "IX_ctms_payment_milestones_tenant_id",
            table: "ctms_payment_milestones");

        migrationBuilder.DropIndex(
            name: "IX_ctms_monitoring_visits_tenant_id",
            table: "ctms_monitoring_visits");

        migrationBuilder.DropIndex(
            name: "IX_ctms_monitoring_visit_findings_tenant_id",
            table: "ctms_monitoring_visit_findings");

        migrationBuilder.DropIndex(
            name: "IX_ctms_investigator_payables_tenant_id",
            table: "ctms_investigator_payables");

        migrationBuilder.DropIndex(
            name: "IX_ctms_investigator_grants_tenant_id",
            table: "ctms_investigator_grants");

        migrationBuilder.DropIndex(
            name: "IX_ctms_generated_letters_tenant_id",
            table: "ctms_generated_letters");

        migrationBuilder.DropIndex(
            name: "IX_ctms_delegations_tenant_id",
            table: "ctms_delegations");

        migrationBuilder.DropIndex(
            name: "IX_ctms_defeated_monitoring_visits_tenant_id",
            table: "ctms_defeated_monitoring_visits");

        migrationBuilder.DropIndex(
            name: "IX_ctms_cra_allocations_tenant_id",
            table: "ctms_cra_allocations");

        migrationBuilder.DropIndex(
            name: "IX_ctms_clinical_queries_tenant_id",
            table: "ctms_clinical_queries");

        migrationBuilder.DropIndex(
            name: "IX_ctms_budget_line_items_tenant_id",
            table: "ctms_budget_line_items");

        migrationBuilder.DropIndex(
            name: "IX_ctms_audit_logs_tenant_id",
            table: "ctms_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "transactional_outbox");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "doa_delegation_records");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_site_milestones");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_site_greenlight_gates");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_regulatory_forms");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_recruitment_records");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_payment_milestones");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_monitoring_visits");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_monitoring_visit_findings");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_investigator_payables");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_investigator_grants");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_delegations");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_defeated_monitoring_visits");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_cra_allocations");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_clinical_queries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_budget_line_items");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "ctms_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");
    }
}
