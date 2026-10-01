using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Payments.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddParticipantCompensationTables : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "AutoDisbursementThreshold",
            table: "payments_participant_wallets",
            type: "numeric(18,4)",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "payments_participant_disbursements",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                WalletId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                DisbursementType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_participant_disbursements", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_participant_expense_claims",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                WalletId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ExpenseDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                MerchantName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                GrossAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                TaxDeduction = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                NetAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ReceiptImageName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReceiptOcrDataJson = table.Column<string>(type: "jsonb", nullable: true),
                ReviewedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ReviewNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_participant_expense_claims", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_participant_stipend_rules",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VisitCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                VisitName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProcedureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                RequiresVerification = table.Column<bool>(type: "boolean", nullable: false),
                Taxable = table.Column<bool>(type: "boolean", nullable: false),
                AntiCoercionCap = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                AutoDisbursementThreshold = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                IsMilestone = table.Column<bool>(type: "boolean", nullable: false),
                MilestoneCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_participant_stipend_rules", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_participant_transactions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                WalletId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TransactionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                BalanceAfter = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                ReferenceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_participant_transactions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_disbursements_ReferenceNumber",
            table: "payments_participant_disbursements",
            column: "ReferenceNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_disbursements_Status",
            table: "payments_participant_disbursements",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_disbursements_SubjectId",
            table: "payments_participant_disbursements",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_disbursements_WalletId",
            table: "payments_participant_disbursements",
            column: "WalletId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_expense_claims_SiteId",
            table: "payments_participant_expense_claims",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_expense_claims_Status",
            table: "payments_participant_expense_claims",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_expense_claims_StudyId",
            table: "payments_participant_expense_claims",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_expense_claims_SubjectId",
            table: "payments_participant_expense_claims",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_expense_claims_WalletId",
            table: "payments_participant_expense_claims",
            column: "WalletId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_stipend_rules_StudyId",
            table: "payments_participant_stipend_rules",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_stipend_rules_VisitCode",
            table: "payments_participant_stipend_rules",
            column: "VisitCode");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_transactions_ReferenceId",
            table: "payments_participant_transactions",
            column: "ReferenceId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_transactions_StudyId",
            table: "payments_participant_transactions",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_transactions_SubjectId",
            table: "payments_participant_transactions",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_transactions_TransactionType",
            table: "payments_participant_transactions",
            column: "TransactionType");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_transactions_WalletId",
            table: "payments_participant_transactions",
            column: "WalletId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "payments_participant_disbursements");

        migrationBuilder.DropTable(
            name: "payments_participant_expense_claims");

        migrationBuilder.DropTable(
            name: "payments_participant_stipend_rules");

        migrationBuilder.DropTable(
            name: "payments_participant_transactions");

        migrationBuilder.DropColumn(
            name: "AutoDisbursementThreshold",
            table: "payments_participant_wallets");
    }
}
