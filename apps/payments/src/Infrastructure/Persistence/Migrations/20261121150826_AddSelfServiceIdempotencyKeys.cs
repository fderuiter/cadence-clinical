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

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_expense_claims_WalletId_IdempotencyKey",
            table: "payments_participant_expense_claims",
            columns: new[] { "WalletId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_disbursements_WalletId_IdempotencyKey",
            table: "payments_participant_disbursements",
            columns: new[] { "WalletId", "IdempotencyKey" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_payments_participant_expense_claims_WalletId_IdempotencyKey",
            table: "payments_participant_expense_claims");

        migrationBuilder.DropIndex(
            name: "IX_payments_participant_disbursements_WalletId_IdempotencyKey",
            table: "payments_participant_disbursements");

        migrationBuilder.DropColumn(
            name: "IdempotencyKey",
            table: "payments_participant_expense_claims");

        migrationBuilder.DropColumn(
            name: "IdempotencyKey",
            table: "payments_participant_disbursements");
    }
}
