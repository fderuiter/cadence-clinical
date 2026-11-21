using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Payments.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTenantRowLevelSecurity : Migration
{
    private static readonly string[] TenantTables =
    [
        "audit_log_entries",
        "payments_audit_logs",
        "payments_budget_contracts",
        "payments_erp_sync_logs",
        "payments_fee_schedules",
        "payments_holdback_reserves",
        "payments_invoice_line_items",
        "payments_participant_disbursements",
        "payments_participant_expense_claims",
        "payments_participant_stipend_rules",
        "payments_participant_transactions",
        "payments_participant_wallets",
        "payments_payable_allocations",
        "payments_payables",
        "payments_remittance_advices",
        "payments_self_billing_invoices",
        "payments_split_payees",
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_split_payees",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_self_billing_invoices",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_remittance_advices",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_payables",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_payable_allocations",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_participant_wallets",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_participant_transactions",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_participant_stipend_rules",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_participant_expense_claims",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_participant_disbursements",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_invoice_line_items",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_holdback_reserves",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_fee_schedules",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_erp_sync_logs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_budget_contracts",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<Guid>(
            name: "tenant_id",
            table: "payments_audit_logs",
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
            name: "IX_payments_split_payees_tenant_id",
            table: "payments_split_payees",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_payments_self_billing_invoices_tenant_id",
            table: "payments_self_billing_invoices",
            column: "tenant_id");

        migrationBuilder.AddCheckConstraint(
            name: "ck_payments_self_billing_invoices_status",
            table: "payments_self_billing_invoices",
            sql: "\"Status\" IN ('DRAFT', 'APPROVED', 'RECONCILED', 'CANCELLED')");

        migrationBuilder.CreateIndex(
            name: "IX_payments_remittance_advices_tenant_id",
            table: "payments_remittance_advices",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payables_tenant_id",
            table: "payments_payables",
            column: "tenant_id");

        migrationBuilder.AddCheckConstraint(
            name: "ck_payments_payables_status",
            table: "payments_payables",
            sql: "\"Status\" IN ('TRIGGERED', 'VERIFIED', 'INVOICED', 'CANCELLED')");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payable_allocations_tenant_id",
            table: "payments_payable_allocations",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_wallets_tenant_id",
            table: "payments_participant_wallets",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_transactions_tenant_id",
            table: "payments_participant_transactions",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_stipend_rules_tenant_id",
            table: "payments_participant_stipend_rules",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_expense_claims_tenant_id",
            table: "payments_participant_expense_claims",
            column: "tenant_id");

        migrationBuilder.AddCheckConstraint(
            name: "ck_payments_participant_expense_claims_status",
            table: "payments_participant_expense_claims",
            sql: "\"Status\" IN ('PENDING_REVIEW', 'APPROVED', 'REJECTED')");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_disbursements_tenant_id",
            table: "payments_participant_disbursements",
            column: "tenant_id");

        migrationBuilder.AddCheckConstraint(
            name: "ck_payments_participant_disbursements_status",
            table: "payments_participant_disbursements",
            sql: "\"Status\" IN ('DISPATCHED', 'SETTLED', 'FAILED', 'CANCELLED')");

        migrationBuilder.CreateIndex(
            name: "IX_payments_invoice_line_items_tenant_id",
            table: "payments_invoice_line_items",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_payments_holdback_reserves_tenant_id",
            table: "payments_holdback_reserves",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_payments_fee_schedules_tenant_id",
            table: "payments_fee_schedules",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_payments_erp_sync_logs_tenant_id",
            table: "payments_erp_sync_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_payments_budget_contracts_tenant_id",
            table: "payments_budget_contracts",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_payments_audit_logs_tenant_id",
            table: "payments_audit_logs",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries",
            column: "tenant_id");
        migrationBuilder.EnableStandaloneTenantIsolation("payments", TenantTables);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("payments", TenantTables);

        migrationBuilder.DropIndex(
            name: "IX_payments_split_payees_tenant_id",
            table: "payments_split_payees");

        migrationBuilder.DropIndex(
            name: "IX_payments_self_billing_invoices_tenant_id",
            table: "payments_self_billing_invoices");

        migrationBuilder.DropCheckConstraint(
            name: "ck_payments_self_billing_invoices_status",
            table: "payments_self_billing_invoices");

        migrationBuilder.DropIndex(
            name: "IX_payments_remittance_advices_tenant_id",
            table: "payments_remittance_advices");

        migrationBuilder.DropIndex(
            name: "IX_payments_payables_tenant_id",
            table: "payments_payables");

        migrationBuilder.DropCheckConstraint(
            name: "ck_payments_payables_status",
            table: "payments_payables");

        migrationBuilder.DropIndex(
            name: "IX_payments_payable_allocations_tenant_id",
            table: "payments_payable_allocations");

        migrationBuilder.DropIndex(
            name: "IX_payments_participant_wallets_tenant_id",
            table: "payments_participant_wallets");

        migrationBuilder.DropIndex(
            name: "IX_payments_participant_transactions_tenant_id",
            table: "payments_participant_transactions");

        migrationBuilder.DropIndex(
            name: "IX_payments_participant_stipend_rules_tenant_id",
            table: "payments_participant_stipend_rules");

        migrationBuilder.DropIndex(
            name: "IX_payments_participant_expense_claims_tenant_id",
            table: "payments_participant_expense_claims");

        migrationBuilder.DropCheckConstraint(
            name: "ck_payments_participant_expense_claims_status",
            table: "payments_participant_expense_claims");

        migrationBuilder.DropIndex(
            name: "IX_payments_participant_disbursements_tenant_id",
            table: "payments_participant_disbursements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_payments_participant_disbursements_status",
            table: "payments_participant_disbursements");

        migrationBuilder.DropIndex(
            name: "IX_payments_invoice_line_items_tenant_id",
            table: "payments_invoice_line_items");

        migrationBuilder.DropIndex(
            name: "IX_payments_holdback_reserves_tenant_id",
            table: "payments_holdback_reserves");

        migrationBuilder.DropIndex(
            name: "IX_payments_fee_schedules_tenant_id",
            table: "payments_fee_schedules");

        migrationBuilder.DropIndex(
            name: "IX_payments_erp_sync_logs_tenant_id",
            table: "payments_erp_sync_logs");

        migrationBuilder.DropIndex(
            name: "IX_payments_budget_contracts_tenant_id",
            table: "payments_budget_contracts");

        migrationBuilder.DropIndex(
            name: "IX_payments_audit_logs_tenant_id",
            table: "payments_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_tenant_id",
            table: "audit_log_entries");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_split_payees");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_self_billing_invoices");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_remittance_advices");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_payables");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_payable_allocations");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_participant_wallets");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_participant_transactions");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_participant_stipend_rules");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_participant_expense_claims");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_participant_disbursements");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_invoice_line_items");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_holdback_reserves");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_fee_schedules");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_erp_sync_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_budget_contracts");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "payments_audit_logs");

        migrationBuilder.DropColumn(
            name: "tenant_id",
            table: "audit_log_entries");
    }
}
