using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Payments.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SyncPaymentsOutboxAndPayoutModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "ck_payments_self_billing_invoices_status",
            table: "payments_self_billing_invoices",
            sql: "\"Status\" IN ('DRAFT', 'APPROVED', 'RECONCILED', 'CANCELLED')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_payments_payables_status",
            table: "payments_payables",
            sql: "\"Status\" IN ('TRIGGERED', 'VERIFIED', 'INVOICED', 'CANCELLED')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_payments_participant_expense_claims_status",
            table: "payments_participant_expense_claims",
            sql: "\"Status\" IN ('PENDING_REVIEW', 'APPROVED', 'REJECTED')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_payments_participant_disbursements_status",
            table: "payments_participant_disbursements",
            sql: "\"Status\" IN ('DISPATCHED', 'SETTLED', 'FAILED', 'CANCELLED')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_payments_self_billing_invoices_status",
            table: "payments_self_billing_invoices");

        migrationBuilder.DropCheckConstraint(
            name: "ck_payments_payables_status",
            table: "payments_payables");

        migrationBuilder.DropCheckConstraint(
            name: "ck_payments_participant_expense_claims_status",
            table: "payments_participant_expense_claims");

        migrationBuilder.DropCheckConstraint(
            name: "ck_payments_participant_disbursements_status",
            table: "payments_participant_disbursements");
    }
}
