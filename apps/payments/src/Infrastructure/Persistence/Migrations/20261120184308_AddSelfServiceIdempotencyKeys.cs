using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Payments.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSelfServiceIdempotencyKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "IdempotencyKey",
            table: "payments_participant_expense_claims",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "IdempotencyKey",
            table: "payments_participant_disbursements",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_payments_self_billing_invoices_status",
            table: "payments_self_billing_invoices",
            sql: "\"Status\" IN ('DRAFT', 'APPROVED', 'RECONCILED', 'CANCELLED')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_payments_payables_status",
            table: "payments_payables",
            sql: "\"Status\" IN ('TRIGGERED', 'VERIFIED', 'INVOICED', 'CANCELLED')");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_expense_claims_WalletId_IdempotencyKey",
            table: "payments_participant_expense_claims",
            columns: new[] { "WalletId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_payments_participant_expense_claims_status",
            table: "payments_participant_expense_claims",
            sql: "\"Status\" IN ('PENDING_REVIEW', 'APPROVED', 'REJECTED')");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_disbursements_WalletId_IdempotencyKey",
            table: "payments_participant_disbursements",
            columns: new[] { "WalletId", "IdempotencyKey" },
            unique: true);

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

        migrationBuilder.DropIndex(
            name: "IX_payments_participant_expense_claims_WalletId_IdempotencyKey",
            table: "payments_participant_expense_claims");

        migrationBuilder.DropCheckConstraint(
            name: "ck_payments_participant_expense_claims_status",
            table: "payments_participant_expense_claims");

        migrationBuilder.DropIndex(
            name: "IX_payments_participant_disbursements_WalletId_IdempotencyKey",
            table: "payments_participant_disbursements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_payments_participant_disbursements_status",
            table: "payments_participant_disbursements");

        migrationBuilder.DropColumn(
            name: "IdempotencyKey",
            table: "payments_participant_expense_claims");

        migrationBuilder.DropColumn(
            name: "IdempotencyKey",
            table: "payments_participant_disbursements");
    }
}
