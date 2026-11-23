using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Payments.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "IdempotencyKey",
            table: "payments_participant_transactions",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_wallets_tenant_id_SubjectId_StudyId",
            table: "payments_participant_wallets",
            columns: new[] { "tenant_id", "SubjectId", "StudyId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_transactions_WalletId_IdempotencyKey",
            table: "payments_participant_transactions",
            columns: new[] { "WalletId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_transactions_WalletId_TransactionType_~",
            table: "payments_participant_transactions",
            columns: new[] { "WalletId", "TransactionType", "ReferenceId" },
            unique: true);

        migrationBuilder.EnableStandaloneInsertOnlyGuards("payments", ["audit_log_entries", "payments_audit_logs", "payments_participant_transactions"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneInsertOnlyGuards("payments", ["audit_log_entries", "payments_audit_logs", "payments_participant_transactions"]);

        migrationBuilder.DropIndex(
            name: "IX_payments_participant_wallets_tenant_id_SubjectId_StudyId",
            table: "payments_participant_wallets");

        migrationBuilder.DropIndex(
            name: "IX_payments_participant_transactions_WalletId_IdempotencyKey",
            table: "payments_participant_transactions");

        migrationBuilder.DropIndex(
            name: "IX_payments_participant_transactions_WalletId_TransactionType_~",
            table: "payments_participant_transactions");

        migrationBuilder.DropColumn(
            name: "IdempotencyKey",
            table: "payments_participant_transactions");
    }
}
