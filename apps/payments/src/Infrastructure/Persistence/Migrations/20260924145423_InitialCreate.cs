using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Payments.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS audit_log_entries (
                    ""Id"" uuid NOT NULL,
                    ""EntityName"" character varying(255) NOT NULL,
                    ""EntityId"" character varying(255) NOT NULL,
                    ""Action"" character varying(50) NOT NULL,
                    ""Timestamp"" timestamp with time zone NOT NULL,
                    ""UserId"" character varying(255) NOT NULL,
                    ""ReasonForChange"" character varying(1000) NOT NULL,
                    ""VersionIndex"" bigint NOT NULL,
                    ""ChangesJson"" text,
                    ""PredecessorHash"" character varying(64) NOT NULL,
                    ""BlockHash"" character varying(64) NOT NULL,
                    CONSTRAINT ""PK_audit_log_entries"" PRIMARY KEY (""Id"")
                );
                CREATE INDEX IF NOT EXISTS ""IX_audit_log_entries_EntityName_Timestamp"" ON audit_log_entries (""EntityName"", ""Timestamp"");
            ");

        migrationBuilder.CreateTable(
            name: "payments_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                EntityId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                PreviousStateJson = table.Column<string>(type: "jsonb", nullable: true),
                NewStateJson = table.Column<string>(type: "jsonb", nullable: true),
                Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UserRoles = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_budget_contracts",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ContractName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TierLevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                TotalBudget = table.Column<double>(type: "double precision", nullable: false),
                OverheadPercentage = table.Column<double>(type: "double precision", nullable: false),
                WithholdingPercentage = table.Column<double>(type: "double precision", nullable: false),
                EffectiveDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_budget_contracts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_erp_sync_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                InvoiceId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ErpSystem = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SyncDirection = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                ResponseDataJson = table.Column<string>(type: "jsonb", nullable: true),
                ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Reconciled = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_erp_sync_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_fee_schedules",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ContractId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                VisitCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ActivityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                FormId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ProcedureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ProcedureName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                BaseAmount = table.Column<double>(type: "double precision", nullable: false),
                OverheadPercentage = table.Column<double>(type: "double precision", nullable: false),
                WithholdingPercentage = table.Column<double>(type: "double precision", nullable: false),
                PatientStipend = table.Column<double>(type: "double precision", nullable: false),
                TravelReimbursement = table.Column<double>(type: "double precision", nullable: false),
                RequiresSdv = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_fee_schedules", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_holdback_reserves",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ContractId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                PayableId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                HoldbackAmount = table.Column<double>(type: "double precision", nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ReleaseAuthorizedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReleaseAuthorizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DmSignoffBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                DmSignoffAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                QualitySignoffBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                QualitySignoffAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ReleaseNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_holdback_reserves", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_invoice_line_items",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                InvoiceId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                PayableId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VisitCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ProcedureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ProcedureName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                BaseAmount = table.Column<double>(type: "double precision", nullable: false),
                OverheadAmount = table.Column<double>(type: "double precision", nullable: false),
                PatientStipend = table.Column<double>(type: "double precision", nullable: false),
                TravelReimbursement = table.Column<double>(type: "double precision", nullable: false),
                GrossAmount = table.Column<double>(type: "double precision", nullable: false),
                WithholdingRate = table.Column<double>(type: "double precision", nullable: false),
                WithholdingAmount = table.Column<double>(type: "double precision", nullable: false),
                NetAmount = table.Column<double>(type: "double precision", nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_invoice_line_items", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_participant_wallets",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CurrentBalance = table.Column<double>(type: "double precision", nullable: false),
                LifetimeStipends = table.Column<double>(type: "double precision", nullable: false),
                LifetimeReimbursements = table.Column<double>(type: "double precision", nullable: false),
                LifetimeDisbursements = table.Column<double>(type: "double precision", nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TaxableYtd = table.Column<double>(type: "double precision", nullable: false),
                Is1099ThresholdExceeded = table.Column<bool>(type: "boolean", nullable: false),
                W9Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                W9SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                W9VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                TaxIdLastFour = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                CardType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CardStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                VirtualCardToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                VirtualCardMask = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                VirtualCardExpiry = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                AntiCoercionCap = table.Column<double>(type: "double precision", nullable: true),
                AchRoutingNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                AchAccountMask = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                AchAccountHolder = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_participant_wallets", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_payable_allocations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                PayableId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                PayeeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                PayeeName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PayeeRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                AllocationPercentage = table.Column<double>(type: "double precision", nullable: false),
                GrossShare = table.Column<double>(type: "double precision", nullable: false),
                WithholdingShare = table.Column<double>(type: "double precision", nullable: false),
                NetShare = table.Column<double>(type: "double precision", nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_payable_allocations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_payables",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ContractId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VisitCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                FormId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ProcedureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ProcedureName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                TriggerType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TriggerEventId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TriggerSource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                BaseAmount = table.Column<double>(type: "double precision", nullable: false),
                OverheadAmount = table.Column<double>(type: "double precision", nullable: false),
                PatientStipendAmount = table.Column<double>(type: "double precision", nullable: false),
                TravelReimbursementAmount = table.Column<double>(type: "double precision", nullable: false),
                GrossAmount = table.Column<double>(type: "double precision", nullable: false),
                WithholdingRate = table.Column<double>(type: "double precision", nullable: false),
                WithholdingAmount = table.Column<double>(type: "double precision", nullable: false),
                NetAmount = table.Column<double>(type: "double precision", nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IsSdvGated = table.Column<bool>(type: "boolean", nullable: false),
                SdvVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SdvVerifiedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_payables", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_remittance_advices",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RemittanceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                InvoiceId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                GrossAmount = table.Column<double>(type: "double precision", nullable: false),
                OverheadAmount = table.Column<double>(type: "double precision", nullable: false),
                HoldbackAmount = table.Column<double>(type: "double precision", nullable: false),
                NetAmount = table.Column<double>(type: "double precision", nullable: false),
                PdfSha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PdfContent = table.Column<string>(type: "text", nullable: true),
                IsSealed = table.Column<bool>(type: "boolean", nullable: false),
                IsSigned = table.Column<bool>(type: "boolean", nullable: false),
                SignedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SignerRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                SignatureManifestJson = table.Column<string>(type: "jsonb", nullable: true),
                DispatchedTo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DispatchStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_remittance_advices", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_self_billing_invoices",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ContractId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                BillingCycle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PeriodStart = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PeriodEnd = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                TotalBaseAmount = table.Column<double>(type: "double precision", nullable: false),
                TotalOverheadAmount = table.Column<double>(type: "double precision", nullable: false),
                TotalPatientStipend = table.Column<double>(type: "double precision", nullable: false),
                TotalTravelReimbursement = table.Column<double>(type: "double precision", nullable: false),
                TotalGrossAmount = table.Column<double>(type: "double precision", nullable: false),
                TotalHoldbackAmount = table.Column<double>(type: "double precision", nullable: false),
                TotalNetAmount = table.Column<double>(type: "double precision", nullable: false),
                PayableCount = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ApprovedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ErpSystem = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ErpReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ErpSyncStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ErpSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ReconciledBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReconciledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ReconciliationReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReconciliationNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_self_billing_invoices", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments_split_payees",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ContractId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                PayeeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                PayeeName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PayeeRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                AllocationPercentage = table.Column<double>(type: "double precision", nullable: false),
                BankAccountReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                TaxIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments_split_payees", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_payments_audit_logs_Action",
            table: "payments_audit_logs",
            column: "Action");

        migrationBuilder.CreateIndex(
            name: "IX_payments_audit_logs_CreatedAt",
            table: "payments_audit_logs",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_payments_audit_logs_EntityId",
            table: "payments_audit_logs",
            column: "EntityId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_audit_logs_EntityType",
            table: "payments_audit_logs",
            column: "EntityType");

        migrationBuilder.CreateIndex(
            name: "IX_payments_audit_logs_UserId",
            table: "payments_audit_logs",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_budget_contracts_SiteId",
            table: "payments_budget_contracts",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_budget_contracts_Status",
            table: "payments_budget_contracts",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_payments_budget_contracts_StudyId",
            table: "payments_budget_contracts",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_erp_sync_logs_ErpSystem",
            table: "payments_erp_sync_logs",
            column: "ErpSystem");

        migrationBuilder.CreateIndex(
            name: "IX_payments_erp_sync_logs_ExternalReference",
            table: "payments_erp_sync_logs",
            column: "ExternalReference");

        migrationBuilder.CreateIndex(
            name: "IX_payments_erp_sync_logs_InvoiceId",
            table: "payments_erp_sync_logs",
            column: "InvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_erp_sync_logs_Status",
            table: "payments_erp_sync_logs",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_payments_fee_schedules_ContractId",
            table: "payments_fee_schedules",
            column: "ContractId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_fee_schedules_FormId",
            table: "payments_fee_schedules",
            column: "FormId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_fee_schedules_VisitCode",
            table: "payments_fee_schedules",
            column: "VisitCode");

        migrationBuilder.CreateIndex(
            name: "IX_payments_holdback_reserves_ContractId",
            table: "payments_holdback_reserves",
            column: "ContractId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_holdback_reserves_PayableId",
            table: "payments_holdback_reserves",
            column: "PayableId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payments_holdback_reserves_SiteId",
            table: "payments_holdback_reserves",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_holdback_reserves_Status",
            table: "payments_holdback_reserves",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_payments_holdback_reserves_StudyId",
            table: "payments_holdback_reserves",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_invoice_line_items_InvoiceId",
            table: "payments_invoice_line_items",
            column: "InvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_invoice_line_items_PayableId",
            table: "payments_invoice_line_items",
            column: "PayableId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_invoice_line_items_SubjectId",
            table: "payments_invoice_line_items",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_invoice_line_items_VisitCode",
            table: "payments_invoice_line_items",
            column: "VisitCode");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_wallets_SiteId",
            table: "payments_participant_wallets",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_wallets_Status",
            table: "payments_participant_wallets",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_wallets_StudyId",
            table: "payments_participant_wallets",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_wallets_SubjectId",
            table: "payments_participant_wallets",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_participant_wallets_W9Status",
            table: "payments_participant_wallets",
            column: "W9Status");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payable_allocations_PayableId",
            table: "payments_payable_allocations",
            column: "PayableId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payable_allocations_PayeeId",
            table: "payments_payable_allocations",
            column: "PayeeId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payables_ContractId",
            table: "payments_payables",
            column: "ContractId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payables_FormId",
            table: "payments_payables",
            column: "FormId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payables_IsSdvGated",
            table: "payments_payables",
            column: "IsSdvGated");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payables_SiteId",
            table: "payments_payables",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payables_Status",
            table: "payments_payables",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payables_StudyId",
            table: "payments_payables",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payables_SubjectId",
            table: "payments_payables",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payables_TriggerEventId",
            table: "payments_payables",
            column: "TriggerEventId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payments_payables_TriggerType",
            table: "payments_payables",
            column: "TriggerType");

        migrationBuilder.CreateIndex(
            name: "IX_payments_payables_VisitCode",
            table: "payments_payables",
            column: "VisitCode");

        migrationBuilder.CreateIndex(
            name: "IX_payments_remittance_advices_DispatchStatus",
            table: "payments_remittance_advices",
            column: "DispatchStatus");

        migrationBuilder.CreateIndex(
            name: "IX_payments_remittance_advices_InvoiceId",
            table: "payments_remittance_advices",
            column: "InvoiceId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payments_remittance_advices_RemittanceNumber",
            table: "payments_remittance_advices",
            column: "RemittanceNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payments_remittance_advices_SiteId",
            table: "payments_remittance_advices",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_remittance_advices_StudyId",
            table: "payments_remittance_advices",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_self_billing_invoices_BillingCycle",
            table: "payments_self_billing_invoices",
            column: "BillingCycle");

        migrationBuilder.CreateIndex(
            name: "IX_payments_self_billing_invoices_ContractId",
            table: "payments_self_billing_invoices",
            column: "ContractId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_self_billing_invoices_ErpReferenceId",
            table: "payments_self_billing_invoices",
            column: "ErpReferenceId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_self_billing_invoices_ErpSyncStatus",
            table: "payments_self_billing_invoices",
            column: "ErpSyncStatus");

        migrationBuilder.CreateIndex(
            name: "IX_payments_self_billing_invoices_InvoiceNumber",
            table: "payments_self_billing_invoices",
            column: "InvoiceNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payments_self_billing_invoices_SiteId",
            table: "payments_self_billing_invoices",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_self_billing_invoices_Status",
            table: "payments_self_billing_invoices",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_payments_self_billing_invoices_StudyId",
            table: "payments_self_billing_invoices",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_split_payees_ContractId",
            table: "payments_split_payees",
            column: "ContractId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "payments_audit_logs");

        migrationBuilder.DropTable(
            name: "payments_budget_contracts");

        migrationBuilder.DropTable(
            name: "payments_erp_sync_logs");

        migrationBuilder.DropTable(
            name: "payments_fee_schedules");

        migrationBuilder.DropTable(
            name: "payments_holdback_reserves");

        migrationBuilder.DropTable(
            name: "payments_invoice_line_items");

        migrationBuilder.DropTable(
            name: "payments_participant_wallets");

        migrationBuilder.DropTable(
            name: "payments_payable_allocations");

        migrationBuilder.DropTable(
            name: "payments_payables");

        migrationBuilder.DropTable(
            name: "payments_remittance_advices");

        migrationBuilder.DropTable(
            name: "payments_self_billing_invoices");

        migrationBuilder.DropTable(
            name: "payments_split_payees");
    }
}
