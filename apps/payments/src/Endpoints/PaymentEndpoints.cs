using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Application;
using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Npgsql;

namespace Cadence.Apps.Payments.Endpoints;

public record DisburseStipendRequest(
    string SubjectId,
    string? StudyId,
    string? SiteId,
    int? SequenceNumber,
    string? AssignmentId,
    string? DiaryId,
    decimal? Amount,
    string? IdempotencyKey,
    string? DisbursementType
);

public record DisburseStipendResponse(
    bool Success,
    string Id,
    string WalletId,
    string SubjectId,
    decimal Amount,
    decimal NewBalance,
    string Currency,
    string Status,
    string ReferenceNumber,
    string IdempotencyKey,
    DateTimeOffset DispatchedAt
);

public record WalletResponse(
    string Id,
    string SubjectId,
    string StudyId,
    string SiteId,
    decimal CurrentBalance,
    decimal LifetimeStipends,
    decimal LifetimeReimbursements,
    decimal LifetimeDisbursements,
    decimal TaxableYtd,
    bool Is1099ThresholdExceeded,
    string W9Status,
    string Currency,
    string Status,
    string? VirtualCardMask,
    string? VirtualCardExpiry,
    string? AchRoutingNumber,
    string? AchAccountMask,
    string? AchAccountHolder,
    decimal? AntiCoercionCap,
    decimal? AutoDisbursementThreshold,
    DateTimeOffset UpdatedAt,
    string CardType = "RELOADABLE_DEBIT",
    string CardStatus = "ACTIVE",
    string? AchRoutingMask = null
);

public record VisitVerificationEventDto(
    string StudyId,
    string SiteId,
    string SubjectId,
    string VisitCode,
    string EventId
);

public record VisitVerificationResponseDto(
    string Status,
    decimal Amount,
    decimal CurrentBalance,
    string WalletId,
    string? TransactionId,
    string EventId,
    string SubjectId,
    string? Reason = null,
    bool RequiresReview = false,
    string? W9Status = null
);

public record ParticipantStipendRuleCreateDto(
    string StudyId,
    string VisitCode,
    string VisitName,
    string? ProcedureCode,
    decimal Amount,
    string? Currency = "USD",
    bool RequiresVerification = true,
    bool Taxable = false,
    decimal? AntiCoercionCap = null,
    decimal? AutoDisbursementThreshold = null,
    bool IsMilestone = false,
    string? MilestoneCode = null
);

public record SubmitW9RequestDto(
    string FullLegalName,
    string TaxIdLastFour,
    string? Address = null,
    bool CertifyAccurate = true
);

public record VerifyW9RequestDto(
    string? Notes = null
);

public record IssueCardRequestDto(
    string? CardType = "RELOADABLE_DEBIT"
);

public record DisburseFundsRequestDto(
    string DisbursementType,
    decimal Amount
);

public record SubmitExpenseClaimRequestDto(
    string SubjectId,
    string StudyId,
    string SiteId,
    string Category,
    string MerchantName,
    decimal GrossAmount,
    bool IsMealAllowance = false,
    decimal? PerDiemCap = null,
    string? ExpenseDate = null,
    string? ReceiptImageName = null,
    object? ReceiptOcrData = null,
    double? OcrConfidence = null,
    string? OcrToken = null,
    string? ReceiptImageBase64 = null
);

public record ReviewExpenseClaimRequestDto(
    string Status,
    string? ReviewNotes = null,
    decimal? AdjustedAmount = null
);

public record OcrExtractRequestDto(
    string? RawText = null,
    string? ImageBase64 = null,
    string? CategoryHint = null,
    string? Filename = null
);

public record ReceiptOcrExtractResponseDto(
    bool OcrReadable,
    string MerchantName,
    string ExpenseDate,
    decimal GrossAmount,
    decimal TaxDeduction,
    decimal NetAmount,
    string Category,
    double ConfidenceScore,
    string RawText,
    string? Warning = null,
    string? OcrToken = null,
    string? ReceiptSha256 = null
);

public record Tax1099StatusResponseDto(
    [property: JsonPropertyName("wallet_id")] string WalletId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("taxable_ytd")] decimal TaxableYtd,
    [property: JsonPropertyName("threshold_amount")] decimal ThresholdAmount,
    [property: JsonPropertyName("threshold_percentage")] decimal ThresholdPercentage,
    [property: JsonPropertyName("is_1099_threshold_exceeded")] bool Is1099ThresholdExceeded,
    [property: JsonPropertyName("w9_status")] string W9Status,
    [property: JsonPropertyName("warning_level")] string WarningLevel
);

public record WalletBalanceInquiryResponseDto(
    [property: JsonPropertyName("wallet_id")] string WalletId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("available_balance")] decimal AvailableBalance,
    [property: JsonPropertyName("pending_claims_total")] decimal PendingClaimsTotal,
    [property: JsonPropertyName("lifetime_stipends")] decimal LifetimeStipends,
    [property: JsonPropertyName("lifetime_reimbursements")] decimal LifetimeReimbursements,
    [property: JsonPropertyName("lifetime_disbursements")] decimal LifetimeDisbursements,
    [property: JsonPropertyName("taxable_ytd")] decimal TaxableYtd
);

public record SelfServiceWalletSummaryResponseDto(
    WalletResponse Wallet,
    List<ParticipantTransaction> RecentTransactions,
    List<ParticipantExpenseClaim> ExpenseClaims
);

public record SelfServiceSubmitExpenseRequestDto(
    string Category,
    string MerchantName,
    decimal GrossAmount,
    bool IsMealAllowance = false,
    decimal? PerDiemCap = null,
    string? ExpenseDate = null,
    string? ReceiptImageName = null,
    object? ReceiptOcrData = null,
    double? OcrConfidence = null,
    [property: JsonPropertyName("studyId")] string? StudyCode = null,
    [property: JsonPropertyName("siteId")] string? SiteCode = null,
    string? SubjectId = null,
    string? WalletId = null,
    string? IdempotencyKey = null,
    string? OcrToken = null,
    string? ReceiptImageBase64 = null
);

public record SelfServiceExpenseClaimResponseDto(
    string Id,
    string WalletId,
    string SubjectId,
    [property: JsonPropertyName("studyId")] string StudyCode,
    [property: JsonPropertyName("siteId")] string SiteCode,
    string ExpenseDate,
    string Category,
    string MerchantName,
    decimal GrossAmount,
    decimal TaxDeduction,
    decimal NetAmount,
    string Status,
    string ApprovalDecision,
    bool AutoApproved,
    string? DisbursementReference,
    string? ReviewNotes,
    string? ReceiptImageName,
    decimal WalletCurrentBalance,
    decimal LifetimeStipends,
    decimal LifetimeReimbursements,
    decimal LifetimeDisbursements,
    DateTimeOffset CreatedAt
);

public record SelfServiceDisburseRequestDto(
    string? DisbursementType = "DIRECT_ACH",
    decimal? Amount = null,
    string? WalletId = null,
    string? SubjectId = null,
    string? IdempotencyKey = null
);

public record SelfServiceDisburseResponseDto(
    string Id,
    string WalletId,
    string SubjectId,
    decimal Amount,
    decimal CurrentBalance,
    decimal LifetimeDisbursements,
    string Currency,
    string DisbursementType,
    string DestinationMask,
    string ReferenceNumber,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed class SelfServiceDiaryStipendRequestDto
{
    public string? DiaryId { get; set; }
    public string? AssignmentId { get; set; }
    public int? SequenceNumber { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? SyncVerificationToken { get; set; }
    [JsonPropertyName("studyId")]
    public string? StudyCode { get; set; }
    [JsonPropertyName("siteId")]
    public string? SiteCode { get; set; }
    public string? SubjectId { get; set; }
    public string? WalletId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public record SelfServiceDiaryStipendResponseDto(
    bool Success,
    string Status,
    decimal CreditedAmount,
    decimal CurrentBalance,
    decimal LifetimeStipends,
    string WalletId,
    string SubjectId,
    string Currency,
    string? TransactionId,
    string? ReferenceNumber,
    string IdempotencyKey,
    string? Reason = null,
    bool RequiresReview = false,
    string? W9Status = null
);

public record BillingCycleCompilationRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("billing_cycle")] string BillingCycle = "MONTHLY",
    [property: JsonPropertyName("period_start")] string? PeriodStart = null,
    [property: JsonPropertyName("period_end")] string? PeriodEnd = null,
    [property: JsonPropertyName("auto_approve")] bool AutoApprove = false
);

public record BatchBillingCycleCompilationRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("billing_cycle")] string BillingCycle = "MONTHLY",
    [property: JsonPropertyName("period_start")] string? PeriodStart = null,
    [property: JsonPropertyName("period_end")] string? PeriodEnd = null,
    [property: JsonPropertyName("auto_approve")] bool AutoApprove = false,
    [property: JsonPropertyName("site_ids")] List<string>? SiteIds = null
);

public record SiteBatchCompilationResult(
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("invoice_id")] string? InvoiceId,
    [property: JsonPropertyName("invoice_number")] string? InvoiceNumber,
    [property: JsonPropertyName("payable_count")] int PayableCount,
    [property: JsonPropertyName("total_gross_amount")] decimal TotalGrossAmount,
    [property: JsonPropertyName("total_holdback_amount")] decimal TotalHoldbackAmount,
    [property: JsonPropertyName("total_net_amount")] decimal TotalNetAmount,
    [property: JsonPropertyName("error_message")] string? ErrorMessage
);

public record PaymentAuditLogDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("entity_type")] string EntityType,
    [property: JsonPropertyName("entity_id")] string EntityId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("details")] string? Details,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy
);

public record BatchBillingCompilationResponse(
    [property: JsonPropertyName("batch_execution_id")] string BatchExecutionId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("billing_cycle")] string BillingCycle,
    [property: JsonPropertyName("period_start")] string PeriodStart,
    [property: JsonPropertyName("period_end")] string PeriodEnd,
    [property: JsonPropertyName("total_sites_processed")] int TotalSitesProcessed,
    [property: JsonPropertyName("successful_sites_count")] int SuccessfulSitesCount,
    [property: JsonPropertyName("failed_sites_count")] int FailedSitesCount,
    [property: JsonPropertyName("total_invoices_generated")] int TotalInvoicesGenerated,
    [property: JsonPropertyName("total_payables_compiled")] int TotalPayablesCompiled,
    [property: JsonPropertyName("total_gross_amount")] decimal TotalGrossAmount,
    [property: JsonPropertyName("total_holdback_amount")] decimal TotalHoldbackAmount,
    [property: JsonPropertyName("total_net_amount")] decimal TotalNetAmount,
    [property: JsonPropertyName("site_results")] List<SiteBatchCompilationResult> SiteResults,
    [property: JsonPropertyName("audit_logs")] List<PaymentAuditLogDto> AuditLogs
);

public record InvoiceLineItemDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("invoice_id")] string InvoiceId,
    [property: JsonPropertyName("payable_id")] string? PayableId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("visit_code")] string VisitCode,
    [property: JsonPropertyName("procedure_code")] string? ProcedureCode,
    [property: JsonPropertyName("procedure_name")] string? ProcedureName,
    [property: JsonPropertyName("base_amount")] decimal BaseAmount,
    [property: JsonPropertyName("overhead_amount")] decimal OverheadAmount,
    [property: JsonPropertyName("patient_stipend")] decimal PatientStipend,
    [property: JsonPropertyName("travel_reimbursement")] decimal TravelReimbursement,
    [property: JsonPropertyName("gross_amount")] decimal GrossAmount,
    [property: JsonPropertyName("withholding_rate")] decimal WithholdingRate,
    [property: JsonPropertyName("withholding_amount")] decimal WithholdingAmount,
    [property: JsonPropertyName("net_amount")] decimal NetAmount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("version_index")] int VersionIndex
);

public record RemittanceAdviceDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("remittance_number")] string RemittanceNumber,
    [property: JsonPropertyName("invoice_id")] string InvoiceId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("gross_amount")] decimal GrossAmount,
    [property: JsonPropertyName("overhead_amount")] decimal OverheadAmount,
    [property: JsonPropertyName("holdback_amount")] decimal HoldbackAmount,
    [property: JsonPropertyName("net_amount")] decimal NetAmount,
    [property: JsonPropertyName("pdf_sha256_hash")] string PdfSha256Hash,
    [property: JsonPropertyName("is_sealed")] bool IsSealed,
    [property: JsonPropertyName("is_signed")] bool IsSigned,
    [property: JsonPropertyName("signed_by")] string? SignedBy,
    [property: JsonPropertyName("signed_at")] DateTimeOffset? SignedAt,
    [property: JsonPropertyName("signer_role")] string? SignerRole,
    [property: JsonPropertyName("dispatch_status")] string DispatchStatus,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("version_index")] int VersionIndex
);

public record SelfBillingInvoiceResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("invoice_number")] string InvoiceNumber,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("contract_id")] string ContractId,
    [property: JsonPropertyName("billing_cycle")] string BillingCycle,
    [property: JsonPropertyName("period_start")] string PeriodStart,
    [property: JsonPropertyName("period_end")] string PeriodEnd,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("total_base_amount")] decimal TotalBaseAmount,
    [property: JsonPropertyName("total_overhead_amount")] decimal TotalOverheadAmount,
    [property: JsonPropertyName("total_patient_stipend")] decimal TotalPatientStipend,
    [property: JsonPropertyName("total_travel_reimbursement")] decimal TotalTravelReimbursement,
    [property: JsonPropertyName("total_gross_amount")] decimal TotalGrossAmount,
    [property: JsonPropertyName("total_holdback_amount")] decimal TotalHoldbackAmount,
    [property: JsonPropertyName("total_net_amount")] decimal TotalNetAmount,
    [property: JsonPropertyName("payable_count")] int PayableCount,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("approved_by")] string? ApprovedBy,
    [property: JsonPropertyName("approved_at")] DateTimeOffset? ApprovedAt,
    [property: JsonPropertyName("erp_system")] string? ErpSystem,
    [property: JsonPropertyName("erp_reference_id")] string? ErpReferenceId,
    [property: JsonPropertyName("erp_sync_status")] string ErpSyncStatus,
    [property: JsonPropertyName("erp_synced_at")] DateTimeOffset? ErpSyncedAt,
    [property: JsonPropertyName("reconciled_by")] string? ReconciledBy,
    [property: JsonPropertyName("reconciled_at")] DateTimeOffset? ReconciledAt,
    [property: JsonPropertyName("reconciliation_reference")] string? ReconciliationReference,
    [property: JsonPropertyName("reconciliation_notes")] string? ReconciliationNotes,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("line_items")] List<InvoiceLineItemDto> LineItems,
    [property: JsonPropertyName("remittance_advice")] RemittanceAdviceDto? RemittanceAdvice
);

/// <summary>
/// A remittance signature. The signer is the signed-in caller; the body carries only the reason (#5361).
/// </summary>
public record SignRemittanceRequest(
    [property: JsonPropertyName("signature_reason")] string? SignatureReason = null
);

public record ReconcileInvoiceRequest(
    [property: JsonPropertyName("payment_reference")] string PaymentReference,
    [property: JsonPropertyName("erp_system")] string? ErpSystem = "SAP",
    [property: JsonPropertyName("amount_paid")] decimal? AmountPaid = null,
    [property: JsonPropertyName("payment_date")] string? PaymentDate = null,
    [property: JsonPropertyName("notes")] string? Notes = null
);

public static class PaymentEndpoints
{
    /// <summary>
    /// The identity recorded for rows the service writes on its own, such as the demonstration stipend rules seeded on
    /// first read.
    /// </summary>
    public const string SeedSystemUser = "svc:payments";

    /// <summary>
    /// The identity recorded for the expense auto-approval policy, which approves and pays a low-risk claim with no
    /// person reviewing it.
    /// </summary>
    public const string AutoApprovalSystemUser = "svc:system_auto_approval";

    // PaymentAuditLog.UserRoles holds at most 255 characters.
    private const int MaxRolesLength = 255;

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder routes)
    {
        // Every route names the permission it needs (ADR-2516, #5361); PermissionMatrix.Payments.cs says which roles
        // hold each one. Handlers that write record the signed caller (CallerScope), never an identity from the body.
        var group = routes.MapGroup("/api/v1/payments")
            .WithTags("Payments");

        // Legacy / Direct endpoints
        group.MapPost("/disburse", DisburseStipendDirect)
            .WithName("DisburseStipendDirect")
            .RequirePermission("payments_stipend:disburse").RequireCallerStudyScope()
            .WithSummary("Direct disburse ePRO stipend to participant wallet with strict idempotency and audit trail.");

        group.MapPost("/participant/wallets/disburse", DisburseStipendDirect)
            .WithName("DisburseStipendWallet")
            .RequirePermission("payments_stipend:disburse").RequireCallerStudyScope()
            .WithSummary("Disburse stipend to participant wallet.");

        // Participant Wallets Directory & Management
        group.MapGet("/participant/wallets", ListWallets)
            .WithName("ListWallets")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("List all participant wallets.");

        group.MapGet("/participant/wallets/subject/{subjectId}", GetSubjectWallet)
            .WithName("GetSubjectWallet")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("Retrieve participant wallet balance and details.");

        group.MapGet("/participant/wallets/{walletId}", GetWalletById)
            .WithName("GetWalletById")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("Retrieve participant wallet details by wallet ID.");

        group.MapGet("/participant/wallets/{walletId}/tax-1099-status", GetTax1099Status)
            .WithName("GetTax1099Status")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("Retrieve IRS 1099 tax status and threshold monitoring tracer.");

        group.MapGet("/participant/wallets/{walletId}/balance", GetBalanceInquiry)
            .WithName("GetBalanceInquiry")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("Real-time balance inquiry for participant wallet.");

        group.MapPost("/participant/wallets/{walletId}/issue-card", IssueReloadableCard)
            .WithName("IssueReloadableCard")
            .RequirePermission("payments_wallet:issue_card").RequireCallerStudyScope()
            .WithSummary("Issue or re-issue reloadable debit card for participant wallet.");

        group.MapPost("/participant/wallets/{walletId}/submit-w9", SubmitW9Form)
            .WithName("SubmitW9Form")
            .RequirePermission("payments_w9:submit").RequireCallerStudyScope()
            .WithSummary("Submit Form W-9 taxpayer identification certification.");

        group.MapPost("/participant/wallets/{walletId}/verify-w9", VerifyW9Form)
            .WithName("VerifyW9Form")
            .RequirePermission("payments_w9:verify").RequireCallerStudyScope()
            .WithSummary("Verify participant Form W-9 submission.");

        group.MapPost("/participant/wallets/{walletId}/disburse", DisburseParticipantFunds)
            .WithName("DisburseParticipantFunds")
            .RequirePermission("payments_wallet:disburse").RequireCallerStudyScope()
            .WithSummary("Disburse digital funds from participant wallet to debit card or bank account.");

        // SoA Stipend Rules & Visit Verification Event Trigger
        group.MapGet("/participant/stipend-rules", ListStipendRules)
            .WithName("ListStipendRules")
            .RequirePermission("payments_stipend_rule:read").RequireCallerStudyScope()
            .WithSummary("List SoA protocol visit stipend compensation rules.");

        group.MapPost("/participant/stipend-rules", CreateStipendRule)
            .WithName("CreateStipendRule")
            .RequirePermission("payments_stipend_rule:configure").RequireCallerStudyScope()
            .WithSummary("Configure participant visit compensation rules linked to Schedule of Activities.");

        group.MapPost("/participant/events/visit-verified", IngestVisitVerifiedEvent)
            .WithName("IngestVisitVerifiedEvent")
            .RequirePermission("payments_visit_event:record").RequireCallerStudyScope()
            .WithSummary("Receives verified eCRF visit completion event and evaluates automated stipend disbursement rules.");

        // Travel Expenses & OCR Scanner
        group.MapGet("/participant/expenses", ListExpenseClaims)
            .WithName("ListExpenseClaims")
            .RequirePermission("payments_expense:read").RequireCallerStudyScope()
            .WithSummary("List travel and meal expense reimbursement claims.");

        group.MapPost("/participant/expenses", SubmitStaffExpenseClaim)
            .WithName("SubmitExpenseClaim")
            .RequirePermission("payments_expense:submit").RequireCallerStudyScope()
            .WithSummary("Submit travel/meal expense claim for coordinator review.");

        group.MapPost("/participant/expenses/ocr-extract", ExtractStaffReceiptOcr)
            .WithName("ExtractReceiptOcr")
            .RequirePermission("payments_expense:submit").RequireCallerStudyScope()
            .WithSummary("Extract metadata from receipt image/text using rule-based OCR engine.");

        group.MapPost("/participant/expenses/{claimId}/review", ReviewExpenseClaim)
            .WithName("ReviewExpenseClaim")
            .RequirePermission("payments_expense:review").RequireCallerStudyScope()
            .WithSummary("Coordinator 1-click review and approval/rejection of expense claim.");

        // Authenticated Participant Self-Service Wallet, Receipt OCR, Expense Claims, Payouts & Diary Stipends
        group.MapGet("/participant/me/wallet", GetSelfParticipantWallet)
            .WithName("GetSelfParticipantWallet")
            .RequirePermission("payments_self_wallet:read").RequireCallerStudyScope()
            .WithSummary("Retrieves the authenticated participant's own study wallet balances, masks, and ledger activity.");

        group.MapPost("/participant/me/expenses/ocr-extract", ExtractSelfReceiptOcr)
            .WithName("ExtractSelfReceiptOcr")
            .RequirePermission("payments_self_expense:ocr_scan").RequireCallerStudyScope()
            .WithSummary("Extracts merchant, date, category, and amount from an uploaded receipt image for the signed participant.");

        group.MapPost("/participant/me/expenses", SubmitSelfExpenseClaim)
            .WithName("SubmitSelfExpenseClaim")
            .RequirePermission("payments_self_expense:submit").RequireCallerStudyScope()
            .WithSummary("Submits a travel or meal expense claim for the signed participant with policy auto-approval and direct ACH dispatch.");

        group.MapPost("/participant/me/disburse", DisburseSelfWalletBalance)
            .WithName("DisburseSelfWalletBalance")
            .RequirePermission("payments_self_wallet:disburse").RequireCallerStudyScope()
            .WithSummary("Triggers an on-demand payout of the signed participant's available wallet balance.");

        group.MapPost("/participant/me/diary-stipend", CreditSelfDiaryStipend)
            .WithName("CreditSelfDiaryStipend")
            .RequirePermission("payments_self_stipend:credit").RequireCallerStudyScope()
            .WithSummary("Credits protocol stipend for a synchronized participant diary strictly from server rules.");

        // Transactions, Disbursements & Audit Logs
        group.MapGet("/participant/transactions", ListTransactions)
            .WithName("ListTransactions")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("List participant wallet transactions ledger.");

        group.MapGet("/participant/disbursements", ListDisbursements)
            .WithName("ListDisbursements")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("List digital disbursement dispatches.");

        group.MapPost("/participant/disbursements/{id}/settle", SettleDisbursement)
            .WithName("SettleDisbursement")
            .RequirePermission("payments_wallet:disburse").RequireCallerStudyScope()
            .WithSummary("Marks a dispatched digital disbursement as settled upon confirmation.");

        group.MapGet("/audit-logs", GetAuditLogs)
            .WithName("GetPaymentAuditLogs")
            .RequirePermission("payments_audit_log:read").RequireCallerStudyScope()
            .WithSummary("Retrieve payment audit ledger records.");

        // Self-Billing Invoices & Automated Batch Milestone Invoicing
        group.MapGet("/invoices", ListInvoices)
            .WithName("ListInvoices")
            .RequirePermission("payments_invoice:read").RequireCallerStudyScope()
            .WithSummary("Lists compiled self-billing invoices matching filter criteria.");

        group.MapGet("/invoices/{id}", GetInvoiceById)
            .WithName("GetInvoiceById")
            .RequirePermission("payments_invoice:read").RequireCallerStudyScope()
            .WithSummary("Retrieve single self-billing invoice details with line items.");

        group.MapPost("/invoices/compile", CompileBillingCycle)
            .WithName("CompileBillingCycle")
            .RequirePermission("payments_invoice:compile").RequireCallerStudyScope()
            .WithSummary("Automates single-site billing cycle compilation into a self-billing invoice.");

        group.MapPost("/invoices/batch-compile", BatchCompileBillingCycles)
            .WithName("BatchCompileBillingCycles")
            .RequirePermission("payments_invoice:compile").RequireCallerStudyScope()
            .WithSummary("Executes study-wide batch milestone invoice compilation across all active trial sites.");

        group.MapPost("/invoices/{id}/approve", ApproveInvoice)
            .WithName("ApproveInvoice")
            .RequirePermission("payments_invoice:approve").RequireCallerStudyScope()
            .WithSummary("Approve draft self-billing invoice for ERP export and settlement.");

        group.MapPost("/invoices/{id}/remittance-advice/sign", SignRemittanceAdvice)
            .WithName("SignRemittanceAdvice")
            .RequirePermission("payments_invoice:sign_remittance").RequireCallerStudyScope()
            .WithSummary("Applies 21 CFR Part 11 electronic signature to cryptographic remittance advice.");

        group.MapGet("/invoices/{id}/remittance-advice/pdf", DownloadRemittancePdf)
            .WithName("DownloadRemittancePdf")
            .RequirePermission("payments_invoice:read").RequireCallerStudyScope()
            .WithSummary("Downloads cryptographic PDF remittance statement.");

        group.MapPost("/invoices/{id}/reconcile", ReconcileInvoice)
            .WithName("ReconcileInvoice")
            .RequirePermission("payments_invoice:reconcile").RequireCallerStudyScope()
            .WithSummary("Reconciles invoice settlement against bank wire or ERP clearing statement.");

        group.MapPost("/invoices/{id}/erp-export", ExportInvoiceToErp)
            .WithName("ExportInvoiceToErp")
            .RequirePermission("payments_invoice:export_erp").RequireCallerStudyScope()
            .WithSummary("Exports self-billing invoice to ERP gateway (e.g. SAP).");

        return routes;
    }

    // Authorization has already required a signed caller, so this answers only a request that bypassed it.
    private static IResult NoSignedCaller() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "The request carries no signed caller.");

    // A write that names a study or site the caller is not assigned to (ADR-2516, #5401). Reads and writes by id answer
    // 404 instead, the same as a missing row, so ids outside the caller's scope cannot be probed.
    private static ProblemHttpResult OutsideScope() =>
        ClinicalScopeGuard.Forbidden("The caller is not assigned to this study or site.");

    private static bool Reaches(HttpContext httpContext, string? studyId, string? siteId) =>
        ClinicalScopeGuard.CanAccessStudy(httpContext, studyId)
        && ClinicalScopeGuard.CanAccessSite(httpContext, siteId);

    private static bool Reaches(HttpContext httpContext, ParticipantWallet? wallet) =>
        wallet is not null && Reaches(httpContext, wallet.StudyId, wallet.SiteId);

    private static bool Reaches(HttpContext httpContext, SelfBillingInvoice? invoice) =>
        invoice is not null && Reaches(httpContext, invoice.StudyId, invoice.SiteId);

    private static WalletResponse MapWalletToResponse(ParticipantWallet wallet)
    {
        string routingMask = MaskRoutingIdentifier(wallet.AchRoutingMask) ?? "*****0021";
        string accountMask = MaskAccountIdentifier(wallet.AchAccountMask) ?? "ACH-***4321";
        string cardMask = MaskCardIdentifier(wallet.VirtualCardMask) ?? "**** **** **** 4821";
        return new WalletResponse(
            Id: wallet.Id,
            SubjectId: wallet.SubjectId,
            StudyId: wallet.StudyId,
            SiteId: wallet.SiteId,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            LifetimeReimbursements: wallet.LifetimeReimbursements,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            TaxableYtd: wallet.TaxableYtd,
            Is1099ThresholdExceeded: wallet.Is1099ThresholdExceeded,
            W9Status: wallet.W9Status,
            Currency: wallet.Currency,
            Status: wallet.Status,
            VirtualCardMask: cardMask,
            VirtualCardExpiry: wallet.VirtualCardExpiry ?? "12/29",
            AchRoutingNumber: routingMask,
            AchAccountMask: accountMask,
            AchAccountHolder: wallet.AchAccountHolder ?? $"Participant {wallet.SubjectId}",
            AntiCoercionCap: wallet.AntiCoercionCap,
            AutoDisbursementThreshold: wallet.AutoDisbursementThreshold,
            UpdatedAt: wallet.UpdatedAt,
            CardType: wallet.CardType,
            CardStatus: wallet.CardStatus,
            AchRoutingMask: routingMask
        );
    }

    private static string? MaskRoutingIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            return trimmed;
        }
        return trimmed.Length > 4 ? $"*****{trimmed[^4..]}" : $"*****{trimmed}";
    }

    private static string? MaskAccountIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            return trimmed;
        }
        return trimmed.Length > 4 ? $"ACH-***{trimmed[^4..]}" : $"ACH-***{trimmed}";
    }

    private static string? MaskCardIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            return trimmed;
        }
        return trimmed.Length > 4 ? $"**** **** **** {trimmed[^4..]}" : $"**** **** **** {trimmed}";
    }

    private static async Task<IResult> ListWallets(PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var wallets = await ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantWallets, httpContext, w => w.StudyId, w => w.SiteId)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync();

        return Results.Ok(wallets.Select(MapWalletToResponse));
    }

    private static async Task<IResult> GetWalletById(string walletId, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null || !Reaches(httpContext, wallet))
        {
            return Results.NotFound(new { detail = $"Participant wallet with ID '{walletId}' not found." });
        }

        return Results.Ok(MapWalletToResponse(wallet));
    }

    private static async Task<IResult> GetSubjectWallet(string subjectId, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var wallet = await ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantWallets, httpContext, w => w.StudyId, w => w.SiteId)
            .FirstOrDefaultAsync(w => w.SubjectId == subjectId);

        // A read creates nothing (read-only roles call it): a wallet is opened by a verified visit, an expense claim or
        // a stipend disbursement.
        if (wallet == null)
        {
            return Results.NotFound(new { detail = $"No participant wallet for subject '{subjectId}'." });
        }

        return Results.Ok(MapWalletToResponse(wallet));
    }

    private static async Task<IResult> GetTax1099Status(string walletId, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null || !Reaches(httpContext, wallet))
        {
            return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
        }

        decimal threshold = 600.00m;
        decimal percentage = Math.Min(100m, (wallet.TaxableYtd / threshold) * 100m);
        string warningLevel;
        if (wallet.TaxableYtd >= threshold)
        {
            warningLevel = "EXCEEDED";
        }
        else if (wallet.TaxableYtd >= 500.00m)
        {
            warningLevel = "APPROACHING";
        }
        else
        {
            warningLevel = "NORMAL";
        }

        return Results.Ok(new Tax1099StatusResponseDto(
            WalletId: wallet.Id,
            SubjectId: wallet.SubjectId,
            TaxableYtd: wallet.TaxableYtd,
            ThresholdAmount: threshold,
            ThresholdPercentage: percentage,
            Is1099ThresholdExceeded: wallet.Is1099ThresholdExceeded,
            W9Status: wallet.W9Status,
            WarningLevel: warningLevel
        ));
    }

    private static async Task<IResult> GetBalanceInquiry(string walletId, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null || !Reaches(httpContext, wallet))
        {
            return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
        }

        decimal pendingClaims = await dbContext.ParticipantExpenseClaims
            .Where(c => c.WalletId == wallet.Id && c.Status == "PENDING_REVIEW")
            .SumAsync(c => c.NetAmount);

        return Results.Ok(new WalletBalanceInquiryResponseDto(
            WalletId: wallet.Id,
            SubjectId: wallet.SubjectId,
            AvailableBalance: wallet.CurrentBalance,
            PendingClaimsTotal: pendingClaims,
            LifetimeStipends: wallet.LifetimeStipends,
            LifetimeReimbursements: wallet.LifetimeReimbursements,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            TaxableYtd: wallet.TaxableYtd
        ));
    }

    private static bool SupportsRowLevelForUpdate(DbContext dbContext) =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) is true;

    private static Task<ParticipantWallet?> LockWalletByIdAsync(
        PaymentsDbContext dbContext,
        string walletId,
        CancellationToken ct = default) =>
        SupportsRowLevelForUpdate(dbContext)
            ? dbContext.ParticipantWallets
                .FromSqlInterpolated($"SELECT * FROM payments_participant_wallets WHERE \"Id\" = {walletId} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
            : dbContext.ParticipantWallets
                .FirstOrDefaultAsync(w => w.Id == walletId, ct);

    private static Task<ParticipantWallet?> LockWalletBySubjectAndStudyAsync(
        PaymentsDbContext dbContext,
        string subjectId,
        string studyId,
        CancellationToken ct = default) =>
        SupportsRowLevelForUpdate(dbContext)
            ? dbContext.ParticipantWallets
                .FromSqlInterpolated($"SELECT * FROM payments_participant_wallets WHERE \"SubjectId\" = {subjectId} AND \"StudyId\" = {studyId} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
            : dbContext.ParticipantWallets
                .FirstOrDefaultAsync(w => w.SubjectId == subjectId && w.StudyId == studyId, ct);

    private static Task<List<ParticipantWallet>> LockWalletsBySubjectAsync(
        PaymentsDbContext dbContext,
        string subjectId,
        CancellationToken ct = default) =>
        SupportsRowLevelForUpdate(dbContext)
            ? dbContext.ParticipantWallets
                .FromSqlInterpolated($"SELECT * FROM payments_participant_wallets WHERE \"SubjectId\" = {subjectId} FOR UPDATE")
                .ToListAsync(ct)
            : dbContext.ParticipantWallets
                .Where(w => w.SubjectId == subjectId)
                .ToListAsync(ct);

    private static bool IsUniqueViolation(DbUpdateException ex, string? constraintContains = null) =>
        (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && (constraintContains is null
                || (pg.ConstraintName is not null
                    && pg.ConstraintName.Contains(constraintContains, StringComparison.OrdinalIgnoreCase))))
        || (ex.InnerException is not null
            && string.Equals(ex.InnerException.GetType().Name, "SqliteException", StringComparison.Ordinal)
            && ex.InnerException.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            && (constraintContains is null
                || ex.InnerException.Message.Contains(constraintContains, StringComparison.OrdinalIgnoreCase)));

    private static async Task<IResult> ExecuteWalletUnitOfWorkAsync(
        PaymentsDbContext dbContext,
        Func<CancellationToken, Task<IResult>> unitOfWork,
        CancellationToken cancellationToken = default)
    {
        for (int walletInitAttempt = 0; walletInitAttempt < 2; walletInitAttempt++)
        {
            try
            {
                return await dbContext.ExecuteInTransactionAsync(async ct =>
                {
                    if (dbContext.Database.IsRelational())
                    {
                        dbContext.ChangeTracker.Clear();
                    }

                    return await unitOfWork(ct);
                }, cancellationToken: cancellationToken);
            }
            catch (DbUpdateException ex) when (walletInitAttempt == 0 && IsUniqueViolation(ex, "payments_participant_wallets"))
            {
                dbContext.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Unreachable wallet initialization retry fallback.");
    }

    private static async Task<IResult> IssueReloadableCard(string walletId, IssueCardRequestDto request, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);

        return await ExecuteWalletUnitOfWorkAsync(dbContext, async ct =>
        {
            var wallet = await LockWalletByIdAsync(dbContext, walletId, ct);

            if (wallet == null || !Reaches(httpContext, wallet))
            {
                return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
            }

            DateTimeOffset now = SystemClock.Instance.UtcNow;
            wallet.CardType = request.CardType ?? "RELOADABLE_DEBIT";
            wallet.CardStatus = "ACTIVE";
            wallet.VirtualCardMask = $"**** **** **** {Random.Shared.Next(1000, 9999)}";
            wallet.VirtualCardExpiry = $"{now.Month:D2}/{(now.Year + 3) % 100:D2}";
            wallet.VirtualCardProcessorReference = $"VTOK-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
            wallet.UpdatedAt = now;
            wallet.VersionIndex += 1;

            var audit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "RELOADABLE_CARD_ISSUED",
                UserId = userId,
                UserRoles = rolesText,
                Details = JsonSerializer.Serialize(new { card_type = wallet.CardType, mask = wallet.VirtualCardMask, expiry = wallet.VirtualCardExpiry }),
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = "Reloadable debit card issued to participant",
                VersionIndex = wallet.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(audit);
            await dbContext.SaveChangesAsync(ct);

            return Results.Ok(MapWalletToResponse(wallet));
        });
    }

    private static bool IsFourAsciiDigits(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length == 4 && value.All(char.IsAsciiDigit);

    private static async Task<string?> ResolveLatestW9SubmitterAsync(
        PaymentsDbContext dbContext,
        ParticipantWallet wallet,
        CancellationToken ct)
    {
        var submissionLogs = await dbContext.PaymentAuditLogs.AsNoTracking()
            .Where(l => l.EntityType == "ParticipantWallet" && l.EntityId == wallet.Id && l.Action == "W9_FORM_SUBMITTED")
            .Select(l => new { l.UserId, l.CreatedBy, l.CreatedAt, l.VersionIndex })
            .ToListAsync(ct);

        var latestSubmission = submissionLogs
            .OrderByDescending(l => l.VersionIndex)
            .ThenByDescending(l => l.CreatedAt)
            .FirstOrDefault();

        if (latestSubmission == null)
        {
            return wallet.CreatedBy;
        }

        return !string.IsNullOrWhiteSpace(latestSubmission.UserId)
            ? latestSubmission.UserId
            : latestSubmission.CreatedBy;
    }

    private static async Task<IResult> SubmitW9Form(string walletId, SubmitW9RequestDto request, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);

        return await ExecuteWalletUnitOfWorkAsync(dbContext, async ct =>
        {
            var wallet = await LockWalletByIdAsync(dbContext, walletId, ct);

            if (wallet == null || !Reaches(httpContext, wallet))
            {
                return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
            }

            string? trimmedLegalName = request.FullLegalName?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLegalName))
            {
                return Results.UnprocessableEntity(new { detail = "Full legal name is required for Form W-9 submission." });
            }

            string? trimmedLastFour = request.TaxIdLastFour?.Trim();
            if (!IsFourAsciiDigits(trimmedLastFour))
            {
                return Results.UnprocessableEntity(new { detail = "TIN format does not conform to IRS requirements: taxIdLastFour must be four digits." });
            }

            if (!request.CertifyAccurate)
            {
                return Results.UnprocessableEntity(new { detail = "W-9 certification must be agreed and verified as accurate." });
            }

            DateTimeOffset now = SystemClock.Instance.UtcNow;
            string priorStatus = wallet.W9Status;
            wallet.TaxIdLastFour = trimmedLastFour;
            wallet.W9Status = "SUBMITTED";
            wallet.W9SubmittedAt = now;
            wallet.W9VerifiedAt = null;
            wallet.UpdatedAt = now;
            wallet.VersionIndex += 1;

            var audit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "W9_FORM_SUBMITTED",
                UserId = userId,
                UserRoles = rolesText,
                PreviousStateJson = JsonSerializer.Serialize(new { w9_status = priorStatus }),
                NewStateJson = JsonSerializer.Serialize(new { w9_status = wallet.W9Status, tax_id_last_four = trimmedLastFour }),
                Details = JsonSerializer.Serialize(new { full_legal_name = trimmedLegalName, tax_id_last_four = trimmedLastFour, address = request.Address }),
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = "Form W-9 taxpayer identification certification submitted for the participant",
                VersionIndex = wallet.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(audit);
            await dbContext.SaveChangesAsync(ct);

            return Results.Ok(MapWalletToResponse(wallet));
        });
    }

    private static async Task<IResult> VerifyW9Form(string walletId, VerifyW9RequestDto request, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);

        return await ExecuteWalletUnitOfWorkAsync(dbContext, async ct =>
        {
            var wallet = await LockWalletByIdAsync(dbContext, walletId, ct);

            if (wallet == null || !Reaches(httpContext, wallet))
            {
                return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
            }

            if (!string.Equals(wallet.W9Status, "SUBMITTED", StringComparison.OrdinalIgnoreCase)
                || !IsFourAsciiDigits(wallet.TaxIdLastFour?.Trim()))
            {
                return Results.Conflict(new
                {
                    detail = "Form W-9 can only be verified when a submitted Form W-9 with a four-digit taxpayer identification suffix is pending review."
                });
            }

            // Form W-9 submission and verification share site clinical roles (crc, investigator), so two people must
            // perform them: the user who submitted the latest Form W-9 may not also verify it (separation of duties).
            string? submittedBy = await ResolveLatestW9SubmitterAsync(dbContext, wallet, ct);
            if (!string.IsNullOrWhiteSpace(submittedBy)
                && string.Equals(submittedBy, userId, StringComparison.Ordinal))
            {
                return Results.Conflict(new { detail = "The user who submitted this Form W-9 cannot also verify it." });
            }

            DateTimeOffset now = SystemClock.Instance.UtcNow;
            string priorStatus = wallet.W9Status;
            wallet.W9Status = "VERIFIED";
            wallet.W9VerifiedAt = now;
            wallet.UpdatedAt = now;
            wallet.VersionIndex += 1;

            var audit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "W9_FORM_VERIFIED",
                UserId = userId,
                UserRoles = rolesText,
                PreviousStateJson = JsonSerializer.Serialize(new { w9_status = priorStatus }),
                NewStateJson = JsonSerializer.Serialize(new { w9_status = wallet.W9Status }),
                Details = JsonSerializer.Serialize(new { notes = request.Notes ?? "TIN matched against verification registry." }),
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = "Site staff validated Form W-9 taxpayer identification",
                VersionIndex = wallet.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(audit);
            await dbContext.SaveChangesAsync(ct);

            return Results.Ok(MapWalletToResponse(wallet));
        });
    }

    private static async Task<IResult> DisburseParticipantFunds(string walletId, DisburseFundsRequestDto request, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);

        return await ExecuteWalletUnitOfWorkAsync(dbContext, async ct =>
        {
            var wallet = await LockWalletByIdAsync(dbContext, walletId, ct);

            if (wallet == null || !Reaches(httpContext, wallet))
            {
                return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
            }

            if (wallet.TaxableYtd >= 600.00m && !string.Equals(wallet.W9Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Conflict(new
                {
                    detail = "IRS $600.00 annual taxable payment threshold reached without a verified Form W-9. Automated payouts are halted until Form W-9 is verified."
                });
            }

            decimal payoutAmount = request.Amount > 0m ? request.Amount : wallet.CurrentBalance;

            if (payoutAmount <= 0m)
            {
                return Results.BadRequest(new { detail = "Disbursement amount must be greater than zero." });
            }

            if (payoutAmount > wallet.CurrentBalance)
            {
                return Results.BadRequest(new { detail = $"Insufficient funds: Requested ${payoutAmount:F2} but available balance is ${wallet.CurrentBalance:F2}." });
            }

            DateTimeOffset now = SystemClock.Instance.UtcNow;
            wallet.CurrentBalance -= payoutAmount;
            wallet.LifetimeDisbursements += payoutAmount;
            wallet.UpdatedAt = now;
            wallet.VersionIndex += 1;

            string refPrefix = request.DisbursementType == "DIRECT_ACH" ? "ACH" : "VCARD";
            string refNumber = $"DISB-{refPrefix}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

            var disbursement = new ParticipantDisbursement
            {
                Id = Guid.NewGuid().ToString(),
                WalletId = wallet.Id,
                SubjectId = wallet.SubjectId,
                Amount = payoutAmount,
                DisbursementType = request.DisbursementType,
                ReferenceNumber = refNumber,
                Status = "DISPATCHED",
                CreatedAt = now,
                CreatedBy = userId,
                VersionIndex = 1
            };
            dbContext.ParticipantDisbursements.Add(disbursement);

            var tx = new ParticipantTransaction
            {
                Id = Guid.NewGuid().ToString(),
                WalletId = wallet.Id,
                SubjectId = wallet.SubjectId,
                StudyId = wallet.StudyId,
                TransactionType = "DISBURSEMENT_DEBIT",
                Amount = -payoutAmount,
                BalanceAfter = wallet.CurrentBalance,
                ReferenceId = disbursement.Id,
                Description = $"Digital payout dispatch via {request.DisbursementType} ({refNumber})",
                CreatedAt = now,
                CreatedBy = userId,
                VersionIndex = 1
            };
            dbContext.ParticipantTransactions.Add(tx);

            var audit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "DIGITAL_DISBURSEMENT_DISPATCHED",
                UserId = userId,
                UserRoles = rolesText,
                Details = JsonSerializer.Serialize(new { disbursement_id = disbursement.Id, amount = payoutAmount, reference_number = refNumber, disbursement_type = request.DisbursementType, new_balance = wallet.CurrentBalance }),
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = "Digital stipend payout funds dispatched to participant card/account",
                VersionIndex = wallet.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(audit);

            await dbContext.SaveChangesAsync(ct);

            return Results.Ok(disbursement);
        });
    }

    private static async Task<IResult> ListStipendRules(PaymentsDbContext dbContext, HttpContext httpContext, string? studyId = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var query = ClinicalScopeGuard.NarrowToStudyScope(dbContext.ParticipantStipendRules.AsNoTracking(), httpContext, r => r.StudyId);
        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(r => r.StudyId == studyId);
        }

        var rules = await query.Where(r => r.IsActive).ToListAsync();
        return Results.Ok(rules);
    }

    private static async Task<IResult> CreateStipendRule(ParticipantStipendRuleCreateDto request, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, request.StudyId))
        {
            return OutsideScope();
        }

        if (request.AntiCoercionCap.HasValue && request.Amount > request.AntiCoercionCap.Value)
        {
            return Results.UnprocessableEntity(new { detail = $"Stipend amount (${request.Amount:F2}) cannot exceed IRB anti-coercion cap limit (${request.AntiCoercionCap.Value:F2})." });
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        DateTimeOffset now = SystemClock.Instance.UtcNow;
        var existing = await dbContext.ParticipantStipendRules
            .FirstOrDefaultAsync(r => r.IsActive && r.StudyId == request.StudyId && r.VisitCode == request.VisitCode);

        if (existing != null)
        {
            existing.VisitName = request.VisitName;
            existing.ProcedureCode = request.ProcedureCode;
            existing.Amount = request.Amount;
            existing.Currency = request.Currency ?? "USD";
            existing.RequiresVerification = request.RequiresVerification;
            existing.Taxable = request.Taxable;
            existing.AntiCoercionCap = request.AntiCoercionCap;
            existing.AutoDisbursementThreshold = request.AutoDisbursementThreshold;
            existing.IsMilestone = request.IsMilestone;
            existing.MilestoneCode = request.MilestoneCode;
            existing.UpdatedAt = now;
            existing.VersionIndex += 1;

            var audit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantStipendRule",
                EntityId = existing.Id,
                Action = "PARTICIPANT_STIPEND_RULE_CREATED",
                UserId = userId,
                UserRoles = rolesText,
                Details = JsonSerializer.Serialize(new { study_id = request.StudyId, visit_code = request.VisitCode, amount = request.Amount }),
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = "SoA visit stipend compensation rule updated",
                VersionIndex = existing.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(audit);
            await dbContext.SaveChangesAsync();

            return Results.Ok(existing);
        }

        var rule = new ParticipantStipendRule
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = request.StudyId,
            VisitCode = request.VisitCode,
            VisitName = request.VisitName,
            ProcedureCode = request.ProcedureCode,
            Amount = request.Amount,
            Currency = request.Currency ?? "USD",
            RequiresVerification = request.RequiresVerification,
            Taxable = request.Taxable,
            AntiCoercionCap = request.AntiCoercionCap,
            AutoDisbursementThreshold = request.AutoDisbursementThreshold,
            IsMilestone = request.IsMilestone,
            MilestoneCode = request.MilestoneCode,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = userId,
            UpdatedAt = now,
            VersionIndex = 1
        };
        dbContext.ParticipantStipendRules.Add(rule);

        var createAudit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantStipendRule",
            EntityId = rule.Id,
            Action = "PARTICIPANT_STIPEND_RULE_CREATED",
            UserId = userId,
            UserRoles = rolesText,
            Details = JsonSerializer.Serialize(new { study_id = request.StudyId, visit_code = request.VisitCode, amount = request.Amount }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "SoA visit stipend compensation rule created",
            VersionIndex = 1
        };
        dbContext.PaymentAuditLogs.Add(createAudit);

        await dbContext.SaveChangesAsync();

        return Results.Ok(rule);
    }

    private static async Task<IResult> IngestVisitVerifiedEvent(VisitVerificationEventDto request, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.SubjectId) || string.IsNullOrWhiteSpace(request.VisitCode))
        {
            return Results.BadRequest(new { detail = "subject_id and visit_code are required." });
        }

        // Only staff of the named study and site may credit a stipend there, and an existing wallet stays at its own
        // site: a body naming the caller's site does not reach another site's participant (#5401).
        if (!Reaches(httpContext, request.StudyId, request.SiteId))
        {
            return OutsideScope();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        string eventId = request.EventId ?? $"EVT-{Guid.NewGuid():N}";
        DateTimeOffset now = SystemClock.Instance.UtcNow;

        try
        {
            return await ExecuteWalletUnitOfWorkAsync(dbContext, async ct =>
            {
                var wallet = await LockWalletBySubjectAndStudyAsync(dbContext, request.SubjectId, request.StudyId, ct);
                if (wallet != null && !Reaches(httpContext, wallet))
                {
                    return OutsideScope();
                }

                // 1. Idempotency Check inside locked wallet transaction
                var existingTx = await dbContext.ParticipantTransactions
                    .FirstOrDefaultAsync(t => t.ReferenceId == eventId || t.IdempotencyKey == eventId, ct);

                if (existingTx != null)
                {
                    var existingWallet = wallet ?? await dbContext.ParticipantWallets
                        .FirstOrDefaultAsync(w => w.Id == existingTx.WalletId, ct);
                    if (existingWallet != null && !Reaches(httpContext, existingWallet))
                    {
                        return OutsideScope();
                    }

                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "ALREADY_PROCESSED",
                        Amount: 0m,
                        CurrentBalance: existingWallet?.CurrentBalance ?? 0m,
                        WalletId: existingTx.WalletId,
                        TransactionId: existingTx.Id,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: "Duplicate visit verification event already processed."
                    ));
                }

                var existingAudit = await dbContext.PaymentAuditLogs
                    .FirstOrDefaultAsync(l => l.Details != null && l.Details.Contains(eventId), ct);

                if (existingAudit != null)
                {
                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "ALREADY_PROCESSED",
                        Amount: 0m,
                        CurrentBalance: wallet?.CurrentBalance ?? 0m,
                        WalletId: wallet?.Id ?? string.Empty,
                        TransactionId: null,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: "Duplicate visit verification event already recorded in audit ledger."
                    ));
                }

                // 2. Find active stipend rule
                var rule = await dbContext.ParticipantStipendRules
                    .FirstOrDefaultAsync(r => r.IsActive && r.StudyId == request.StudyId && r.VisitCode == request.VisitCode, ct);

                if (rule == null)
                {
                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "SKIPPED",
                        Amount: 0m,
                        CurrentBalance: 0m,
                        WalletId: string.Empty,
                        TransactionId: null,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: $"No active stipend compensation rule found for visit_code '{request.VisitCode}' and study_id '{request.StudyId}'."
                    ));
                }

                // 3. Provision participant wallet if missing
                if (wallet == null)
                {
                    wallet = new ParticipantWallet
                    {
                        Id = Guid.NewGuid().ToString(),
                        SubjectId = request.SubjectId,
                        StudyId = request.StudyId,
                        SiteId = request.SiteId,
                        CurrentBalance = 0m,
                        LifetimeStipends = 0m,
                        LifetimeReimbursements = 0m,
                        LifetimeDisbursements = 0m,
                        Currency = rule.Currency ?? "USD",
                        Status = "ACTIVE",
                        W9Status = "NOT_REQUIRED",
                        CreatedAt = now,
                        CreatedBy = userId,
                        UpdatedAt = now,
                        VersionIndex = 1
                    };
                    dbContext.ParticipantWallets.Add(wallet);

                    var walletAudit = new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "PARTICIPANT_WALLET_CREATED",
                        UserId = userId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new { subject_id = request.SubjectId, study_id = request.StudyId, site_id = request.SiteId }),
                        CreatedAt = now,
                        CreatedBy = userId,
                        ReasonForChange = "Initial wallet provisioned on eCRF visit verification",
                        VersionIndex = 1
                    };
                    dbContext.PaymentAuditLogs.Add(walletAudit);
                }

                decimal stipendAmount = rule.Amount;

                // 4. IRB Anti-Coercion Cap Check
                bool ruleCapExceeded = rule.AntiCoercionCap.HasValue && stipendAmount > rule.AntiCoercionCap.Value;
                bool walletCapExceeded = wallet.AntiCoercionCap.HasValue && stipendAmount > wallet.AntiCoercionCap.Value;

                if (ruleCapExceeded || walletCapExceeded)
                {
                    decimal effectiveCap = Math.Min(
                        rule.AntiCoercionCap ?? decimal.MaxValue,
                        wallet.AntiCoercionCap ?? decimal.MaxValue);

                    var capAudit = new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "ANTI_COERCION_CAP_EXCEEDED",
                        UserId = userId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new { event_id = eventId, visit_code = request.VisitCode, amount = stipendAmount, anti_coercion_cap = effectiveCap, timestamp = now }),
                        CreatedAt = now,
                        CreatedBy = userId,
                        ReasonForChange = "Stipend amount exceeds IRB anti-coercion cap limit",
                        VersionIndex = wallet.VersionIndex
                    };
                    dbContext.PaymentAuditLogs.Add(capAudit);
                    await dbContext.SaveChangesAsync(ct);

                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "FLAGGED_FOR_REVIEW",
                        Amount: stipendAmount,
                        CurrentBalance: wallet.CurrentBalance,
                        WalletId: wallet.Id,
                        TransactionId: null,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: $"Payment of ${stipendAmount:F2} exceeds IRB anti-coercion cap limit (${effectiveCap:F2}). Routed to administrator review queue.",
                        RequiresReview: true
                    ));
                }

                // 5. Configurable Auto-Disbursement Safety Threshold Check
                bool ruleThresholdExceeded = rule.AutoDisbursementThreshold.HasValue && stipendAmount > rule.AutoDisbursementThreshold.Value;
                bool walletThresholdExceeded = wallet.AutoDisbursementThreshold.HasValue && stipendAmount > wallet.AutoDisbursementThreshold.Value;

                if (ruleThresholdExceeded || walletThresholdExceeded)
                {
                    decimal effectiveThreshold = Math.Min(
                        rule.AutoDisbursementThreshold ?? decimal.MaxValue,
                        wallet.AutoDisbursementThreshold ?? decimal.MaxValue);

                    var thresholdAudit = new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "THRESHOLD_EXCEEDED_REVIEW_REQUIRED",
                        UserId = userId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new { event_id = eventId, visit_code = request.VisitCode, amount = stipendAmount, auto_disbursement_threshold = effectiveThreshold, timestamp = now }),
                        CreatedAt = now,
                        CreatedBy = userId,
                        ReasonForChange = "Stipend amount exceeds auto-disbursement safety threshold",
                        VersionIndex = wallet.VersionIndex
                    };
                    dbContext.PaymentAuditLogs.Add(thresholdAudit);
                    await dbContext.SaveChangesAsync(ct);

                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "FLAGGED_FOR_REVIEW",
                        Amount: stipendAmount,
                        CurrentBalance: wallet.CurrentBalance,
                        WalletId: wallet.Id,
                        TransactionId: null,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: $"Payment of ${stipendAmount:F2} exceeds configured auto-disbursement threshold limit (${effectiveThreshold:F2}). Routed to administrator review queue.",
                        RequiresReview: true
                    ));
                }

                // 6. IRS $600 YTD Tax Compliance Check
                decimal potentialTaxableYtd = rule.Taxable ? wallet.TaxableYtd + stipendAmount : wallet.TaxableYtd;

                if (potentialTaxableYtd >= 600.00m && wallet.W9Status != "VERIFIED")
                {
                    wallet.Is1099ThresholdExceeded = true;
                    if (wallet.W9Status == "NOT_REQUIRED")
                    {
                        wallet.W9Status = "REQUIRED";
                    }
                    wallet.UpdatedAt = now;
                    wallet.VersionIndex += 1;

                    var taxAudit = new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "IRS_1099_THRESHOLD_EXCEEDED",
                        UserId = userId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new { event_id = eventId, visit_code = request.VisitCode, amount = stipendAmount, taxable_ytd = potentialTaxableYtd, w9_status = wallet.W9Status, timestamp = now }),
                        CreatedAt = now,
                        CreatedBy = userId,
                        ReasonForChange = "Participant YTD taxable earnings reach or exceed $600.00 IRS 1099 threshold without verified W-9",
                        VersionIndex = wallet.VersionIndex
                    };
                    dbContext.PaymentAuditLogs.Add(taxAudit);
                    await dbContext.SaveChangesAsync(ct);

                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "FLAGGED_FOR_REVIEW",
                        Amount: stipendAmount,
                        CurrentBalance: wallet.CurrentBalance,
                        WalletId: wallet.Id,
                        TransactionId: null,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: $"IRS $600.00 YTD tax threshold reached (YTD: ${potentialTaxableYtd:F2}) without a verified W-9 form. Auto-disbursement halted for administrator review.",
                        RequiresReview: true,
                        W9Status: wallet.W9Status
                    ));
                }

                // 7. Auto-disburse: Credit Wallet & Record Ledger Transaction
                wallet.CurrentBalance += stipendAmount;
                wallet.LifetimeStipends += stipendAmount;
                if (rule.Taxable)
                {
                    wallet.TaxableYtd += stipendAmount;
                    if (wallet.TaxableYtd >= 600.00m)
                    {
                        wallet.Is1099ThresholdExceeded = true;
                    }
                }
                wallet.UpdatedAt = now;
                wallet.VersionIndex += 1;

                string txType = rule.IsMilestone ? "MILESTONE_CREDIT" : "STIPEND_CREDIT";
                var tx = new ParticipantTransaction
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = request.SubjectId,
                    StudyId = request.StudyId,
                    TransactionType = txType,
                    Amount = stipendAmount,
                    BalanceAfter = wallet.CurrentBalance,
                    ReferenceId = eventId,
                    IdempotencyKey = eventId,
                    Description = $"Verified visit stipend credit for {rule.VisitName} ({rule.VisitCode})",
                    CreatedAt = now,
                    CreatedBy = userId,
                    VersionIndex = 1
                };
                dbContext.ParticipantTransactions.Add(tx);

                var creditAudit = new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantWallet",
                    EntityId = wallet.Id,
                    Action = "PARTICIPANT_STIPEND_CREDITED",
                    UserId = userId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new
                    {
                        event_id = eventId,
                        visit_code = request.VisitCode,
                        stipend_amount = stipendAmount,
                        new_balance = wallet.CurrentBalance,
                        transaction_id = tx.Id,
                        rule_id = rule.Id,
                        timestamp = now
                    }),
                    CreatedAt = now,
                    CreatedBy = userId,
                    ReasonForChange = $"EDC visit verification automated stipend payout for {rule.VisitCode}",
                    VersionIndex = wallet.VersionIndex
                };
                dbContext.PaymentAuditLogs.Add(creditAudit);

                await dbContext.SaveChangesAsync(ct);

                return Results.Ok(new VisitVerificationResponseDto(
                    Status: "CREDITED",
                    Amount: stipendAmount,
                    CurrentBalance: wallet.CurrentBalance,
                    WalletId: wallet.Id,
                    TransactionId: tx.Id,
                    EventId: eventId,
                    SubjectId: request.SubjectId
                ));
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "payments_participant_transactions"))
        {
            dbContext.ChangeTracker.Clear();
            var racedTx = await dbContext.ParticipantTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.ReferenceId == eventId || t.IdempotencyKey == eventId);
            var racedWallet = await dbContext.ParticipantWallets.AsNoTracking()
                .FirstOrDefaultAsync(w => w.SubjectId == request.SubjectId && w.StudyId == request.StudyId);
            if (racedWallet != null && !Reaches(httpContext, racedWallet))
            {
                return OutsideScope();
            }
            if (racedTx != null && racedWallet != null)
            {
                return Results.Ok(new VisitVerificationResponseDto(
                    Status: "ALREADY_PROCESSED",
                    Amount: 0m,
                    CurrentBalance: racedWallet.CurrentBalance,
                    WalletId: racedWallet.Id,
                    TransactionId: racedTx.Id,
                    EventId: eventId,
                    SubjectId: request.SubjectId,
                    Reason: "Duplicate visit verification event already processed."
                ));
            }
            throw;
        }
    }

    private static async Task<IResult> ListExpenseClaims(PaymentsDbContext dbContext, HttpContext httpContext, string? status = null, string? subjectId = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var query = ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantExpenseClaims, httpContext, c => c.StudyId, c => c.SiteId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query = query.Where(c => c.SubjectId == subjectId);
        }

        var claims = await query
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return Results.Ok(claims);
    }

    private const string OcrTokenConsumedAuditAction = "OCR_TOKEN_CONSUMED";
    private const string DiarySyncTokenConsumedAuditAction = "DIARY_SYNC_TOKEN_CONSUMED";
    private const string ReceiptDigestRecordedAuditAction = "EXPENSE_RECEIPT_DIGEST_RECORDED";
    private static readonly TimeSpan OcrTokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DiarySyncTokenLifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Payload embedded in a server-signed single-use HMAC ePRO diary synchronization token.
    /// </summary>
    public sealed record DiarySyncVerificationTokenPayload(
        string TokenId,
        string CallerUserId,
        string StudyCode,
        string? DiaryId,
        string? AssignmentId,
        int? SequenceNumber,
        long IssuedAtUnix,
        long ExpiresAtUnix);

    private sealed record ReceiptOcrTokenPayload(
        string ScanId,
        string CallerUserId,
        string ReceiptSha256,
        bool OcrReadable,
        string MerchantName,
        string ExpenseDate,
        decimal GrossAmount,
        string Category,
        double ConfidenceScore,
        long IssuedAtUnix,
        long ExpiresAtUnix);

    private sealed record ServerExpensePolicyEvaluation(
        string NormalizedCategory,
        string MerchantName,
        string ResolvedExpenseDate,
        decimal GrossAmount,
        decimal TaxDeduction,
        decimal NetAmount,
        double OcrConfidence,
        bool CanAutoApprove,
        string InitialStatus,
        string ApprovalDecision,
        string ReviewNotes,
        string ServerReceiptOcrJson,
        string? ConsumedScanId,
        string? NewReceiptSha256);

    private static string? ResolveConfiguredGatewaySecret(IOptionsMonitor<GatewayAuthenticationOptions>? gatewayAuthentication)
    {
        string? fromOptions = gatewayAuthentication?.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret;
        if (!string.IsNullOrWhiteSpace(fromOptions))
        {
            return fromOptions;
        }

        string? fromEnv = Environment.GetEnvironmentVariable(
            CadenceSecurityServiceCollectionExtensions.GatewaySecretConfigurationKey);
        return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : null;
    }

    private static string DeriveOcrTokenSigningKey(string gatewaySecret) =>
        HmacSigner.ComputeSignature("cadence-payments-receipt-ocr-token-v1", gatewaySecret);

    private static string DeriveDiarySyncTokenSigningKey(string gatewaySecret) =>
        HmacSigner.ComputeSignature("cadence-payments-diary-sync-token-v1", gatewaySecret);

    private static string NormalizeOptionalTokenField(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string SignHmacTokenPayload<TPayload>(TPayload payload, string signingKey)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        string encodedPayload = Convert.ToBase64String(jsonBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        string signature = HmacSigner.ComputeSignature(encodedPayload, signingKey);
        return $"{encodedPayload}.{signature}";
    }

    private static bool TryDecodeAndVerifyHmacTokenPayload<TPayload>(
        string? token,
        string signingKey,
        out TPayload? payload)
        where TPayload : class
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        string encodedPayload = parts[0];
        string signature = parts[1];
        if (!HmacSigner.VerifySignature(encodedPayload, signingKey, signature))
        {
            return false;
        }

        try
        {
            string padded = encodedPayload.Replace('-', '+').Replace('_', '/');
            int remainder = padded.Length % 4;
            if (remainder == 2)
            {
                padded += "==";
            }
            else if (remainder == 3)
            {
                padded += "=";
            }
            else if (remainder != 0)
            {
                return false;
            }

            byte[] jsonBytes = Convert.FromBase64String(padded);
            payload = JsonSerializer.Deserialize<TPayload>(jsonBytes);
            return payload is not null;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string IssueReceiptOcrToken(ReceiptOcrTokenPayload payload, string? gatewaySecret)
    {
        if (string.IsNullOrWhiteSpace(gatewaySecret))
        {
            throw new InvalidOperationException("GATEWAY_SECRET is not configured; receipt OCR tokens cannot be signed.");
        }

        return SignHmacTokenPayload(payload, DeriveOcrTokenSigningKey(gatewaySecret));
    }

    /// <summary>
    /// Issues a single-use HMAC verification token proving server-confirmed ePRO diary synchronization
    /// bound to the caller, study, diary, assignment, sequence number, and expiration window.
    /// </summary>
    public static string IssueDiarySyncVerificationToken(
        string callerUserId,
        string studyCode,
        string? diaryId,
        string? assignmentId,
        int? sequenceNumber,
        DateTimeOffset issuedAt,
        string? gatewaySecret,
        TimeSpan? lifetime = null,
        string? tokenId = null)
    {
        if (string.IsNullOrWhiteSpace(gatewaySecret))
        {
            throw new InvalidOperationException("GATEWAY_SECRET is not configured; diary sync verification tokens cannot be signed.");
        }

        string resolvedTokenId = string.IsNullOrWhiteSpace(tokenId)
            ? "DIARY_SYNC_" + Guid.NewGuid().ToString("N")
            : tokenId.Trim();
        TimeSpan resolvedLifetime = lifetime ?? DiarySyncTokenLifetime;
        var payload = new DiarySyncVerificationTokenPayload(
            TokenId: resolvedTokenId,
            CallerUserId: callerUserId,
            StudyCode: studyCode,
            DiaryId: NormalizeOptionalTokenField(diaryId),
            AssignmentId: NormalizeOptionalTokenField(assignmentId),
            SequenceNumber: sequenceNumber,
            IssuedAtUnix: issuedAt.ToUnixTimeSeconds(),
            ExpiresAtUnix: issuedAt.Add(resolvedLifetime).ToUnixTimeSeconds());

        return SignHmacTokenPayload(payload, DeriveDiarySyncTokenSigningKey(gatewaySecret));
    }

    private static bool TryVerifyReceiptOcrToken(
        string? token,
        string? gatewaySecret,
        string expectedCallerUserId,
        DateTimeOffset now,
        out ReceiptOcrTokenPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(gatewaySecret)
            || !TryDecodeAndVerifyHmacTokenPayload(token, DeriveOcrTokenSigningKey(gatewaySecret), out ReceiptOcrTokenPayload? parsed)
            || parsed is null)
        {
            return false;
        }

        if (parsed.ExpiresAtUnix < now.ToUnixTimeSeconds()
            || !string.Equals(parsed.CallerUserId, expectedCallerUserId, StringComparison.Ordinal))
        {
            return false;
        }

        payload = parsed;
        return true;
    }

    private static bool TryVerifyDiarySyncVerificationToken(
        string? token,
        string? gatewaySecret,
        string expectedCallerUserId,
        string expectedStudyCode,
        string? expectedDiaryId,
        string? expectedAssignmentId,
        int? expectedSequenceNumber,
        DateTimeOffset now,
        out DiarySyncVerificationTokenPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(gatewaySecret)
            || !TryDecodeAndVerifyHmacTokenPayload(token, DeriveDiarySyncTokenSigningKey(gatewaySecret), out DiarySyncVerificationTokenPayload? parsed)
            || parsed is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.TokenId)
            || parsed.ExpiresAtUnix < now.ToUnixTimeSeconds()
            || !string.Equals(parsed.CallerUserId, expectedCallerUserId, StringComparison.Ordinal)
            || !string.Equals(parsed.StudyCode, expectedStudyCode, StringComparison.Ordinal)
            || !string.Equals(NormalizeOptionalTokenField(parsed.DiaryId), NormalizeOptionalTokenField(expectedDiaryId), StringComparison.Ordinal)
            || !string.Equals(NormalizeOptionalTokenField(parsed.AssignmentId), NormalizeOptionalTokenField(expectedAssignmentId), StringComparison.Ordinal)
            || parsed.SequenceNumber != expectedSequenceNumber)
        {
            return false;
        }

        payload = parsed;
        return true;
    }

    private static byte[] DecodeReceiptImageBytes(string? imageBase64) =>
        ReceiptOcrEngine.TryDecodeReceiptPayload(imageBase64, out byte[] bytes)
            ? bytes
            : [];

    private static string? TryExtractOcrTokenFromObject(object? ocrData)
    {
        if (ocrData is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("ocrToken", out var camelProp) && camelProp.ValueKind == JsonValueKind.String)
        {
            return camelProp.GetString();
        }

        if (element.TryGetProperty("ocr_token", out var snakeProp) && snakeProp.ValueKind == JsonValueKind.String)
        {
            return snakeProp.GetString();
        }

        return null;
    }

    private static async Task<ServerExpensePolicyEvaluation> EvaluateServerExpensePolicyAsync(
        PaymentsDbContext dbContext,
        string callerUserId,
        ParticipantWallet wallet,
        string? rawCategory,
        string? rawMerchantName,
        decimal requestedGrossAmount,
        bool isMealAllowance,
        string? requestedExpenseDate,
        string? explicitOcrToken,
        string? receiptImageBase64,
        object? rawReceiptOcrData,
        string? gatewaySecret,
        DateTimeOffset now,
        string approvedSummaryNote,
        CancellationToken ct)
    {
        string normalizedCategory = string.IsNullOrWhiteSpace(rawCategory)
            ? "TRANSIT"
            : rawCategory.Trim().ToUpperInvariant();
        string trimmedMerchant = (rawMerchantName ?? string.Empty).Trim();

        decimal gross = Math.Round(requestedGrossAmount, 2);
        bool isMeal = string.Equals(normalizedCategory, "MEAL", StringComparison.OrdinalIgnoreCase) || isMealAllowance;
        decimal perDiemCap = ParticipantExpenseClaim.ServerMealPerDiemCap;
        decimal taxDeduction = ParticipantExpenseClaim.ComputeMealTaxDeduction(gross, isMeal);
        decimal net = Math.Round(gross - taxDeduction, 2);

        string? candidateToken = !string.IsNullOrWhiteSpace(explicitOcrToken)
            ? explicitOcrToken.Trim()
            : TryExtractOcrTokenFromObject(rawReceiptOcrData);
        byte[] receiptBytes = DecodeReceiptImageBytes(receiptImageBase64);

        var verificationHoldReasons = new List<string>();
        bool hasVerifiedScan = false;
        bool ocrReadable = false;
        string ocrMerchant = string.Empty;
        string ocrExpenseDate = string.Empty;
        decimal ocrGrossAmount = 0m;
        string ocrCategory = normalizedCategory;
        double rawOcrConfidence = 0.0;
        string? receiptSha256 = null;
        string? consumedScanId = null;
        bool fieldMismatch = false;
        bool duplicateReceipt = false;

        ReceiptOcrTokenPayload? verifiedToken = null;
        bool hasCandidateToken = !string.IsNullOrWhiteSpace(candidateToken);
        if (hasCandidateToken && TryVerifyReceiptOcrToken(candidateToken, gatewaySecret, callerUserId, now, out var parsedToken) && parsedToken is not null)
        {
            bool tokenAlreadyConsumed = await dbContext.PaymentAuditLogs.AsNoTracking()
                .AnyAsync(l => l.Action == OcrTokenConsumedAuditAction && l.EntityId == parsedToken.ScanId, ct)
                || dbContext.ChangeTracker.Entries<PaymentAuditLog>()
                    .Any(e => e.Entity.Action == OcrTokenConsumedAuditAction && e.Entity.EntityId == parsedToken.ScanId);
            if (tokenAlreadyConsumed)
            {
                verificationHoldReasons.Add("Receipt OCR scan token was already consumed by a prior expense submission.");
            }
            else
            {
                verifiedToken = parsedToken;
            }
        }
        else if (hasCandidateToken)
        {
            verificationHoldReasons.Add("Receipt OCR scan token could not be verified for this caller.");
        }

        if (receiptBytes.Length > 0)
        {
            string imageSha256 = HmacSigner.ComputeSha256Hash(receiptBytes);
            var extracted = ReceiptOcrEngine.Extract(
                rawText: null,
                imageBase64: receiptImageBase64,
                categoryHint: normalizedCategory);

            if (hasCandidateToken)
            {
                if (verifiedToken is null || !string.Equals(verifiedToken.ReceiptSha256, imageSha256, StringComparison.Ordinal))
                {
                    if (verifiedToken is not null)
                    {
                        verificationHoldReasons.Add("Uploaded receipt bytes do not match the server OCR scan token.");
                    }
                }
                else
                {
                    consumedScanId = verifiedToken.ScanId;
                    hasVerifiedScan = true;
                }
            }
            else
            {
                hasVerifiedScan = true;
            }

            receiptSha256 = imageSha256;
            ocrReadable = extracted.OcrReadable;
            ocrMerchant = extracted.MerchantName;
            ocrExpenseDate = extracted.ExpenseDate;
            ocrGrossAmount = extracted.GrossAmount;
            ocrCategory = extracted.Category;
            rawOcrConfidence = extracted.ConfidenceScore;
        }
        else if (verifiedToken is not null)
        {
            hasVerifiedScan = true;
            consumedScanId = verifiedToken.ScanId;
            receiptSha256 = verifiedToken.ReceiptSha256;
            ocrReadable = verifiedToken.OcrReadable;
            ocrMerchant = verifiedToken.MerchantName;
            ocrExpenseDate = verifiedToken.ExpenseDate;
            ocrGrossAmount = verifiedToken.GrossAmount;
            ocrCategory = verifiedToken.Category;
            rawOcrConfidence = verifiedToken.ConfidenceScore;
        }

        if (!string.IsNullOrEmpty(receiptSha256))
        {
            duplicateReceipt = await dbContext.PaymentAuditLogs.AsNoTracking()
                .AnyAsync(l => l.Action == ReceiptDigestRecordedAuditAction && l.EntityId == receiptSha256, ct)
                || dbContext.ChangeTracker.Entries<PaymentAuditLog>()
                    .Any(e => e.Entity.Action == ReceiptDigestRecordedAuditAction && e.Entity.EntityId == receiptSha256);
            if (duplicateReceipt)
            {
                verificationHoldReasons.Add("Duplicate receipt digest detected across expense claims.");
            }
        }

        double effectiveOcrConfidence = 0.0;
        if (hasVerifiedScan && verificationHoldReasons.Count == 0)
        {
            if (!ocrReadable)
            {
                verificationHoldReasons.Add("Receipt scan could not read legible merchant and total fields.");
            }
            else
            {
                bool amountMatches = Math.Round(gross, 2) == Math.Round(ocrGrossAmount, 2);
                bool merchantMatches = !string.IsNullOrWhiteSpace(ocrMerchant)
                    && string.Equals(trimmedMerchant, ocrMerchant.Trim(), StringComparison.OrdinalIgnoreCase);
                bool categoryMatches = string.Equals(normalizedCategory, ocrCategory, StringComparison.OrdinalIgnoreCase);
                bool dateMatches = string.IsNullOrWhiteSpace(ocrExpenseDate)
                    || string.IsNullOrWhiteSpace(requestedExpenseDate)
                    || string.Equals(requestedExpenseDate.Trim(), ocrExpenseDate.Trim(), StringComparison.Ordinal);

                if (amountMatches && merchantMatches && categoryMatches && dateMatches)
                {
                    effectiveOcrConfidence = rawOcrConfidence;
                }
                else
                {
                    fieldMismatch = true;
                    verificationHoldReasons.Add("Submitted claim fields differ from server-extracted receipt scan.");
                }
            }
        }

        bool isAmountLowRisk = gross <= 100.00m;
        bool isHighConfidence = effectiveOcrConfidence >= 0.90;
        bool isMealWithinCap = !isMeal || gross <= perDiemCap;
        bool isW9Compliant = !(wallet.TaxableYtd >= 600.00m && !string.Equals(wallet.W9Status, "VERIFIED", StringComparison.OrdinalIgnoreCase));

        bool canAutoApprove = isAmountLowRisk
            && isHighConfidence
            && isMealWithinCap
            && isW9Compliant
            && verificationHoldReasons.Count == 0;

        string initialStatus = canAutoApprove ? "APPROVED" : "PENDING_REVIEW";
        string approvalDecision = canAutoApprove ? "AUTO_APPROVED" : "PENDING_REVIEW";

        string reviewNotes;
        if (!canAutoApprove)
        {
            var holdReasons = new List<string>(verificationHoldReasons);
            if (!isAmountLowRisk)
            {
                holdReasons.Add($"Gross amount (${gross:F2}) exceeds $100.00 auto-approval threshold.");
            }

            if (!isMealWithinCap)
            {
                holdReasons.Add($"Meal expense (${gross:F2}) exceeds per-diem cap (${perDiemCap:F2}).");
            }

            if (!isW9Compliant)
            {
                holdReasons.Add("IRS $600.00 annual taxable threshold reached without verified Form W-9 certification.");
            }

            if (!isHighConfidence)
            {
                holdReasons.Add("We could not read every detail on the receipt photo clearly, so your study coordinator will check it for you.");
            }

            reviewNotes = string.Join(" ", holdReasons) + " Routed to site coordinator review queue.";
        }
        else
        {
            reviewNotes = approvedSummaryNote;
        }

        string resolvedExpenseDate = now.ToString("yyyy-MM-dd");
        if (!string.IsNullOrWhiteSpace(requestedExpenseDate))
        {
            resolvedExpenseDate = requestedExpenseDate.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(ocrExpenseDate))
        {
            resolvedExpenseDate = ocrExpenseDate;
        }

        string serverOcrJson = JsonSerializer.Serialize(new
        {
            ocr_readable = ocrReadable,
            merchant_name = ocrMerchant,
            expense_date = ocrExpenseDate,
            gross_amount = ocrGrossAmount,
            category = ocrCategory,
            confidence_score = effectiveOcrConfidence,
            raw_confidence_score = rawOcrConfidence,
            is_meal_allowance = isMeal,
            per_diem_cap = perDiemCap,
            receipt_sha256 = receiptSha256,
            scan_id = consumedScanId,
            duplicate_receipt = duplicateReceipt,
            field_mismatch = fieldMismatch
        });

        string? newReceiptSha256 = !string.IsNullOrEmpty(receiptSha256) && !duplicateReceipt
            ? receiptSha256
            : null;

        return new ServerExpensePolicyEvaluation(
            NormalizedCategory: normalizedCategory,
            MerchantName: trimmedMerchant,
            ResolvedExpenseDate: resolvedExpenseDate,
            GrossAmount: gross,
            TaxDeduction: taxDeduction,
            NetAmount: net,
            OcrConfidence: effectiveOcrConfidence,
            CanAutoApprove: canAutoApprove,
            InitialStatus: initialStatus,
            ApprovalDecision: approvalDecision,
            ReviewNotes: reviewNotes,
            ServerReceiptOcrJson: serverOcrJson,
            ConsumedScanId: consumedScanId,
            NewReceiptSha256: newReceiptSha256);
    }

    private static void RecordServerReceiptVerificationAuditLogs(
        PaymentsDbContext dbContext,
        ParticipantExpenseClaim claim,
        string callerUserId,
        string callerRolesText,
        ServerExpensePolicyEvaluation policy,
        DateTimeOffset now)
    {
        if (!string.IsNullOrEmpty(policy.ConsumedScanId))
        {
            dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantExpenseClaim",
                EntityId = policy.ConsumedScanId,
                Action = OcrTokenConsumedAuditAction,
                UserId = callerUserId,
                UserRoles = callerRolesText,
                Details = JsonSerializer.Serialize(new
                {
                    claim_id = claim.Id,
                    scan_id = policy.ConsumedScanId
                }),
                CreatedAt = now,
                CreatedBy = callerUserId,
                ReasonForChange = "Single-use receipt OCR token bound to expense claim",
                VersionIndex = 1
            });
        }

        if (!string.IsNullOrEmpty(policy.NewReceiptSha256))
        {
            dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantExpenseClaim",
                EntityId = policy.NewReceiptSha256,
                Action = ReceiptDigestRecordedAuditAction,
                UserId = callerUserId,
                UserRoles = callerRolesText,
                Details = JsonSerializer.Serialize(new
                {
                    claim_id = claim.Id,
                    receipt_sha256 = policy.NewReceiptSha256
                }),
                CreatedAt = now,
                CreatedBy = callerUserId,
                ReasonForChange = "Receipt image SHA-256 digest bound to expense claim",
                VersionIndex = 1
            });
        }
    }

    private static void ApplyAutoApprovedExpenseDisbursement(
        PaymentsDbContext dbContext,
        ParticipantWallet wallet,
        ParticipantExpenseClaim claim,
        string callerUserId,
        string callerRolesText,
        double ocrConfidence,
        DateTimeOffset now,
        bool auditAsSystemActor)
    {
        decimal net = claim.NetAmount;
        wallet.CurrentBalance += net;
        wallet.LifetimeReimbursements += net;
        wallet.UpdatedAt = now;
        wallet.VersionIndex += 1;

        dbContext.ParticipantTransactions.Add(new ParticipantTransaction
        {
            Id = Guid.NewGuid().ToString(),
            WalletId = wallet.Id,
            SubjectId = claim.SubjectId,
            StudyId = wallet.StudyId,
            TransactionType = "REIMBURSEMENT_CREDIT",
            Amount = net,
            BalanceAfter = wallet.CurrentBalance,
            ReferenceId = claim.Id,
            Description = $"Auto-approved reimbursement credit for {claim.Category} expense ({claim.MerchantName})",
            CreatedAt = now,
            CreatedBy = AutoApprovalSystemUser,
            VersionIndex = 1
        });

        string achRef = $"DISB-ACH-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        claim.RecordWalletCredit();
        claim.RecordDisbursement(achRef);

        wallet.CurrentBalance -= net;
        wallet.LifetimeDisbursements += net;

        var disbursement = new ParticipantDisbursement
        {
            Id = Guid.NewGuid().ToString(),
            WalletId = wallet.Id,
            SubjectId = claim.SubjectId,
            Amount = net,
            DisbursementType = "DIRECT_ACH",
            ReferenceNumber = achRef,
            Status = "DISPATCHED",
            CreatedAt = now,
            CreatedBy = AutoApprovalSystemUser,
            VersionIndex = 1
        };
        dbContext.ParticipantDisbursements.Add(disbursement);

        dbContext.ParticipantTransactions.Add(new ParticipantTransaction
        {
            Id = Guid.NewGuid().ToString(),
            WalletId = wallet.Id,
            SubjectId = claim.SubjectId,
            StudyId = wallet.StudyId,
            TransactionType = "DISBURSEMENT_DEBIT",
            Amount = -net,
            BalanceAfter = wallet.CurrentBalance,
            ReferenceId = claim.Id,
            Description = $"Automated direct ACH payout dispatch ({achRef})",
            CreatedAt = now,
            CreatedBy = AutoApprovalSystemUser,
            VersionIndex = 1
        });

        string auditUserId = auditAsSystemActor ? AutoApprovalSystemUser : callerUserId;
        string auditUserRoles = auditAsSystemActor ? string.Empty : callerRolesText;

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantExpenseClaim",
            EntityId = claim.Id,
            Action = "EXPENSE_CLAIM_AUTO_APPROVED",
            UserId = auditUserId,
            UserRoles = auditUserRoles,
            Details = JsonSerializer.Serialize(new
            {
                claim_id = claim.Id,
                submitted_by = callerUserId,
                approver = AutoApprovalSystemUser,
                gross_amount = claim.GrossAmount,
                net_amount = net,
                ocr_confidence = ocrConfidence,
                disbursement_reference = achRef
            }),
            CreatedAt = now,
            CreatedBy = auditUserId,
            ReasonForChange = "Automated OCR policy auto-approval and ACH payout dispatch",
            VersionIndex = claim.VersionIndex
        });

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = wallet.Id,
            Action = "DIGITAL_DISBURSEMENT_DISPATCHED",
            UserId = auditUserId,
            UserRoles = auditUserRoles,
            Details = JsonSerializer.Serialize(new
            {
                submitted_by = callerUserId,
                disbursement_id = disbursement.Id,
                amount = net,
                reference_number = achRef,
                disbursement_type = "DIRECT_ACH"
            }),
            CreatedAt = now,
            CreatedBy = auditUserId,
            ReasonForChange = "Automated direct ACH reimbursement dispatched to linked bank account",
            VersionIndex = wallet.VersionIndex
        });
    }

    private static Task<IResult> SubmitStaffExpenseClaim(
        SubmitExpenseClaimRequestDto request,
        PaymentsDbContext dbContext,
        [FromServices] IClock clock,
        [FromServices] IOptionsMonitor<GatewayAuthenticationOptions> gatewayAuthentication,
        HttpContext httpContext) =>
        SubmitExpenseClaimCore(
            request,
            dbContext,
            clock.UtcNow,
            ResolveConfiguredGatewaySecret(gatewayAuthentication),
            httpContext);

    internal static Task<IResult> SubmitExpenseClaim(
        SubmitExpenseClaimRequestDto request,
        PaymentsDbContext dbContext,
        HttpContext httpContext) =>
        SubmitExpenseClaimCore(
            request,
            dbContext,
            SystemClock.Instance.UtcNow,
            ResolveConfiguredGatewaySecret(gatewayAuthentication: null),
            httpContext);

    private static async Task<IResult> SubmitExpenseClaimCore(
        SubmitExpenseClaimRequestDto request,
        PaymentsDbContext dbContext,
        DateTimeOffset now,
        string? gatewaySecret,
        HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.SubjectId) || request.GrossAmount <= 0m)
        {
            return Results.BadRequest(new { detail = "subject_id and positive gross_amount are required." });
        }

        if (!Reaches(httpContext, request.StudyId, request.SiteId))
        {
            return OutsideScope();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);

        return await ExecuteWalletUnitOfWorkAsync(dbContext, async ct =>
        {
            var wallet = await LockWalletBySubjectAndStudyAsync(dbContext, request.SubjectId, request.StudyId, ct);

            if (wallet != null && !Reaches(httpContext, wallet))
            {
                return OutsideScope();
            }

            if (wallet == null)
            {
                wallet = new ParticipantWallet
                {
                    Id = Guid.NewGuid().ToString(),
                    SubjectId = request.SubjectId,
                    StudyId = request.StudyId,
                    SiteId = request.SiteId,
                    CurrentBalance = 0m,
                    LifetimeStipends = 0m,
                    LifetimeReimbursements = 0m,
                    LifetimeDisbursements = 0m,
                    Currency = "USD",
                    Status = "ACTIVE",
                    W9Status = "NOT_REQUIRED",
                    CreatedAt = now,
                    CreatedBy = userId,
                    UpdatedAt = now,
                    VersionIndex = 1
                };
                dbContext.ParticipantWallets.Add(wallet);
            }

            var policy = await EvaluateServerExpensePolicyAsync(
                dbContext,
                userId,
                wallet,
                rawCategory: request.Category,
                rawMerchantName: request.MerchantName,
                requestedGrossAmount: request.GrossAmount,
                isMealAllowance: request.IsMealAllowance,
                requestedExpenseDate: request.ExpenseDate,
                explicitOcrToken: request.OcrToken,
                receiptImageBase64: request.ReceiptImageBase64,
                rawReceiptOcrData: request.ReceiptOcrData,
                gatewaySecret: gatewaySecret,
                now: now,
                approvedSummaryNote: "Auto-approved based on low-risk expense policy (<$100, high OCR confidence).",
                ct: ct);

            var claim = new ParticipantExpenseClaim
            {
                Id = Guid.NewGuid().ToString(),
                WalletId = wallet.Id,
                SubjectId = request.SubjectId,
                StudyId = request.StudyId,
                SiteId = request.SiteId,
                ExpenseDate = policy.ResolvedExpenseDate,
                Category = policy.NormalizedCategory,
                MerchantName = policy.MerchantName,
                GrossAmount = policy.GrossAmount,
                TaxDeduction = policy.TaxDeduction,
                NetAmount = policy.NetAmount,
                Status = policy.InitialStatus,
                ReceiptImageName = !string.IsNullOrWhiteSpace(request.ReceiptImageName) ? request.ReceiptImageName : "receipt.jpg",
                ReceiptOcrDataJson = policy.ServerReceiptOcrJson,
                ReviewedBy = policy.CanAutoApprove ? AutoApprovalSystemUser : null,
                ReviewedAt = policy.CanAutoApprove ? now : null,
                ReviewNotes = policy.ReviewNotes,
                CreatedAt = now,
                CreatedBy = userId,
                UpdatedAt = now,
                VersionIndex = 1
            };

            RecordServerReceiptVerificationAuditLogs(dbContext, claim, userId, rolesText, policy, now);

            if (policy.CanAutoApprove)
            {
                ApplyAutoApprovedExpenseDisbursement(
                    dbContext,
                    wallet,
                    claim,
                    userId,
                    rolesText,
                    policy.OcrConfidence,
                    now,
                    auditAsSystemActor: true);
            }
            else
            {
                dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantExpenseClaim",
                    EntityId = claim.Id,
                    Action = "EXPENSE_CLAIM_SUBMITTED",
                    UserId = userId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new
                    {
                        category = claim.Category,
                        gross = policy.GrossAmount,
                        net = policy.NetAmount,
                        merchant = claim.MerchantName,
                        ocr_confidence = policy.OcrConfidence,
                        status = claim.Status,
                        review_notes = policy.ReviewNotes
                    }),
                    CreatedAt = now,
                    CreatedBy = userId,
                    ReasonForChange = "Travel/meal expense claim submitted for coordinator review",
                    VersionIndex = 1
                });
            }

            dbContext.ParticipantExpenseClaims.Add(claim);
            await dbContext.SaveChangesAsync(ct);

            return Results.Ok(claim);
        });
    }

    private static Task<IResult> ExtractStaffReceiptOcr(
        OcrExtractRequestDto request,
        PaymentsDbContext dbContext,
        [FromServices] IClock clock,
        [FromServices] IOptionsMonitor<GatewayAuthenticationOptions> gatewayAuthentication,
        HttpContext httpContext) =>
        ExtractReceiptOcrCore(
            request,
            dbContext,
            clock.UtcNow,
            ResolveConfiguredGatewaySecret(gatewayAuthentication),
            httpContext);

    internal static Task<IResult> ExtractReceiptOcr(
        OcrExtractRequestDto request,
        PaymentsDbContext dbContext,
        HttpContext httpContext) =>
        ExtractReceiptOcrCore(
            request,
            dbContext,
            SystemClock.Instance.UtcNow,
            ResolveConfiguredGatewaySecret(gatewayAuthentication: null),
            httpContext);

    private static async Task<IResult> ExtractReceiptOcrCore(
        OcrExtractRequestDto request,
        PaymentsDbContext dbContext,
        DateTimeOffset now,
        string? gatewaySecret,
        HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        if (!ClinicalScopeGuard.HasValidSiteScopeIfRequired(ClinicalScopeGuard.ResolvePrincipal(httpContext)))
        {
            return OutsideScope();
        }

        byte[] receiptBytes = DecodeReceiptImageBytes(request.ImageBase64);
        var extracted = ReceiptOcrEngine.Extract(
            rawText: receiptBytes.Length > 0 ? null : request.RawText,
            imageBase64: request.ImageBase64,
            categoryHint: request.CategoryHint);

        string? receiptSha256 = receiptBytes.Length > 0 ? HmacSigner.ComputeSha256Hash(receiptBytes) : null;
        string scanId = "OCR_SCAN_" + Guid.NewGuid().ToString("N");
        string? ocrToken = receiptSha256 is not null
            && string.IsNullOrEmpty(request.RawText)
            && extracted.OcrReadable
            && !string.IsNullOrWhiteSpace(gatewaySecret)
            ? IssueReceiptOcrToken(new ReceiptOcrTokenPayload(
                ScanId: scanId,
                CallerUserId: userId,
                ReceiptSha256: receiptSha256,
                OcrReadable: extracted.OcrReadable,
                MerchantName: extracted.MerchantName,
                ExpenseDate: extracted.ExpenseDate,
                GrossAmount: extracted.GrossAmount,
                Category: extracted.Category,
                ConfidenceScore: extracted.ConfidenceScore,
                IssuedAtUnix: now.ToUnixTimeSeconds(),
                ExpiresAtUnix: now.Add(OcrTokenLifetime).ToUnixTimeSeconds()), gatewaySecret)
            : null;

        var ocrAudit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantExpenseClaim",
            EntityId = scanId,
            Action = "OCR_RECEIPT_EXTRACTED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
            Details = JsonSerializer.Serialize(new
            {
                ocr_readable = extracted.OcrReadable,
                merchant_name = extracted.MerchantName,
                expense_date = extracted.ExpenseDate,
                gross_amount = extracted.GrossAmount,
                tax_deduction = extracted.TaxDeduction,
                net_amount = extracted.NetAmount,
                category = extracted.Category,
                confidence_score = extracted.ConfidenceScore,
                receipt_sha256 = receiptSha256,
                filename = request.Filename
            }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "Receipt OCR scan performed",
            VersionIndex = 1
        };
        dbContext.PaymentAuditLogs.Add(ocrAudit);
        await dbContext.SaveChangesAsync();

        return Results.Ok(new ReceiptOcrExtractResponseDto(
            OcrReadable: extracted.OcrReadable,
            MerchantName: extracted.MerchantName,
            ExpenseDate: extracted.ExpenseDate,
            GrossAmount: extracted.GrossAmount,
            TaxDeduction: extracted.TaxDeduction,
            NetAmount: extracted.NetAmount,
            Category: extracted.Category,
            ConfidenceScore: extracted.ConfidenceScore,
            RawText: extracted.RawText,
            Warning: extracted.Warning,
            OcrToken: ocrToken,
            ReceiptSha256: receiptSha256
        ));
    }

    private static ProblemHttpResult CrossParticipantAccessDenied() =>
        ClinicalScopeGuard.Forbidden("Participants may only access or mutate their own wallet.");

    private static async Task<(ParticipantWallet? Wallet, IResult? ErrorResult)> ResolveOwnWalletAsync(
        PaymentsDbContext dbContext,
        HttpContext httpContext,
        Principal caller,
        string? requestedSubjectId,
        string? requestedWalletId,
        string? requestedStudyId,
        string? requestedSiteId,
        bool createIfMissing,
        DateTimeOffset now,
        bool lockForUpdate = false,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(requestedSubjectId)
            && !string.Equals(requestedSubjectId.Trim(), caller.UserId, StringComparison.Ordinal))
        {
            return (null, CrossParticipantAccessDenied());
        }

        if (!ClinicalScopeGuard.HasValidSiteScopeIfRequired(caller))
        {
            return (null, OutsideScope());
        }

        if (!string.IsNullOrWhiteSpace(requestedSiteId) && !ClinicalScopeGuard.CanAccessSite(httpContext, requestedSiteId.Trim()))
        {
            return (null, OutsideScope());
        }

        if (!string.IsNullOrWhiteSpace(requestedWalletId))
        {
            string trimmedWalletId = requestedWalletId.Trim();
            var byId = lockForUpdate
                ? await LockWalletByIdAsync(dbContext, trimmedWalletId, ct)
                : await dbContext.ParticipantWallets.FirstOrDefaultAsync(w => w.Id == trimmedWalletId, ct);
            if (byId == null)
            {
                return (null, Results.NotFound(new { detail = $"Participant wallet '{requestedWalletId}' not found." }));
            }
            if (!string.Equals(byId.SubjectId, caller.UserId, StringComparison.Ordinal))
            {
                return (null, CrossParticipantAccessDenied());
            }
            if (!Reaches(httpContext, byId))
            {
                return (null, OutsideScope());
            }
            return (byId, null);
        }

        var loadedWallets = lockForUpdate
            ? await LockWalletsBySubjectAsync(dbContext, caller.UserId, ct)
            : await dbContext.ParticipantWallets.Where(w => w.SubjectId == caller.UserId).ToListAsync(ct);
        var ownWallets = loadedWallets
            .OrderByDescending(w => w.UpdatedAt)
            .ToList();

        if (!string.IsNullOrWhiteSpace(requestedStudyId))
        {
            if (!ClinicalScopeGuard.CanAccessStudy(httpContext, requestedStudyId))
            {
                return (null, OutsideScope());
            }
            var studyMatch = ownWallets.FirstOrDefault(w => string.Equals(w.StudyId, requestedStudyId, StringComparison.Ordinal));
            if (studyMatch != null)
            {
                return Reaches(httpContext, studyMatch) ? (studyMatch, null) : (null, OutsideScope());
            }
        }
        else if (ownWallets.Count > 0)
        {
            var scopedMatch = ownWallets.FirstOrDefault(w => Reaches(httpContext, w));
            if (scopedMatch == null)
            {
                return (null, OutsideScope());
            }
            return (scopedMatch, null);
        }

        if (!createIfMissing)
        {
            return (null, null);
        }

        string? resolvedStudy = !string.IsNullOrWhiteSpace(requestedStudyId)
            ? requestedStudyId.Trim()
            : caller.AssignedStudies.Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s != "*");
        if (string.IsNullOrWhiteSpace(resolvedStudy) || !ClinicalScopeGuard.CanAccessStudy(httpContext, resolvedStudy))
        {
            return (null, OutsideScope());
        }

        string resolvedSite = !string.IsNullOrWhiteSpace(requestedSiteId)
            ? requestedSiteId.Trim()
            : (caller.AssignedSites.Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "SITE-DEFAULT");

        if (!Reaches(httpContext, resolvedStudy, resolvedSite))
        {
            return (null, OutsideScope());
        }

        var provisioned = new ParticipantWallet
        {
            Id = Guid.NewGuid().ToString(),
            SubjectId = caller.UserId,
            StudyId = resolvedStudy,
            SiteId = resolvedSite,
            CurrentBalance = 0m,
            LifetimeStipends = 0m,
            LifetimeReimbursements = 0m,
            LifetimeDisbursements = 0m,
            Currency = "USD",
            Status = "ACTIVE",
            W9Status = "NOT_REQUIRED",
            VirtualCardMask = "**** **** **** 4821",
            VirtualCardExpiry = "12/29",
            AchRoutingMask = "*****0021",
            AchAccountMask = "ACH-***4321",
            AchAccountHolder = $"Participant {caller.UserId}",
            CreatedAt = now,
            CreatedBy = caller.UserId,
            UpdatedAt = now,
            VersionIndex = 1
        };
        dbContext.ParticipantWallets.Add(provisioned);

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = provisioned.Id,
            Action = "PARTICIPANT_WALLET_CREATED",
            UserId = caller.UserId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
            Details = JsonSerializer.Serialize(new
            {
                subject_id = caller.UserId,
                study_id = resolvedStudy,
                site_id = resolvedSite
            }),
            CreatedAt = now,
            CreatedBy = caller.UserId,
            ReasonForChange = "Participant study wallet provisioned for self-service workflow",
            VersionIndex = 1
        });

        return (provisioned, null);
    }

    private static async Task<IResult> GetSelfParticipantWallet(
        PaymentsDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext,
        string? subjectId = null,
        string? walletId = null,
        string? studyId = null)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return NoSignedCaller();
        }

        DateTimeOffset now = clock.UtcNow;
        var (wallet, error) = await ResolveOwnWalletAsync(
            dbContext,
            httpContext,
            caller,
            requestedSubjectId: subjectId,
            requestedWalletId: walletId,
            requestedStudyId: studyId,
            requestedSiteId: null,
            createIfMissing: false,
            now: now);

        if (error != null)
        {
            return error;
        }

        if (wallet == null)
        {
            string? fallbackStudy = !string.IsNullOrWhiteSpace(studyId)
                ? studyId.Trim()
                : caller.AssignedStudies.Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s != "*");
            if (string.IsNullOrWhiteSpace(fallbackStudy)
                && !caller.AssignedStudies.Any(s => string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal)))
            {
                return OutsideScope();
            }
            if (!string.IsNullOrWhiteSpace(fallbackStudy) && !ClinicalScopeGuard.CanAccessStudy(httpContext, fallbackStudy))
            {
                return OutsideScope();
            }

            string emptyStudy = fallbackStudy ?? "UNASSIGNED";
            string emptySite = caller.AssignedSites.Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "SITE-DEFAULT";
            if (!Reaches(httpContext, emptyStudy, emptySite))
            {
                return OutsideScope();
            }

            var emptyProjection = new WalletResponse(
                Id: $"unprovisioned:{caller.UserId}",
                SubjectId: caller.UserId,
                StudyId: emptyStudy,
                SiteId: emptySite,
                CurrentBalance: 0m,
                LifetimeStipends: 0m,
                LifetimeReimbursements: 0m,
                LifetimeDisbursements: 0m,
                TaxableYtd: 0m,
                Is1099ThresholdExceeded: false,
                W9Status: "NOT_REQUIRED",
                Currency: "USD",
                Status: "ACTIVE",
                VirtualCardMask: "**** **** **** 4821",
                VirtualCardExpiry: "12/29",
                AchRoutingNumber: "*****0021",
                AchAccountMask: "ACH-***4321",
                AchAccountHolder: $"Participant {caller.UserId}",
                AntiCoercionCap: null,
                AutoDisbursementThreshold: null,
                UpdatedAt: now,
                CardType: "RELOADABLE_DEBIT",
                CardStatus: "ACTIVE",
                AchRoutingMask: "*****0021"
            );

            return Results.Ok(new SelfServiceWalletSummaryResponseDto(emptyProjection, [], []));
        }

        var recentTransactions = (await dbContext.ParticipantTransactions.AsNoTracking()
            .Where(t => t.WalletId == wallet.Id && t.SubjectId == caller.UserId)
            .ToListAsync())
            .OrderByDescending(t => t.CreatedAt)
            .Take(25)
            .ToList();

        var claims = (await dbContext.ParticipantExpenseClaims.AsNoTracking()
            .Where(c => c.WalletId == wallet.Id && c.SubjectId == caller.UserId)
            .ToListAsync())
            .OrderByDescending(c => c.CreatedAt)
            .Take(25)
            .ToList();

        return Results.Ok(new SelfServiceWalletSummaryResponseDto(
            Wallet: MapWalletToResponse(wallet),
            RecentTransactions: recentTransactions,
            ExpenseClaims: claims
        ));
    }

    private static Task<IResult> ExtractSelfReceiptOcr(
        OcrExtractRequestDto request,
        PaymentsDbContext dbContext,
        [FromServices] IClock clock,
        [FromServices] IOptionsMonitor<GatewayAuthenticationOptions> gatewayAuthentication,
        HttpContext httpContext) =>
        ExtractReceiptOcrCore(
            request,
            dbContext,
            clock.UtcNow,
            ResolveConfiguredGatewaySecret(gatewayAuthentication),
            httpContext);

    private static async Task<IResult> SubmitSelfExpenseClaim(
        SelfServiceSubmitExpenseRequestDto request,
        PaymentsDbContext dbContext,
        [FromServices] IClock clock,
        [FromServices] IOptionsMonitor<GatewayAuthenticationOptions> gatewayAuthentication,
        HttpContext httpContext,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader = null)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        string? idempotencyKey = NormalizeIdempotencyKey(idempotencyKeyHeader, request.IdempotencyKey);

        if (idempotencyKey is { Length: > 255 })
        {
            return Results.BadRequest(new { detail = "Idempotency-Key must not exceed 255 characters." });
        }

        DateTimeOffset now = clock.UtcNow;
        string? resolvedWalletId = null;

        try
        {
            return await ExecuteWalletUnitOfWorkAsync(dbContext, async ct =>
            {
                var (wallet, error) = await ResolveOwnWalletAsync(
                    dbContext,
                    httpContext,
                    caller,
                    requestedSubjectId: request.SubjectId,
                    requestedWalletId: request.WalletId,
                    requestedStudyId: request.StudyCode,
                    requestedSiteId: request.SiteCode,
                    createIfMissing: true,
                    now: now,
                    lockForUpdate: true,
                    ct: ct);

                if (error != null)
                {
                    return error;
                }

                if (wallet == null)
                {
                    return OutsideScope();
                }

                resolvedWalletId = wallet.Id;

                if (!string.IsNullOrEmpty(idempotencyKey))
                {
                    var existingClaim = await dbContext.ParticipantExpenseClaims.AsNoTracking()
                        .FirstOrDefaultAsync(c =>
                            c.WalletId == wallet.Id
                            && c.SubjectId == caller.UserId
                            && c.IdempotencyKey == idempotencyKey, ct);
                    if (existingClaim != null)
                    {
                        return BuildSelfServiceExpenseClaimResponse(existingClaim, wallet, caller.UserId);
                    }
                }

                if (request.GrossAmount <= 0m)
                {
                    return Results.BadRequest(new { detail = "Expense gross amount must be greater than zero." });
                }

                if (string.IsNullOrWhiteSpace(request.MerchantName))
                {
                    return Results.BadRequest(new { detail = "Merchant name is required to submit an expense claim." });
                }

                var policy = await EvaluateServerExpensePolicyAsync(
                    dbContext,
                    caller.UserId,
                    wallet,
                    rawCategory: request.Category,
                    rawMerchantName: request.MerchantName,
                    requestedGrossAmount: request.GrossAmount,
                    isMealAllowance: request.IsMealAllowance,
                    requestedExpenseDate: request.ExpenseDate,
                    explicitOcrToken: request.OcrToken,
                    receiptImageBase64: request.ReceiptImageBase64,
                    rawReceiptOcrData: request.ReceiptOcrData,
                    gatewaySecret: ResolveConfiguredGatewaySecret(gatewayAuthentication),
                    now: now,
                    approvedSummaryNote: "Auto-approved under participant expense policy and dispatched via direct ACH.",
                    ct: ct);

                var claim = new ParticipantExpenseClaim
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = caller.UserId,
                    StudyId = wallet.StudyId,
                    SiteId = wallet.SiteId,
                    ExpenseDate = policy.ResolvedExpenseDate,
                    Category = policy.NormalizedCategory,
                    MerchantName = policy.MerchantName,
                    GrossAmount = policy.GrossAmount,
                    TaxDeduction = policy.TaxDeduction,
                    NetAmount = policy.NetAmount,
                    Status = policy.InitialStatus,
                    ReceiptImageName = !string.IsNullOrWhiteSpace(request.ReceiptImageName) ? request.ReceiptImageName : "receipt.png",
                    ReceiptOcrDataJson = policy.ServerReceiptOcrJson,
                    IdempotencyKey = idempotencyKey,
                    ReviewedBy = policy.CanAutoApprove ? AutoApprovalSystemUser : null,
                    ReviewedAt = policy.CanAutoApprove ? now : null,
                    ReviewNotes = policy.ReviewNotes,
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    UpdatedAt = now,
                    VersionIndex = 1
                };

                RecordServerReceiptVerificationAuditLogs(dbContext, claim, caller.UserId, rolesText, policy, now);

                if (policy.CanAutoApprove)
                {
                    ApplyAutoApprovedExpenseDisbursement(
                        dbContext,
                        wallet,
                        claim,
                        caller.UserId,
                        rolesText,
                        policy.OcrConfidence,
                        now,
                        auditAsSystemActor: false);
                }
                else
                {
                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantExpenseClaim",
                        EntityId = claim.Id,
                        Action = "EXPENSE_CLAIM_SUBMITTED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            submitted_by = caller.UserId,
                            category = claim.Category,
                            gross = policy.GrossAmount,
                            net = policy.NetAmount,
                            merchant = claim.MerchantName,
                            ocr_confidence = policy.OcrConfidence,
                            status = claim.Status,
                            review_notes = policy.ReviewNotes
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Participant expense claim held for coordinator review",
                        VersionIndex = 1
                    });
                }

                dbContext.ParticipantExpenseClaims.Add(claim);
                await dbContext.SaveChangesAsync(ct);

                return Results.Ok(new SelfServiceExpenseClaimResponseDto(
                    Id: claim.Id,
                    WalletId: wallet.Id,
                    SubjectId: caller.UserId,
                    StudyCode: wallet.StudyId,
                    SiteCode: wallet.SiteId,
                    ExpenseDate: claim.ExpenseDate,
                    Category: claim.Category,
                    MerchantName: claim.MerchantName,
                    GrossAmount: claim.GrossAmount,
                    TaxDeduction: claim.TaxDeduction,
                    NetAmount: claim.NetAmount,
                    Status: claim.Status,
                    ApprovalDecision: policy.ApprovalDecision,
                    AutoApproved: policy.CanAutoApprove,
                    DisbursementReference: claim.DisbursementReference,
                    ReviewNotes: claim.ReviewNotes,
                    ReceiptImageName: claim.ReceiptImageName,
                    WalletCurrentBalance: wallet.CurrentBalance,
                    LifetimeStipends: wallet.LifetimeStipends,
                    LifetimeReimbursements: wallet.LifetimeReimbursements,
                    LifetimeDisbursements: wallet.LifetimeDisbursements,
                    CreatedAt: claim.CreatedAt
                ));
            });
        }
        catch (DbUpdateException) when (!string.IsNullOrEmpty(idempotencyKey))
        {
            dbContext.ChangeTracker.Clear();
            var racedWallet = resolvedWalletId is not null
                ? await dbContext.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == resolvedWalletId)
                : await dbContext.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.SubjectId == caller.UserId);
            if (racedWallet != null)
            {
                var racedClaim = await dbContext.ParticipantExpenseClaims.AsNoTracking()
                    .FirstOrDefaultAsync(c =>
                        c.WalletId == racedWallet.Id
                        && c.SubjectId == caller.UserId
                        && c.IdempotencyKey == idempotencyKey);
                if (racedClaim != null)
                {
                    return BuildSelfServiceExpenseClaimResponse(racedClaim, racedWallet, caller.UserId);
                }
            }
            throw;
        }
    }

    private static IResult BuildSelfServiceExpenseClaimResponse(
        ParticipantExpenseClaim existingClaim,
        ParticipantWallet wallet,
        string subjectId)
    {
        bool wasAutoApproved = !string.IsNullOrEmpty(existingClaim.DisbursementReference)
            && string.Equals(
                existingClaim.Status,
                ParticipantExpenseClaimStatus.APPROVED.ToString(),
                StringComparison.OrdinalIgnoreCase);
        string existingDecision = wasAutoApproved ? "AUTO_APPROVED" : existingClaim.Status;

        return Results.Ok(new SelfServiceExpenseClaimResponseDto(
            Id: existingClaim.Id,
            WalletId: wallet.Id,
            SubjectId: subjectId,
            StudyCode: wallet.StudyId,
            SiteCode: wallet.SiteId,
            ExpenseDate: existingClaim.ExpenseDate,
            Category: existingClaim.Category,
            MerchantName: existingClaim.MerchantName,
            GrossAmount: existingClaim.GrossAmount,
            TaxDeduction: existingClaim.TaxDeduction,
            NetAmount: existingClaim.NetAmount,
            Status: existingClaim.Status,
            ApprovalDecision: existingDecision,
            AutoApproved: wasAutoApproved,
            DisbursementReference: existingClaim.DisbursementReference,
            ReviewNotes: existingClaim.ReviewNotes,
            ReceiptImageName: existingClaim.ReceiptImageName,
            WalletCurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            LifetimeReimbursements: wallet.LifetimeReimbursements,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            CreatedAt: existingClaim.CreatedAt
        ));
    }

    private static async Task<IResult> DisburseSelfWalletBalance(
        SelfServiceDisburseRequestDto request,
        PaymentsDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader = null)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        string? idempotencyKey = NormalizeIdempotencyKey(idempotencyKeyHeader, request.IdempotencyKey);

        if (idempotencyKey is { Length: > 255 })
        {
            return Results.BadRequest(new { detail = "Idempotency-Key must not exceed 255 characters." });
        }

        DateTimeOffset now = clock.UtcNow;
        string? resolvedWalletId = null;

        try
        {
            return await ExecuteWalletUnitOfWorkAsync(dbContext, async ct =>
            {
                var (wallet, error) = await ResolveOwnWalletAsync(
                    dbContext,
                    httpContext,
                    caller,
                    requestedSubjectId: request.SubjectId,
                    requestedWalletId: request.WalletId,
                    requestedStudyId: null,
                    requestedSiteId: null,
                    createIfMissing: false,
                    now: now,
                    lockForUpdate: true,
                    ct: ct);

                if (error != null)
                {
                    return error;
                }

                if (wallet == null)
                {
                    return Results.NotFound(new { detail = "No active participant wallet found for the signed-in participant." });
                }

                resolvedWalletId = wallet.Id;

                if (!string.IsNullOrEmpty(idempotencyKey))
                {
                    var existingDisbursement = await dbContext.ParticipantDisbursements.AsNoTracking()
                        .FirstOrDefaultAsync(d =>
                            d.WalletId == wallet.Id
                            && d.SubjectId == caller.UserId
                            && d.IdempotencyKey == idempotencyKey, ct);
                    if (existingDisbursement != null)
                    {
                        return BuildSelfServiceDisburseResponse(existingDisbursement, wallet, caller.UserId);
                    }
                }

                if (wallet.TaxableYtd >= 600.00m && !string.Equals(wallet.W9Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
                {
                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "PAYOUT_HALTED_W9_REQUIRED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            wallet_id = wallet.Id,
                            subject_id = caller.UserId,
                            taxable_ytd = wallet.TaxableYtd,
                            w9_status = wallet.W9Status
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Self-service payout halted pending Form W-9 verification",
                        VersionIndex = wallet.VersionIndex
                    });
                    await dbContext.SaveChangesAsync(ct);

                    return Results.Conflict(new
                    {
                        detail = "Annual taxable payments have reached $600.00 without a verified Form W-9. Automated payouts are halted until your Form W-9 is verified by site staff."
                    });
                }

                decimal payoutAmount = request.Amount.HasValue && request.Amount.Value > 0m
                    ? Math.Round(request.Amount.Value, 2)
                    : wallet.CurrentBalance;

                if (payoutAmount <= 0m)
                {
                    return Results.Conflict(new { detail = "Available wallet balance is $0.00; there are no funds to disburse." });
                }

                if (payoutAmount > wallet.CurrentBalance)
                {
                    return Results.BadRequest(new { detail = $"Requested payout (${payoutAmount:F2}) exceeds available wallet balance (${wallet.CurrentBalance:F2})." });
                }

                string disbursementType = string.Equals(request.DisbursementType, "VIRTUAL_CARD", StringComparison.OrdinalIgnoreCase)
                    ? "VIRTUAL_CARD"
                    : "DIRECT_ACH";

                string refPrefix = disbursementType == "DIRECT_ACH" ? "ACH" : "VCARD";
                string refNumber = $"DISB-{refPrefix}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
                string destinationMask = disbursementType == "DIRECT_ACH"
                    ? (MaskAccountIdentifier(wallet.AchAccountMask) ?? "ACH-***4321")
                    : (MaskCardIdentifier(wallet.VirtualCardMask) ?? "**** **** **** 4821");

                wallet.CurrentBalance -= payoutAmount;
                wallet.LifetimeDisbursements += payoutAmount;
                wallet.UpdatedAt = now;
                wallet.VersionIndex += 1;

                var disbursement = new ParticipantDisbursement
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = caller.UserId,
                    Amount = payoutAmount,
                    DisbursementType = disbursementType,
                    ReferenceNumber = refNumber,
                    IdempotencyKey = idempotencyKey,
                    Status = "DISPATCHED",
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    VersionIndex = 1
                };
                dbContext.ParticipantDisbursements.Add(disbursement);

                dbContext.ParticipantTransactions.Add(new ParticipantTransaction
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = caller.UserId,
                    StudyId = wallet.StudyId,
                    TransactionType = "DISBURSEMENT_DEBIT",
                    Amount = -payoutAmount,
                    BalanceAfter = wallet.CurrentBalance,
                    ReferenceId = disbursement.Id,
                    Description = $"Participant on-demand balance payout via {disbursementType} ({refNumber})",
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    VersionIndex = 1
                });

                dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantWallet",
                    EntityId = wallet.Id,
                    Action = "DIGITAL_DISBURSEMENT_DISPATCHED",
                    UserId = caller.UserId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new
                    {
                        disbursement_id = disbursement.Id,
                        subject_id = caller.UserId,
                        amount = payoutAmount,
                        new_balance = wallet.CurrentBalance,
                        reference_number = refNumber,
                        disbursement_type = disbursementType,
                        destination_mask = destinationMask
                    }),
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    ReasonForChange = "Participant triggered on-demand wallet balance payout",
                    VersionIndex = wallet.VersionIndex
                });

                await dbContext.SaveChangesAsync(ct);

                return BuildSelfServiceDisburseResponse(disbursement, wallet, caller.UserId, destinationMask);
            });
        }
        catch (DbUpdateException) when (!string.IsNullOrEmpty(idempotencyKey))
        {
            dbContext.ChangeTracker.Clear();
            var racedWallet = resolvedWalletId is not null
                ? await dbContext.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == resolvedWalletId)
                : await dbContext.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.SubjectId == caller.UserId);
            if (racedWallet != null)
            {
                var racedDisbursement = await dbContext.ParticipantDisbursements.AsNoTracking()
                    .FirstOrDefaultAsync(d =>
                        d.WalletId == racedWallet.Id
                        && d.SubjectId == caller.UserId
                        && d.IdempotencyKey == idempotencyKey);
                if (racedDisbursement != null)
                {
                    return BuildSelfServiceDisburseResponse(racedDisbursement, racedWallet, caller.UserId);
                }
            }
            throw;
        }
    }

    private static string? NormalizeIdempotencyKey(string? headerValue, string? bodyValue)
    {
        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(bodyValue))
        {
            return bodyValue.Trim();
        }

        return null;
    }

    private static IResult BuildSelfServiceDisburseResponse(
        ParticipantDisbursement disbursement,
        ParticipantWallet wallet,
        string subjectId,
        string? explicitDestinationMask = null)
    {
        string destinationMask = explicitDestinationMask ?? (
            string.Equals(disbursement.DisbursementType, "VIRTUAL_CARD", StringComparison.OrdinalIgnoreCase)
                ? (MaskCardIdentifier(wallet.VirtualCardMask) ?? "**** **** **** 4821")
                : (MaskAccountIdentifier(wallet.AchAccountMask) ?? "ACH-***4321"));

        return Results.Ok(new SelfServiceDisburseResponseDto(
            Id: disbursement.Id,
            WalletId: wallet.Id,
            SubjectId: subjectId,
            Amount: disbursement.Amount,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            Currency: wallet.Currency,
            DisbursementType: disbursement.DisbursementType,
            DestinationMask: destinationMask,
            ReferenceNumber: disbursement.ReferenceNumber,
            Status: disbursement.Status,
            CreatedAt: disbursement.CreatedAt
        ));
    }

    private static string? ResolveRequestedDiarySyncToken(SelfServiceDiaryStipendRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.SyncVerificationToken))
        {
            return request.SyncVerificationToken.Trim();
        }

        if (request.AdditionalFields is { Count: > 0 } extra
            && extra.TryGetValue("sync_verification_token", out var snakeToken)
            && snakeToken.ValueKind == JsonValueKind.String)
        {
            string? raw = snakeToken.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw.Trim();
            }
        }

        return null;
    }

    private static void RecordConsumedDiarySyncTokenAuditLog(
        PaymentsDbContext dbContext,
        ParticipantWallet wallet,
        string callerUserId,
        string callerRolesText,
        string tokenId,
        string idempotencyKey,
        string diaryCode,
        DateTimeOffset now)
    {
        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = tokenId,
            Action = DiarySyncTokenConsumedAuditAction,
            UserId = callerUserId,
            UserRoles = callerRolesText,
            Details = JsonSerializer.Serialize(new
            {
                wallet_id = wallet.Id,
                token_id = tokenId,
                idempotency_key = idempotencyKey,
                diary_id = diaryCode
            }),
            CreatedAt = now,
            CreatedBy = callerUserId,
            ReasonForChange = "Single-use ePRO diary sync verification token bound to stipend evaluation",
            VersionIndex = 1
        });
    }

    private static IResult BuildFlaggedDiaryStipendResponse(
        ParticipantWallet wallet,
        string subjectId,
        string idempotencyKey,
        string reason,
        string? w9Status = null) =>
        Results.Ok(new SelfServiceDiaryStipendResponseDto(
            Success: false,
            Status: "FLAGGED_FOR_REVIEW",
            CreditedAmount: 0m,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            WalletId: wallet.Id,
            SubjectId: subjectId,
            Currency: wallet.Currency,
            TransactionId: null,
            ReferenceNumber: null,
            IdempotencyKey: idempotencyKey,
            Reason: reason,
            RequiresReview: true,
            W9Status: w9Status
        ));

    private static async Task<IResult> CreditSelfDiaryStipend(
        SelfServiceDiaryStipendRequestDto request,
        PaymentsDbContext dbContext,
        [FromServices] IClock clock,
        [FromServices] IOptionsMonitor<GatewayAuthenticationOptions> gatewayAuthentication,
        HttpContext httpContext)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return NoSignedCaller();
        }

        if (!string.IsNullOrWhiteSpace(request.SubjectId)
            && !string.Equals(request.SubjectId.Trim(), caller.UserId, StringComparison.Ordinal))
        {
            return CrossParticipantAccessDenied();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        string fallbackDiaryKey = $"diary_{caller.UserId}_{request.AssignmentId ?? request.DiaryId}_{request.SequenceNumber ?? 0}";
        string? suppliedKey = NormalizeIdempotencyKey(
            httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault(),
            request.IdempotencyKey);
        string idempotencyKey = suppliedKey ?? fallbackDiaryKey;
        string diaryReference = (!string.IsNullOrWhiteSpace(request.AssignmentId) || request.SequenceNumber.HasValue)
            ? fallbackDiaryKey
            : idempotencyKey;
        if (idempotencyKey.Length > 255 || diaryReference.Length > 255)
        {
            return Results.BadRequest(new { detail = "Idempotency-Key must not exceed 255 characters." });
        }

        DateTimeOffset now = clock.UtcNow;
        string? gatewaySecret = ResolveConfiguredGatewaySecret(gatewayAuthentication);
        string? resolvedWalletId = null;

        try
        {
            return await ExecuteWalletUnitOfWorkAsync(dbContext, async ct =>
            {
                var (wallet, error) = await ResolveOwnWalletAsync(
                    dbContext,
                    httpContext,
                    caller,
                    requestedSubjectId: request.SubjectId,
                    requestedWalletId: request.WalletId,
                    requestedStudyId: request.StudyCode,
                    requestedSiteId: request.SiteCode,
                    createIfMissing: false,
                    now: now,
                    lockForUpdate: true,
                    ct: ct);

                if (error != null)
                {
                    return error;
                }

                // Guardrail (Requirement 7): Never accept client-supplied stipend amounts.
                if (request.AdditionalFields is { Count: > 0 } extra
                    && extra.Keys.Any(k =>
                        k.Equals("amount", StringComparison.OrdinalIgnoreCase)
                        || k.Equals("grossAmount", StringComparison.OrdinalIgnoreCase)
                        || k.Equals("gross_amount", StringComparison.OrdinalIgnoreCase)
                        || k.Equals("stipendAmount", StringComparison.OrdinalIgnoreCase)
                        || k.Equals("stipend_amount", StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.BadRequest(new
                    {
                        detail = "Client-supplied stipend amounts are prohibited; stipend credits are computed exclusively from server protocol rules."
                    });
                }

                if (string.IsNullOrWhiteSpace(request.DiaryId) && string.IsNullOrWhiteSpace(request.AssignmentId))
                {
                    return Results.BadRequest(new { detail = "Either diaryId or assignmentId is required to evaluate diary stipend rules." });
                }

                if (wallet == null)
                {
                    (wallet, error) = await ResolveOwnWalletAsync(
                        dbContext,
                        httpContext,
                        caller,
                        requestedSubjectId: request.SubjectId,
                        requestedWalletId: request.WalletId,
                        requestedStudyId: request.StudyCode,
                        requestedSiteId: request.SiteCode,
                        createIfMissing: true,
                        now: now,
                        lockForUpdate: true,
                        ct: ct);

                    if (error != null)
                    {
                        return error;
                    }
                }

                if (wallet == null)
                {
                    return OutsideScope();
                }

                resolvedWalletId = wallet.Id;

                var existingTx = await dbContext.ParticipantTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t =>
                        t.WalletId == wallet.Id
                        && t.TransactionType == "STIPEND_CREDIT"
                        && (t.IdempotencyKey == idempotencyKey || t.ReferenceId == diaryReference), ct);

                if (existingTx != null)
                {
                    return BuildAlreadyProcessedDiaryStipendResponse(existingTx, wallet, caller.UserId, idempotencyKey);
                }

                string diaryCode = (request.DiaryId ?? request.AssignmentId ?? "EPRO_DIARY").Trim();
                string? candidateSyncToken = ResolveRequestedDiarySyncToken(request);

                if (!TryVerifyDiarySyncVerificationToken(
                        candidateSyncToken,
                        gatewaySecret,
                        caller.UserId,
                        wallet.StudyId,
                        request.DiaryId,
                        request.AssignmentId,
                        request.SequenceNumber,
                        now,
                        out var verifiedSyncToken)
                    || verifiedSyncToken is null)
                {
                    string invalidReason = string.IsNullOrWhiteSpace(candidateSyncToken)
                        ? "Server-verified ePRO diary synchronization token is required before crediting protocol stipends; routed to coordinator review."
                        : "ePRO diary synchronization token could not be verified for this participant, study, diary, or sequence number; routed to coordinator review.";

                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "DIARY_SYNC_VERIFICATION_FAILED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            idempotency_key = idempotencyKey,
                            diary_id = diaryCode,
                            assignment_id = request.AssignmentId,
                            sequence_number = request.SequenceNumber,
                            token_supplied = !string.IsNullOrWhiteSpace(candidateSyncToken)
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Unverified self-service diary stipend request held for review",
                        VersionIndex = wallet.VersionIndex
                    });
                    await dbContext.SaveChangesAsync(ct);

                    return BuildFlaggedDiaryStipendResponse(wallet, caller.UserId, idempotencyKey, invalidReason);
                }

                bool syncTokenAlreadyConsumed = await dbContext.PaymentAuditLogs.AsNoTracking()
                    .AnyAsync(l => l.Action == DiarySyncTokenConsumedAuditAction && l.EntityId == verifiedSyncToken.TokenId, ct)
                    || dbContext.ChangeTracker.Entries<PaymentAuditLog>()
                        .Any(e => e.Entity.Action == DiarySyncTokenConsumedAuditAction && e.Entity.EntityId == verifiedSyncToken.TokenId);

                if (syncTokenAlreadyConsumed)
                {
                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "DIARY_SYNC_TOKEN_REPLAYED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            idempotency_key = idempotencyKey,
                            diary_id = diaryCode,
                            token_id = verifiedSyncToken.TokenId
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Replayed single-use diary sync verification token held for review",
                        VersionIndex = wallet.VersionIndex
                    });
                    await dbContext.SaveChangesAsync(ct);

                    return BuildFlaggedDiaryStipendResponse(
                        wallet,
                        caller.UserId,
                        idempotencyKey,
                        "ePRO diary synchronization token was already consumed by a prior stipend evaluation; routed to coordinator review.");
                }

                var activeStudyRules = await dbContext.ParticipantStipendRules.AsNoTracking()
                    .Where(r => r.IsActive && r.StudyId == wallet.StudyId)
                    .ToListAsync(ct);

                var eligibleStudyRules = activeStudyRules
                    .Where(r => !r.RequiresVerification && !r.IsMilestone)
                    .ToList();

                var explicitEligibleRule = eligibleStudyRules.FirstOrDefault(r =>
                    string.Equals(r.VisitCode, diaryCode, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.ProcedureCode, diaryCode, StringComparison.OrdinalIgnoreCase));

                var explicitRestrictedRule = explicitEligibleRule == null
                    ? activeStudyRules.FirstOrDefault(r =>
                        (r.RequiresVerification || r.IsMilestone)
                        && (string.Equals(r.VisitCode, diaryCode, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(r.ProcedureCode, diaryCode, StringComparison.OrdinalIgnoreCase)))
                    : null;

                if (explicitRestrictedRule != null)
                {
                    RecordConsumedDiarySyncTokenAuditLog(dbContext, wallet, caller.UserId, rolesText, verifiedSyncToken.TokenId, idempotencyKey, diaryCode, now);
                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "STIPEND_VERIFICATION_REQUIRED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            idempotency_key = idempotencyKey,
                            diary_id = diaryCode,
                            rule_id = explicitRestrictedRule.Id,
                            requires_verification = explicitRestrictedRule.RequiresVerification,
                            is_milestone = explicitRestrictedRule.IsMilestone
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Clinician-verified or milestone stipend rule cannot be auto-credited via participant self-service",
                        VersionIndex = wallet.VersionIndex
                    });
                    await dbContext.SaveChangesAsync(ct);

                    return BuildFlaggedDiaryStipendResponse(
                        wallet,
                        caller.UserId,
                        idempotencyKey,
                        $"Protocol rule '{explicitRestrictedRule.VisitCode}' requires clinician verification or milestone sign-off and cannot be auto-credited via self-service.");
                }

                var matchingRule = explicitEligibleRule
                    ?? eligibleStudyRules.FirstOrDefault(r =>
                        string.Equals(r.VisitCode, "DIARY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.VisitCode, "EPRO_DIARY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.VisitCode, "DIARY_SYNC", StringComparison.OrdinalIgnoreCase));

                if (matchingRule == null)
                {
                    RecordConsumedDiarySyncTokenAuditLog(dbContext, wallet, caller.UserId, rolesText, verifiedSyncToken.TokenId, idempotencyKey, diaryCode, now);
                    await dbContext.SaveChangesAsync(ct);

                    return Results.Ok(new SelfServiceDiaryStipendResponseDto(
                        Success: true,
                        Status: "NO_APPLICABLE_RULE",
                        CreditedAmount: 0m,
                        CurrentBalance: wallet.CurrentBalance,
                        LifetimeStipends: wallet.LifetimeStipends,
                        WalletId: wallet.Id,
                        SubjectId: caller.UserId,
                        Currency: wallet.Currency,
                        TransactionId: null,
                        ReferenceNumber: null,
                        IdempotencyKey: idempotencyKey,
                        Reason: $"No active self-service diary stipend rule configured for '{diaryCode}' in study '{wallet.StudyId}'."
                    ));
                }

                decimal stipendAmount = matchingRule.Amount;
                RecordConsumedDiarySyncTokenAuditLog(dbContext, wallet, caller.UserId, rolesText, verifiedSyncToken.TokenId, idempotencyKey, diaryCode, now);

                // Enforce IRB anti-coercion cap
                bool ruleCapExceeded = matchingRule.AntiCoercionCap.HasValue && stipendAmount > matchingRule.AntiCoercionCap.Value;
                bool walletCapExceeded = wallet.AntiCoercionCap.HasValue && stipendAmount > wallet.AntiCoercionCap.Value;
                if (ruleCapExceeded || walletCapExceeded)
                {
                    decimal effectiveCap = Math.Min(
                        matchingRule.AntiCoercionCap ?? decimal.MaxValue,
                        wallet.AntiCoercionCap ?? decimal.MaxValue);

                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "ANTI_COERCION_CAP_EXCEEDED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            idempotency_key = idempotencyKey,
                            diary_id = diaryCode,
                            amount = stipendAmount,
                            anti_coercion_cap = effectiveCap
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Diary stipend exceeds IRB anti-coercion cap; routed to review",
                        VersionIndex = wallet.VersionIndex
                    });
                    await dbContext.SaveChangesAsync(ct);

                    return BuildFlaggedDiaryStipendResponse(
                        wallet,
                        caller.UserId,
                        idempotencyKey,
                        $"Protocol stipend (${stipendAmount:F2}) exceeds IRB anti-coercion cap (${effectiveCap:F2}); routed to coordinator review.");
                }

                // Enforce configurable auto-disbursement safety threshold
                bool ruleThresholdExceeded = matchingRule.AutoDisbursementThreshold.HasValue && stipendAmount > matchingRule.AutoDisbursementThreshold.Value;
                bool walletThresholdExceeded = wallet.AutoDisbursementThreshold.HasValue && stipendAmount > wallet.AutoDisbursementThreshold.Value;
                if (ruleThresholdExceeded || walletThresholdExceeded)
                {
                    decimal effectiveThreshold = Math.Min(
                        matchingRule.AutoDisbursementThreshold ?? decimal.MaxValue,
                        wallet.AutoDisbursementThreshold ?? decimal.MaxValue);

                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "THRESHOLD_EXCEEDED_REVIEW_REQUIRED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            idempotency_key = idempotencyKey,
                            diary_id = diaryCode,
                            amount = stipendAmount,
                            auto_disbursement_threshold = effectiveThreshold
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Stipend amount exceeds auto-disbursement safety threshold",
                        VersionIndex = wallet.VersionIndex
                    });
                    await dbContext.SaveChangesAsync(ct);

                    return BuildFlaggedDiaryStipendResponse(
                        wallet,
                        caller.UserId,
                        idempotencyKey,
                        $"Payment of ${stipendAmount:F2} exceeds configured auto-disbursement threshold limit (${effectiveThreshold:F2}). Routed to administrator review queue.");
                }

                // Enforce IRS $600 YTD W-9 compliance check
                decimal projectedTaxable = matchingRule.Taxable ? wallet.TaxableYtd + stipendAmount : wallet.TaxableYtd;
                if (projectedTaxable >= 600.00m && !string.Equals(wallet.W9Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
                {
                    wallet.Is1099ThresholdExceeded = true;
                    if (wallet.W9Status == "NOT_REQUIRED")
                    {
                        wallet.W9Status = "REQUIRED";
                    }
                    wallet.UpdatedAt = now;
                    wallet.VersionIndex += 1;

                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "IRS_1099_THRESHOLD_EXCEEDED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            idempotency_key = idempotencyKey,
                            diary_id = diaryCode,
                            amount = stipendAmount,
                            taxable_ytd = projectedTaxable,
                            w9_status = wallet.W9Status
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Diary stipend halted pending Form W-9 verification",
                        VersionIndex = wallet.VersionIndex
                    });
                    await dbContext.SaveChangesAsync(ct);

                    return BuildFlaggedDiaryStipendResponse(
                        wallet,
                        caller.UserId,
                        idempotencyKey,
                        $"Annual taxable payments reach ${projectedTaxable:F2} without a verified Form W-9. Stipend credit halted for review.",
                        wallet.W9Status);
                }

                wallet.CurrentBalance += stipendAmount;
                wallet.LifetimeStipends += stipendAmount;
                if (matchingRule.Taxable)
                {
                    wallet.TaxableYtd += stipendAmount;
                    if (wallet.TaxableYtd >= 600.00m)
                    {
                        wallet.Is1099ThresholdExceeded = true;
                    }
                }
                wallet.UpdatedAt = now;
                wallet.VersionIndex += 1;

                var tx = new ParticipantTransaction
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = caller.UserId,
                    StudyId = wallet.StudyId,
                    TransactionType = "STIPEND_CREDIT",
                    Amount = stipendAmount,
                    BalanceAfter = wallet.CurrentBalance,
                    ReferenceId = diaryReference,
                    IdempotencyKey = idempotencyKey,
                    Description = $"Protocol diary synchronization stipend credit ({matchingRule.VisitName})",
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    VersionIndex = 1
                };
                dbContext.ParticipantTransactions.Add(tx);

                dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantWallet",
                    EntityId = wallet.Id,
                    Action = "PARTICIPANT_STIPEND_CREDITED",
                    UserId = caller.UserId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new
                    {
                        idempotency_key = idempotencyKey,
                        diary_id = diaryCode,
                        rule_id = matchingRule.Id,
                        stipend_amount = stipendAmount,
                        new_balance = wallet.CurrentBalance,
                        transaction_id = tx.Id
                    }),
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    ReasonForChange = $"Server-rule protocol stipend credited for synchronized diary '{diaryCode}'",
                    VersionIndex = wallet.VersionIndex
                });

                await dbContext.SaveChangesAsync(ct);

                return Results.Ok(new SelfServiceDiaryStipendResponseDto(
                    Success: true,
                    Status: "CREDITED",
                    CreditedAmount: stipendAmount,
                    CurrentBalance: wallet.CurrentBalance,
                    LifetimeStipends: wallet.LifetimeStipends,
                    WalletId: wallet.Id,
                    SubjectId: caller.UserId,
                    Currency: wallet.Currency,
                    TransactionId: tx.Id,
                    ReferenceNumber: tx.Id,
                    IdempotencyKey: idempotencyKey
                ));
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex) || !dbContext.Database.IsRelational())
        {
            dbContext.ChangeTracker.Clear();
            var racedWallet = resolvedWalletId is not null
                ? await dbContext.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == resolvedWalletId)
                : await dbContext.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.SubjectId == caller.UserId);
            if (racedWallet != null)
            {
                var racedTx = await dbContext.ParticipantTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t =>
                        t.WalletId == racedWallet.Id
                        && t.TransactionType == "STIPEND_CREDIT"
                        && (t.IdempotencyKey == idempotencyKey || t.ReferenceId == diaryReference));
                if (racedTx != null)
                {
                    return BuildAlreadyProcessedDiaryStipendResponse(racedTx, racedWallet, caller.UserId, idempotencyKey);
                }
            }
            throw;
        }
    }

    private static IResult BuildAlreadyProcessedDiaryStipendResponse(
        ParticipantTransaction existingTx,
        ParticipantWallet wallet,
        string subjectId,
        string idempotencyKey) =>
        Results.Ok(new SelfServiceDiaryStipendResponseDto(
            Success: true,
            Status: "ALREADY_PROCESSED",
            CreditedAmount: 0m,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            WalletId: wallet.Id,
            SubjectId: subjectId,
            Currency: wallet.Currency,
            TransactionId: existingTx.Id,
            ReferenceNumber: existingTx.ReferenceId,
            IdempotencyKey: idempotencyKey,
            Reason: "Diary synchronization stipend was already credited for this submission."
        ));

    private static async Task<IResult> ReviewExpenseClaim(string claimId, ReviewExpenseClaimRequestDto request, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);

        return await ExecuteWalletUnitOfWorkAsync(dbContext, async ct =>
        {
            var claim = await dbContext.ParticipantExpenseClaims
                .FirstOrDefaultAsync(c => c.Id == claimId, ct);

            if (claim == null || !Reaches(httpContext, claim.StudyId, claim.SiteId))
            {
                return Results.NotFound(new { detail = $"Expense claim '{claimId}' not found." });
            }

            DateTimeOffset now = SystemClock.Instance.UtcNow;

            try
            {
                if (string.Equals(request.Status, ParticipantExpenseClaimStatus.APPROVED.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    // Expense submission and review share site clinical roles, so two people must perform them:
                    // the user who submitted a claim may not also approve it (separation of duties, matching SignRemittanceAdvice).
                    if (!string.IsNullOrWhiteSpace(claim.CreatedBy)
                        && string.Equals(claim.CreatedBy, userId, StringComparison.Ordinal))
                    {
                        return Results.Conflict(new { detail = "The user who submitted this expense claim cannot also approve it." });
                    }

                    if (!claim.IsMealExpenseClaim())
                    {
                        claim.TaxDeduction = 0m;
                    }

                    claim.Approve(userId, now, request.ReviewNotes, request.AdjustedAmount);
                }
                else if (string.Equals(request.Status, ParticipantExpenseClaimStatus.REJECTED.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    claim.Reject(userId, now, request.ReviewNotes);
                }
                else
                {
                    return Results.Conflict(new { detail = $"Invalid review status '{request.Status}'. Only APPROVED or REJECTED are permitted." });
                }
            }
            catch (PaymentDomainException ex)
            {
                return Results.Conflict(new { detail = ex.Message });
            }

            if (string.Equals(claim.Status, ParticipantExpenseClaimStatus.APPROVED.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var wallet = await LockWalletByIdAsync(dbContext, claim.WalletId, ct);

                if (wallet == null)
                {
                    wallet = await LockWalletBySubjectAndStudyAsync(dbContext, claim.SubjectId, claim.StudyId, ct);
                }

                if (wallet != null)
                {
                    claim.RecordWalletCredit();
                    wallet.CurrentBalance += claim.NetAmount;
                    wallet.LifetimeReimbursements += claim.NetAmount;
                    wallet.UpdatedAt = now;
                    wallet.VersionIndex += 1;

                    var tx = new ParticipantTransaction
                    {
                        Id = Guid.NewGuid().ToString(),
                        WalletId = wallet.Id,
                        SubjectId = wallet.SubjectId,
                        StudyId = wallet.StudyId,
                        TransactionType = "REIMBURSEMENT_CREDIT",
                        Amount = claim.NetAmount,
                        BalanceAfter = wallet.CurrentBalance,
                        ReferenceId = claim.Id,
                        Description = $"Reimbursement approval for {claim.Category} expense ({claim.MerchantName})",
                        CreatedAt = now,
                        CreatedBy = userId,
                        VersionIndex = 1
                    };
                    dbContext.ParticipantTransactions.Add(tx);
                }

                var audit = new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantExpenseClaim",
                    EntityId = claim.Id,
                    Action = "EXPENSE_CLAIM_APPROVED",
                    UserId = userId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new { claim_id = claim.Id, net_amount = claim.NetAmount, status = claim.Status }),
                    CreatedAt = now,
                    CreatedBy = userId,
                    ReasonForChange = "Expense claim approved by coordinator",
                    VersionIndex = claim.VersionIndex
                };
                dbContext.PaymentAuditLogs.Add(audit);
            }
            else
            {
                var audit = new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantExpenseClaim",
                    EntityId = claim.Id,
                    Action = "EXPENSE_CLAIM_REJECTED",
                    UserId = userId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new { claim_id = claim.Id, status = claim.Status, notes = request.ReviewNotes }),
                    CreatedAt = now,
                    CreatedBy = userId,
                    ReasonForChange = "Expense claim rejected by coordinator",
                    VersionIndex = claim.VersionIndex
                };
                dbContext.PaymentAuditLogs.Add(audit);
            }

            await dbContext.SaveChangesAsync(ct);

            return Results.Ok(claim);
        });
    }

    private static async Task<IResult> ListTransactions(PaymentsDbContext dbContext, HttpContext httpContext, string? subjectId = null, string? walletId = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        // These rows carry no site, so they are scoped through their wallet.
        var query = dbContext.ParticipantTransactions.AsQueryable();
        if (!ClinicalScopeGuard.IsUnrestricted(httpContext))
        {
            var walletIds = ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantWallets, httpContext, w => w.StudyId, w => w.SiteId).Select(w => w.Id);
            query = query.Where(t => walletIds.Contains(t.WalletId));
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query = query.Where(t => t.SubjectId == subjectId);
        }

        if (!string.IsNullOrWhiteSpace(walletId))
        {
            query = query.Where(t => t.WalletId == walletId);
        }

        var txs = await query
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Results.Ok(txs);
    }

    private static async Task<IResult> ListDisbursements(PaymentsDbContext dbContext, HttpContext httpContext, string? subjectId = null, string? walletId = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        // These rows carry no site, so they are scoped through their wallet.
        var query = dbContext.ParticipantDisbursements.AsQueryable();
        if (!ClinicalScopeGuard.IsUnrestricted(httpContext))
        {
            var walletIds = ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantWallets, httpContext, w => w.StudyId, w => w.SiteId).Select(w => w.Id);
            query = query.Where(d => walletIds.Contains(d.WalletId));
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query = query.Where(d => d.SubjectId == subjectId);
        }

        if (!string.IsNullOrWhiteSpace(walletId))
        {
            query = query.Where(d => d.WalletId == walletId);
        }

        var disbs = await query
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return Results.Ok(disbs);
    }

    private static async Task<IResult> SettleDisbursement(
        string id,
        PaymentsDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        var disb = await dbContext.ParticipantDisbursements.FirstOrDefaultAsync(d => d.Id == id || d.ReferenceNumber == id);
        if (disb == null)
        {
            return Results.NotFound(new { detail = $"Disbursement '{id}' not found." });
        }

        var wallet = await dbContext.ParticipantWallets.FirstOrDefaultAsync(w => w.Id == disb.WalletId);
        if (wallet != null && !Reaches(httpContext, wallet.StudyId, wallet.SiteId))
        {
            return OutsideScope();
        }

        DateTimeOffset now = clock.UtcNow;
        try
        {
            disb.Settle(now);
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }

        var audit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantDisbursement",
            EntityId = disb.Id,
            Action = "DIGITAL_DISBURSEMENT_SETTLED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
            Details = JsonSerializer.Serialize(new
            {
                disbursement_id = disb.Id,
                reference_number = disb.ReferenceNumber,
                status = disb.Status,
                amount = disb.Amount
            }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "Disbursement confirmed and settled by payment rail",
            VersionIndex = disb.VersionIndex
        };
        dbContext.PaymentAuditLogs.Add(audit);
        await dbContext.SaveChangesAsync();

        return Results.Ok(disb);
    }

    private static string ResolveDirectStipendIdempotencyKey(DisburseStipendRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return request.IdempotencyKey.Trim();
        }

        return request.SequenceNumber.HasValue
            ? $"seq_{request.SequenceNumber}_{request.SubjectId}"
            : $"disburse_{request.SubjectId}_{Guid.NewGuid()}";
    }

    private static (ParticipantStipendRule? MatchingRule, IResult? RejectionResult) EvaluateDirectStipendPolicy(
        decimal stipendAmount,
        ParticipantWallet? wallet,
        IReadOnlyList<ParticipantStipendRule> studyRules,
        DisburseStipendRequest request)
    {
        string? diaryCode = !string.IsNullOrWhiteSpace(request.DiaryId)
            ? request.DiaryId.Trim()
            : request.AssignmentId?.Trim();

        ParticipantStipendRule? matchingRule = null;
        if (!string.IsNullOrEmpty(diaryCode))
        {
            matchingRule = studyRules.FirstOrDefault(r =>
                string.Equals(r.VisitCode, diaryCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.ProcedureCode, diaryCode, StringComparison.OrdinalIgnoreCase));
        }

        matchingRule ??= studyRules.FirstOrDefault(r =>
            string.Equals(r.VisitCode, "DIARY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.VisitCode, "EPRO_DIARY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.VisitCode, "DIARY_SYNC", StringComparison.OrdinalIgnoreCase));

        decimal? ruleCap = matchingRule?.AntiCoercionCap;
        if (!ruleCap.HasValue && studyRules.Any(r => r.AntiCoercionCap.HasValue))
        {
            ruleCap = studyRules.Where(r => r.AntiCoercionCap.HasValue).Max(r => r.AntiCoercionCap!.Value);
        }

        decimal? walletCap = wallet?.AntiCoercionCap;
        if ((ruleCap.HasValue && stipendAmount > ruleCap.Value)
            || (walletCap.HasValue && stipendAmount > walletCap.Value))
        {
            decimal effectiveCap = Math.Min(ruleCap ?? decimal.MaxValue, walletCap ?? decimal.MaxValue);
            return (matchingRule, Results.UnprocessableEntity(new
            {
                detail = $"Stipend amount (${stipendAmount:F2}) exceeds IRB anti-coercion cap limit (${effectiveCap:F2})."
            }));
        }

        decimal? ruleThreshold = matchingRule?.AutoDisbursementThreshold;
        if (!ruleThreshold.HasValue && studyRules.Any(r => r.AutoDisbursementThreshold.HasValue))
        {
            ruleThreshold = studyRules.Where(r => r.AutoDisbursementThreshold.HasValue).Max(r => r.AutoDisbursementThreshold!.Value);
        }

        decimal? walletThreshold = wallet?.AutoDisbursementThreshold;
        if ((ruleThreshold.HasValue && stipendAmount > ruleThreshold.Value)
            || (walletThreshold.HasValue && stipendAmount > walletThreshold.Value))
        {
            decimal effectiveThreshold = Math.Min(ruleThreshold ?? decimal.MaxValue, walletThreshold ?? decimal.MaxValue);
            return (matchingRule, Results.Conflict(new
            {
                detail = $"Stipend amount (${stipendAmount:F2}) exceeds configured auto-disbursement threshold limit (${effectiveThreshold:F2})."
            }));
        }

        decimal potentialTaxableYtd = (wallet?.TaxableYtd ?? 0m) + stipendAmount;
        string w9Status = wallet?.W9Status ?? "NOT_REQUIRED";
        if (potentialTaxableYtd >= 600.00m && !string.Equals(w9Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
        {
            return (matchingRule, Results.Conflict(new
            {
                detail = $"IRS $600.00 YTD tax threshold reached (YTD: ${potentialTaxableYtd:F2}) without a verified Form W-9. Direct disbursement halted until Form W-9 is verified."
            }));
        }

        return (matchingRule, null);
    }

    private static IResult BuildDuplicateDirectStipendResponse(
        ParticipantTransaction existingTx,
        ParticipantWallet wallet,
        string subjectId,
        string idempotencyKey) =>
        Results.Ok(new DisburseStipendResponse(
            Success: true,
            Id: existingTx.Id,
            WalletId: wallet.Id,
            SubjectId: subjectId,
            Amount: existingTx.Amount,
            NewBalance: wallet.CurrentBalance,
            Currency: wallet.Currency,
            Status: "DUPLICATE_SKIPPED",
            ReferenceNumber: $"DISB-{existingTx.Id[..Math.Min(8, existingTx.Id.Length)]}",
            IdempotencyKey: idempotencyKey,
            DispatchedAt: existingTx.CreatedAt
        ));

    private static async Task<IResult> DisburseStipendDirect(DisburseStipendRequest request, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.SubjectId))
        {
            return Results.BadRequest(new { detail = "subject_id is required for stipend disbursement." });
        }

        decimal stipendAmount = request.Amount ?? 25.00m;
        if (stipendAmount <= 0m)
        {
            return Results.BadRequest(new { detail = "Stipend disbursement amount must be greater than zero." });
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        string idempotencyKey = ResolveDirectStipendIdempotencyKey(request);
        if (idempotencyKey.Length > 255)
        {
            return Results.BadRequest(new { detail = "idempotencyKey must not exceed 255 characters." });
        }

        try
        {
            return await ExecuteWalletUnitOfWorkAsync(dbContext, async ct =>
            {
                var subjectWallets = await LockWalletsBySubjectAsync(dbContext, request.SubjectId, ct);
                var wallet = !string.IsNullOrWhiteSpace(request.StudyId)
                    ? subjectWallets.FirstOrDefault(w => string.Equals(w.StudyId, request.StudyId, StringComparison.Ordinal))
                    : subjectWallets.FirstOrDefault();

                string walletStudyId = wallet?.StudyId ?? request.StudyId ?? "STUDY-DCT-001";
                string walletSiteId = wallet?.SiteId ?? request.SiteId ?? "SITE-101";
                if (!Reaches(httpContext, walletStudyId, walletSiteId))
                {
                    return OutsideScope();
                }

                if (wallet != null)
                {
                    var existingTx = await dbContext.ParticipantTransactions.AsNoTracking()
                        .FirstOrDefaultAsync(t => t.WalletId == wallet.Id && t.IdempotencyKey == idempotencyKey, ct);

                    if (existingTx != null)
                    {
                        return BuildDuplicateDirectStipendResponse(existingTx, wallet, request.SubjectId, idempotencyKey);
                    }
                }

                var studyRules = await dbContext.ParticipantStipendRules.AsNoTracking()
                    .Where(r => r.IsActive && r.StudyId == walletStudyId)
                    .ToListAsync(ct);

                var (matchingRule, rejection) = EvaluateDirectStipendPolicy(stipendAmount, wallet, studyRules, request);
                if (rejection != null)
                {
                    return rejection;
                }

                DateTimeOffset now = SystemClock.Instance.UtcNow;
                bool isNewWallet = wallet == null;

                if (wallet == null)
                {
                    wallet = new ParticipantWallet
                    {
                        Id = Guid.NewGuid().ToString(),
                        SubjectId = request.SubjectId,
                        StudyId = walletStudyId,
                        SiteId = walletSiteId,
                        CurrentBalance = 0m,
                        LifetimeStipends = 0m,
                        LifetimeReimbursements = 0m,
                        LifetimeDisbursements = 0m,
                        Currency = matchingRule?.Currency ?? "USD",
                        Status = "ACTIVE",
                        W9Status = "NOT_REQUIRED",
                        CreatedAt = now,
                        CreatedBy = userId,
                        UpdatedAt = now,
                        VersionIndex = 1
                    };
                    dbContext.ParticipantWallets.Add(wallet);
                }

                wallet.CurrentBalance += stipendAmount;
                wallet.LifetimeStipends += stipendAmount;
                wallet.TaxableYtd += stipendAmount;
                if (wallet.TaxableYtd >= 600.00m)
                {
                    wallet.Is1099ThresholdExceeded = true;
                }
                wallet.UpdatedAt = now;
                if (!isNewWallet)
                {
                    wallet.VersionIndex += 1;
                }

                string txType = matchingRule is { IsMilestone: true } ? "MILESTONE_CREDIT" : "STIPEND_CREDIT";
                var tx = new ParticipantTransaction
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = request.SubjectId,
                    StudyId = wallet.StudyId,
                    TransactionType = txType,
                    Amount = stipendAmount,
                    BalanceAfter = wallet.CurrentBalance,
                    ReferenceId = idempotencyKey,
                    IdempotencyKey = idempotencyKey,
                    Description = !string.IsNullOrWhiteSpace(request.DiaryId)
                        ? $"Direct ePRO stipend credit ({request.DiaryId.Trim()})"
                        : "Direct participant stipend credit",
                    CreatedAt = now,
                    CreatedBy = userId,
                    VersionIndex = 1
                };
                dbContext.ParticipantTransactions.Add(tx);

                var auditDetails = JsonSerializer.Serialize(new
                {
                    subject_id = request.SubjectId,
                    sequence_number = request.SequenceNumber,
                    assignment_id = request.AssignmentId,
                    diary_id = request.DiaryId,
                    stipend_amount = stipendAmount,
                    taxable_ytd = wallet.TaxableYtd,
                    idempotency_key = idempotencyKey,
                    transaction_id = tx.Id,
                    new_balance = wallet.CurrentBalance,
                    timestamp = now
                });

                var auditLog = new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantWallet",
                    EntityId = wallet.Id,
                    Action = "AUTO_STIPEND_DISBURSEMENT",
                    UserId = userId,
                    UserRoles = rolesText,
                    Details = auditDetails,
                    CreatedAt = now,
                    CreatedBy = userId,
                    ReasonForChange = "Automatic ePRO survey completion stipend payout",
                    VersionIndex = wallet.VersionIndex
                };

                dbContext.PaymentAuditLogs.Add(auditLog);
                await dbContext.SaveChangesAsync(ct);

                return Results.Ok(new DisburseStipendResponse(
                    Success: true,
                    Id: tx.Id,
                    WalletId: wallet.Id,
                    SubjectId: request.SubjectId,
                    Amount: stipendAmount,
                    NewBalance: wallet.CurrentBalance,
                    Currency: wallet.Currency,
                    Status: "COMPLETED",
                    ReferenceNumber: $"DISB-{tx.Id[..Math.Min(8, tx.Id.Length)]}",
                    IdempotencyKey: idempotencyKey,
                    DispatchedAt: now
                ));
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "payments_participant_transactions"))
        {
            dbContext.ChangeTracker.Clear();
            var racedWallets = await dbContext.ParticipantWallets.AsNoTracking()
                .Where(w => w.SubjectId == request.SubjectId)
                .ToListAsync();
            var racedWallet = !string.IsNullOrWhiteSpace(request.StudyId)
                ? racedWallets.FirstOrDefault(w => string.Equals(w.StudyId, request.StudyId, StringComparison.Ordinal))
                : racedWallets.FirstOrDefault();
            if (racedWallet != null)
            {
                if (!Reaches(httpContext, racedWallet))
                {
                    return OutsideScope();
                }

                var racedTx = await dbContext.ParticipantTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.WalletId == racedWallet.Id && t.IdempotencyKey == idempotencyKey);
                if (racedTx != null)
                {
                    return BuildDuplicateDirectStipendResponse(racedTx, racedWallet, request.SubjectId, idempotencyKey);
                }
            }
            throw;
        }
    }

    private static async Task<IResult> GetAuditLogs(PaymentsDbContext dbContext, HttpContext httpContext, string? subjectId = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var query = dbContext.PaymentAuditLogs.AsQueryable();

        // Audit rows carry no study or site of their own: a caller limited to some studies or sites sees the rows about
        // the wallets, claims, stipend rules, invoices and remittances they reach, and no batch-level rows.
        if (!ClinicalScopeGuard.IsUnrestricted(httpContext))
        {
            var walletIds = ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantWallets, httpContext, w => w.StudyId, w => w.SiteId).Select(w => w.Id);
            var claimIds = ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantExpenseClaims, httpContext, c => c.StudyId, c => c.SiteId).Select(c => c.Id);
            var invoiceIds = ClinicalScopeGuard.NarrowToScope(dbContext.SelfBillingInvoices, httpContext, i => i.StudyId, i => i.SiteId).Select(i => i.Id);
            var remittanceIds = ClinicalScopeGuard.NarrowToScope(dbContext.RemittanceAdvices, httpContext, r => r.StudyId, r => r.SiteId).Select(r => r.Id);
            var ruleIds = ClinicalScopeGuard.NarrowToStudyScope(dbContext.ParticipantStipendRules, httpContext, r => r.StudyId).Select(r => r.Id);
            query = query.Where(l =>
                walletIds.Contains(l.EntityId) || claimIds.Contains(l.EntityId) || invoiceIds.Contains(l.EntityId)
                || remittanceIds.Contains(l.EntityId) || ruleIds.Contains(l.EntityId));
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query = query.Where(l => l.UserId == subjectId || (l.Details != null && l.Details.Contains(subjectId)));
        }

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(100)
            .ToListAsync();

        return Results.Ok(logs);
    }

    private static SelfBillingInvoiceResponse MapInvoiceToResponse(
        SelfBillingInvoice inv,
        IEnumerable<InvoiceLineItem> lineItems,
        RemittanceAdvice? remittance) => new(
            Id: inv.Id,
            InvoiceNumber: inv.InvoiceNumber,
            StudyId: inv.StudyId,
            SiteId: inv.SiteId,
            ContractId: inv.ContractId,
            BillingCycle: inv.BillingCycle,
            PeriodStart: inv.PeriodStart,
            PeriodEnd: inv.PeriodEnd,
            Currency: inv.Currency,
            TotalBaseAmount: inv.TotalBaseAmount,
            TotalOverheadAmount: inv.TotalOverheadAmount,
            TotalPatientStipend: inv.TotalPatientStipend,
            TotalTravelReimbursement: inv.TotalTravelReimbursement,
            TotalGrossAmount: inv.TotalGrossAmount,
            TotalHoldbackAmount: inv.TotalHoldbackAmount,
            TotalNetAmount: inv.TotalNetAmount,
            PayableCount: inv.PayableCount,
            Status: inv.Status,
            ApprovedBy: inv.ApprovedBy,
            ApprovedAt: inv.ApprovedAt,
            ErpSystem: inv.ErpSystem,
            ErpReferenceId: inv.ErpReferenceId,
            ErpSyncStatus: inv.ErpSyncStatus,
            ErpSyncedAt: inv.ErpSyncedAt,
            ReconciledBy: inv.ReconciledBy,
            ReconciledAt: inv.ReconciledAt,
            ReconciliationReference: inv.ReconciliationReference,
            ReconciliationNotes: inv.ReconciliationNotes,
            CreatedAt: inv.CreatedAt,
            CreatedBy: inv.CreatedBy,
            ReasonForChange: inv.ReasonForChange,
            VersionIndex: inv.VersionIndex,
            LineItems: lineItems.Select(item => new InvoiceLineItemDto(
                Id: item.Id,
                InvoiceId: item.InvoiceId,
                PayableId: item.PayableId,
                SubjectId: item.SubjectId,
                VisitCode: item.VisitCode,
                ProcedureCode: item.ProcedureCode,
                ProcedureName: item.ProcedureName,
                BaseAmount: item.BaseAmount,
                OverheadAmount: item.OverheadAmount,
                PatientStipend: item.PatientStipend,
                TravelReimbursement: item.TravelReimbursement,
                GrossAmount: item.GrossAmount,
                WithholdingRate: item.WithholdingRate,
                WithholdingAmount: item.WithholdingAmount,
                NetAmount: item.NetAmount,
                Currency: item.Currency,
                CreatedAt: item.CreatedAt,
                VersionIndex: item.VersionIndex
            )).ToList(),
            RemittanceAdvice: remittance == null ? null : new RemittanceAdviceDto(
                Id: remittance.Id,
                RemittanceNumber: remittance.RemittanceNumber,
                InvoiceId: remittance.InvoiceId,
                StudyId: remittance.StudyId,
                SiteId: remittance.SiteId,
                Currency: remittance.Currency,
                GrossAmount: remittance.GrossAmount,
                OverheadAmount: remittance.OverheadAmount,
                HoldbackAmount: remittance.HoldbackAmount,
                NetAmount: remittance.NetAmount,
                PdfSha256Hash: remittance.PdfSha256Hash,
                IsSealed: remittance.IsSealed,
                IsSigned: remittance.IsSigned,
                SignedBy: remittance.SignedBy,
                SignedAt: remittance.SignedAt,
                SignerRole: remittance.SignerRole,
                DispatchStatus: remittance.DispatchStatus,
                CreatedAt: remittance.CreatedAt,
                VersionIndex: remittance.VersionIndex
            )
        );

    private static async Task<IResult> ListInvoices(
        PaymentsDbContext dbContext,
        HttpContext httpContext,
        string? study_id = null,
        string? site_id = null,
        string? billing_cycle = null,
        string? status = null,
        string? erp_sync_status = null,
        string? batch_execution_id = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var query = ClinicalScopeGuard.NarrowToScope(dbContext.SelfBillingInvoices.AsNoTracking(), httpContext, i => i.StudyId, i => i.SiteId);

        if (!string.IsNullOrWhiteSpace(study_id))
        {
            query = query.Where(i => i.StudyId == study_id);
        }
        if (!string.IsNullOrWhiteSpace(site_id))
        {
            query = query.Where(i => i.SiteId == site_id);
        }
        if (!string.IsNullOrWhiteSpace(billing_cycle))
        {
            query = query.Where(i => i.BillingCycle == billing_cycle);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(i => i.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(erp_sync_status))
        {
            query = query.Where(i => i.ErpSyncStatus == erp_sync_status);
        }
        if (!string.IsNullOrWhiteSpace(batch_execution_id))
        {
            var batchAuditLogs = await dbContext.PaymentAuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "SelfBillingInvoiceBatch" && a.EntityId == batch_execution_id)
                .Select(a => a.Details)
                .ToListAsync();

            var matchingInvoiceIds = new HashSet<string>();
            foreach (var detail in batchAuditLogs)
            {
                if (!string.IsNullOrEmpty(detail) && detail.Contains("invoice_ids"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(detail);
                        if (doc.RootElement.TryGetProperty("invoice_ids", out var idsElement) && idsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var idEl in idsElement.EnumerateArray())
                            {
                                if (idEl.GetString() is string idStr)
                                {
                                    matchingInvoiceIds.Add(idStr);
                                }
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        // Ignore malformed JSON details from non-batch audit logs
                        _ = ex.Message;
                    }
                }
            }

            if (matchingInvoiceIds.Count > 0)
            {
                query = query.Where(i => matchingInvoiceIds.Contains(i.Id));
            }
            else
            {
                query = query.Where(i => i.ReasonForChange != null && i.ReasonForChange.Contains(batch_execution_id));
            }
        }

        var invoices = await query.OrderByDescending(i => i.CreatedAt).ToListAsync();
        var invoiceIds = invoices.Select(i => i.Id).ToList();

        var lineItems = await dbContext.InvoiceLineItems.AsNoTracking()
            .Where(l => invoiceIds.Contains(l.InvoiceId))
            .ToListAsync();

        var remittanceAdvices = await dbContext.RemittanceAdvices.AsNoTracking()
            .Where(r => invoiceIds.Contains(r.InvoiceId))
            .ToListAsync();

        var lineItemMap = lineItems.GroupBy(l => l.InvoiceId).ToDictionary(g => g.Key, g => g.ToList());
        var remittanceMap = remittanceAdvices.ToDictionary(r => r.InvoiceId, r => r);

        var responses = invoices.Select(inv => MapInvoiceToResponse(
            inv,
            lineItemMap.GetValueOrDefault(inv.Id) ?? [],
            remittanceMap.GetValueOrDefault(inv.Id)
        )).ToList();

        return Results.Ok(responses);
    }

    private static async Task<IResult> GetInvoiceById(string id, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var inv = await dbContext.SelfBillingInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inv == null || !Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Self-billing invoice with ID '{id}' not found." });
        }

        var lineItems = await dbContext.InvoiceLineItems.AsNoTracking()
            .Where(l => l.InvoiceId == id)
            .ToListAsync();

        var remittance = await dbContext.RemittanceAdvices.AsNoTracking()
            .FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(MapInvoiceToResponse(inv, lineItems, remittance));
    }

    private static async Task<(SelfBillingInvoice Invoice, List<InvoiceLineItem> LineItems, RemittanceAdvice Remittance, PaymentAuditLog AuditLog)> CompileSiteInvoiceCoreAsync(
        PaymentsDbContext dbContext,
        IClock clock,
        string studyId,
        string siteId,
        string billingCycle,
        string periodStart,
        string periodEnd,
        bool autoApprove,
        string userId,
        string rolesText,
        string? batchExecutionId = null)
    {
        DateTimeOffset now = clock.UtcNow;

        var contract = await dbContext.BudgetContracts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.StudyId == studyId && c.SiteId == siteId && c.IsActive);

        string contractId = contract?.Id ?? $"CONTRACT-{studyId}-{siteId}";
        string currency = contract?.Currency ?? "USD";

        var pendingPayables = await dbContext.Payables
            .Where(p => p.StudyId == studyId && p.SiteId == siteId && p.Status != "INVOICED" && p.Status != "CANCELLED")
            .ToListAsync();

        var eligiblePayables = pendingPayables
            .Where(p => !p.IsSdvGated || (p.SdvVerifiedAt.HasValue && !string.IsNullOrWhiteSpace(p.SdvVerifiedBy)))
            .ToList();

        if (eligiblePayables.Count == 0)
        {
            throw new InvalidOperationException($"No pending achieved milestone payables found for site '{siteId}' in study '{studyId}'.");
        }

        decimal totalBase = eligiblePayables.Sum(p => p.BaseAmount);
        decimal totalOverhead = eligiblePayables.Sum(p => p.OverheadAmount);
        decimal totalStipend = eligiblePayables.Sum(p => p.PatientStipendAmount);
        decimal totalTravel = eligiblePayables.Sum(p => p.TravelReimbursementAmount);
        decimal totalGross = eligiblePayables.Sum(p => p.GrossAmount);
        decimal totalHoldback = eligiblePayables.Sum(p => p.WithholdingAmount);
        decimal totalNet = totalGross - totalHoldback;

        foreach (var p in eligiblePayables)
        {
            p.MarkInvoiced(now);
        }

        string invoiceNumber = $"INV-{studyId}-{siteId}-{now.Ticks % 1000000:D6}";
        var invoice = new SelfBillingInvoice
        {
            Id = Guid.NewGuid().ToString(),
            InvoiceNumber = invoiceNumber,
            StudyId = studyId,
            SiteId = siteId,
            ContractId = contractId,
            BillingCycle = billingCycle,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Currency = currency,
            TotalBaseAmount = totalBase,
            TotalOverheadAmount = totalOverhead,
            TotalPatientStipend = totalStipend,
            TotalTravelReimbursement = totalTravel,
            TotalGrossAmount = totalGross,
            TotalHoldbackAmount = totalHoldback,
            TotalNetAmount = totalNet,
            PayableCount = eligiblePayables.Count,
            Status = autoApprove ? "APPROVED" : "DRAFT",
            ApprovedBy = autoApprove ? userId : null,
            ApprovedAt = autoApprove ? now : null,
            ErpSyncStatus = "PENDING",
            CreatedAt = now,
            CreatedBy = userId,
            UpdatedAt = now,
            ReasonForChange = batchExecutionId != null ? $"Compiled in study batch run {batchExecutionId}" : "Compiled billing cycle",
            VersionIndex = 1
        };

        var lineItems = new List<InvoiceLineItem>();
        foreach (var p in eligiblePayables)
        {
            lineItems.Add(new InvoiceLineItem
            {
                Id = Guid.NewGuid().ToString(),
                InvoiceId = invoice.Id,
                PayableId = p.Id,
                SubjectId = p.SubjectId,
                VisitCode = p.VisitCode,
                ProcedureCode = p.ProcedureCode,
                ProcedureName = p.ProcedureName,
                BaseAmount = p.BaseAmount,
                OverheadAmount = p.OverheadAmount,
                PatientStipend = p.PatientStipendAmount,
                TravelReimbursement = p.TravelReimbursementAmount,
                GrossAmount = p.GrossAmount,
                WithholdingRate = p.WithholdingRate,
                WithholdingAmount = p.WithholdingAmount,
                NetAmount = p.NetAmount,
                Currency = p.Currency,
                CreatedAt = now,
                CreatedBy = userId,
                UpdatedAt = now,
                ReasonForChange = "Invoice line item generated from milestone payable",
                VersionIndex = 1
            });
        }

        string remittanceNumber = $"REM-{invoiceNumber}";
        string pdfContentRaw = $"SELF-BILLING REMITTANCE STATEMENT\nInvoice #: {invoiceNumber}\nStudy: {studyId}\nSite: {siteId}\nNet Total: {currency} {totalNet:F2}\nDate: {now:yyyy-MM-dd HH:mm:ss} UTC";
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(pdfContentRaw));
        string sha256Hash = Convert.ToHexStringLower(hashBytes);

        var remittance = new RemittanceAdvice
        {
            Id = Guid.NewGuid().ToString(),
            RemittanceNumber = remittanceNumber,
            InvoiceId = invoice.Id,
            StudyId = studyId,
            SiteId = siteId,
            Currency = currency,
            GrossAmount = totalGross,
            OverheadAmount = totalOverhead,
            HoldbackAmount = totalHoldback,
            NetAmount = totalNet,
            PdfSha256Hash = sha256Hash,
            PdfContent = pdfContentRaw,
            IsSealed = true,
            IsSigned = false,
            DispatchStatus = "PENDING",
            CreatedAt = now,
            CreatedBy = userId,
            UpdatedAt = now,
            ReasonForChange = "Remittance advice generated for self-billing invoice",
            VersionIndex = 1
        };

        var auditLog = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoice",
            EntityId = invoice.Id,
            Action = batchExecutionId != null ? "BATCH_SITE_INVOICE_COMPILED" : "INVOICE_COMPILED",
            UserId = userId,
            UserRoles = rolesText,
            Details = JsonSerializer.Serialize(new
            {
                invoice_id = invoice.Id,
                invoice_number = invoice.InvoiceNumber,
                study_id = studyId,
                site_id = siteId,
                billing_cycle = billingCycle,
                payable_count = eligiblePayables.Count,
                total_gross_amount = totalGross,
                total_holdback_amount = totalHoldback,
                total_net_amount = totalNet,
                batch_execution_id = batchExecutionId
            }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = $"Self-billing invoice {invoiceNumber} generated for site {siteId}",
            VersionIndex = 1
        };

        dbContext.SelfBillingInvoices.Add(invoice);
        dbContext.InvoiceLineItems.AddRange(lineItems);
        dbContext.RemittanceAdvices.Add(remittance);
        dbContext.PaymentAuditLogs.Add(auditLog);

        return (invoice, lineItems, remittance, auditLog);
    }

    private static async Task<IResult> CompileBillingCycle(
        BillingCycleCompilationRequest request,
        PaymentsDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.StudyId) || string.IsNullOrWhiteSpace(request.SiteId))
        {
            return Results.BadRequest(new { detail = "Both 'study_id' and 'site_id' are required for single site invoice compilation." });
        }

        if (!Reaches(httpContext, request.StudyId, request.SiteId))
        {
            return OutsideScope();
        }

        // Compiling with auto_approve also approves, so the caller must hold the approval permission too.
        if (request.AutoApprove && !PermissionMatrix.HasPermission(caller, "payments_invoice:approve"))
        {
            return ClinicalScopeGuard.Forbidden("Compiling with auto_approve needs payments_invoice:approve.");
        }

        DateTimeOffset now = clock.UtcNow;
        string periodStart = request.PeriodStart ?? $"{now.Year}-{now.Month:D2}-01";
        string periodEnd = request.PeriodEnd ?? $"{now.Year}-{now.Month:D2}-{DateTime.DaysInMonth(now.Year, now.Month):D2}";
        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        try
        {
            var (invoice, lineItems, remittance, _) = await CompileSiteInvoiceCoreAsync(
                dbContext,
                clock,
                request.StudyId,
                request.SiteId,
                request.BillingCycle,
                periodStart,
                periodEnd,
                request.AutoApprove,
                caller.UserId,
                rolesText
            );

            await dbContext.SaveChangesAsync();

            return Results.Created($"/api/v1/payments/invoices/{invoice.Id}", MapInvoiceToResponse(invoice, lineItems, remittance));
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
    }

    private static async Task<IResult> BatchCompileBillingCycles(
        BatchBillingCycleCompilationRequest request,
        PaymentsDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.StudyId))
        {
            return Results.BadRequest(new { detail = "Study Protocol ID ('study_id') is required for batch compilation." });
        }

        // A named site outside the caller's scope refuses the whole batch; discovered sites are narrowed to theirs.
        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, request.StudyId)
            || (request.SiteIds is { Count: > 0 } named && named.Any(site => !Reaches(httpContext, request.StudyId, site))))
        {
            return OutsideScope();
        }

        // Compiling with auto_approve also approves, so the caller must hold the approval permission too.
        if (request.AutoApprove && !PermissionMatrix.HasPermission(caller, "payments_invoice:approve"))
        {
            return ClinicalScopeGuard.Forbidden("Compiling with auto_approve needs payments_invoice:approve.");
        }

        DateTimeOffset now = clock.UtcNow;
        string batchExecutionId = $"BATCH-EXEC-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        string periodStart = request.PeriodStart ?? $"{now.Year}-{now.Month:D2}-01";
        string periodEnd = request.PeriodEnd ?? $"{now.Year}-{now.Month:D2}-{DateTime.DaysInMonth(now.Year, now.Month):D2}";
        string userId = caller.UserId;
        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);

        List<string> targetSiteIds;
        if (request.SiteIds != null && request.SiteIds.Count > 0)
        {
            targetSiteIds = request.SiteIds.Distinct().ToList();
        }
        else
        {
            var contractSites = await dbContext.BudgetContracts.AsNoTracking()
                .Where(c => c.StudyId == request.StudyId && c.IsActive)
                .Select(c => c.SiteId)
                .ToListAsync();

            var payableSites = await dbContext.Payables.AsNoTracking()
                .Where(p => p.StudyId == request.StudyId && p.Status != "INVOICED" && p.Status != "CANCELLED" && (!p.IsSdvGated || (p.SdvVerifiedAt != null && p.SdvVerifiedBy != null)))
                .Select(p => p.SiteId)
                .ToListAsync();

            targetSiteIds = contractSites.Concat(payableSites).Distinct().Where(site => Reaches(httpContext, request.StudyId, site)).OrderBy(s => s).ToList();
        }

        var siteResults = new List<SiteBatchCompilationResult>();
        var batchAuditLogs = new List<PaymentAuditLogDto>();
        var generatedInvoiceIds = new List<string>();

        int successfulSites = 0;
        int failedSites = 0;
        int totalInvoices = 0;
        int totalPayables = 0;
        decimal totalGrossSum = 0m;
        decimal totalHoldbackSum = 0m;
        decimal totalNetSum = 0m;

        foreach (var siteId in targetSiteIds)
        {
            try
            {
                var (invoice, _, _, auditLog) = await CompileSiteInvoiceCoreAsync(
                    dbContext,
                    clock,
                    request.StudyId,
                    siteId,
                    request.BillingCycle,
                    periodStart,
                    periodEnd,
                    request.AutoApprove,
                    userId,
                    rolesText,
                    batchExecutionId
                );

                await dbContext.SaveChangesAsync();

                successfulSites++;
                totalInvoices++;
                totalPayables += invoice.PayableCount;
                totalGrossSum += invoice.TotalGrossAmount;
                totalHoldbackSum += invoice.TotalHoldbackAmount;
                totalNetSum += invoice.TotalNetAmount;
                generatedInvoiceIds.Add(invoice.Id);

                siteResults.Add(new SiteBatchCompilationResult(
                    SiteId: siteId,
                    Success: true,
                    InvoiceId: invoice.Id,
                    InvoiceNumber: invoice.InvoiceNumber,
                    PayableCount: invoice.PayableCount,
                    TotalGrossAmount: invoice.TotalGrossAmount,
                    TotalHoldbackAmount: invoice.TotalHoldbackAmount,
                    TotalNetAmount: invoice.TotalNetAmount,
                    ErrorMessage: null
                ));

                batchAuditLogs.Add(new PaymentAuditLogDto(
                    Id: auditLog.Id,
                    EntityType: auditLog.EntityType,
                    EntityId: auditLog.EntityId,
                    Action: auditLog.Action,
                    UserId: auditLog.UserId,
                    Details: auditLog.Details,
                    CreatedAt: auditLog.CreatedAt,
                    CreatedBy: auditLog.CreatedBy
                ));
            }
            catch (Exception ex)
            {
                failedSites++;
                siteResults.Add(new SiteBatchCompilationResult(
                    SiteId: siteId,
                    Success: false,
                    InvoiceId: null,
                    InvoiceNumber: null,
                    PayableCount: 0,
                    TotalGrossAmount: 0m,
                    TotalHoldbackAmount: 0m,
                    TotalNetAmount: 0m,
                    ErrorMessage: ex.Message
                ));
            }
        }

        var masterAudit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoiceBatch",
            EntityId = batchExecutionId,
            Action = "BATCH_MILESTONE_INVOICE_COMPILED",
            UserId = userId,
            UserRoles = rolesText,
            Details = JsonSerializer.Serialize(new
            {
                batch_execution_id = batchExecutionId,
                study_id = request.StudyId,
                billing_cycle = request.BillingCycle,
                period_start = periodStart,
                period_end = periodEnd,
                total_sites_processed = targetSiteIds.Count,
                successful_sites_count = successfulSites,
                failed_sites_count = failedSites,
                total_invoices_generated = totalInvoices,
                total_payables_compiled = totalPayables,
                total_gross_amount = totalGrossSum,
                total_holdback_amount = totalHoldbackSum,
                total_net_amount = totalNetSum,
                invoice_ids = generatedInvoiceIds
            }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = $"Executed multi-site batch milestone compilation for study {request.StudyId}",
            VersionIndex = 1
        };

        dbContext.PaymentAuditLogs.Add(masterAudit);
        await dbContext.SaveChangesAsync();

        batchAuditLogs.Insert(0, new PaymentAuditLogDto(
            Id: masterAudit.Id,
            EntityType: masterAudit.EntityType,
            EntityId: masterAudit.EntityId,
            Action: masterAudit.Action,
            UserId: masterAudit.UserId,
            Details: masterAudit.Details,
            CreatedAt: masterAudit.CreatedAt,
            CreatedBy: masterAudit.CreatedBy
        ));

        var response = new BatchBillingCompilationResponse(
            BatchExecutionId: batchExecutionId,
            StudyId: request.StudyId,
            BillingCycle: request.BillingCycle,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            TotalSitesProcessed: targetSiteIds.Count,
            SuccessfulSitesCount: successfulSites,
            FailedSitesCount: failedSites,
            TotalInvoicesGenerated: totalInvoices,
            TotalPayablesCompiled: totalPayables,
            TotalGrossAmount: totalGrossSum,
            TotalHoldbackAmount: totalHoldbackSum,
            TotalNetAmount: totalNetSum,
            SiteResults: siteResults,
            AuditLogs: batchAuditLogs
        );

        return Results.Created($"/api/v1/payments/invoices?batch_execution_id={batchExecutionId}", response);
    }

    private static async Task<IResult> ApproveInvoice(string id, PaymentsDbContext dbContext, [FromServices] IClock clock, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        var inv = await dbContext.SelfBillingInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null || !Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Invoice with ID '{id}' not found." });
        }

        DateTimeOffset now = clock.UtcNow;
        try
        {
            inv.Approve(userId, now);
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }

        var audit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoice",
            EntityId = inv.Id,
            Action = "INVOICE_APPROVED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
            Details = JsonSerializer.Serialize(new { invoice_id = inv.Id, invoice_number = inv.InvoiceNumber, status = inv.Status }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "Self-billing invoice approved for ERP sync and payment processing",
            VersionIndex = inv.VersionIndex
        };
        dbContext.PaymentAuditLogs.Add(audit);
        await dbContext.SaveChangesAsync();

        var lineItems = await dbContext.InvoiceLineItems.AsNoTracking().Where(l => l.InvoiceId == id).ToListAsync();
        var remittance = await dbContext.RemittanceAdvices.AsNoTracking().FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(MapInvoiceToResponse(inv, lineItems, remittance));
    }

    private static async Task<IResult> SignRemittanceAdvice(string id, SignRemittanceRequest request, PaymentsDbContext dbContext, [FromServices] IClock clock, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        var remittance = await dbContext.RemittanceAdvices.FirstOrDefaultAsync(r => r.InvoiceId == id || r.Id == id);
        if (remittance == null || !Reaches(httpContext, remittance.StudyId, remittance.SiteId))
        {
            return Results.NotFound(new { detail = $"Remittance advice statement for invoice '{id}' not found." });
        }

        // Approval and remittance signing share the finance role, so two people must do them: the invoice's approver
        // may not also sign its remittance (separation of duties, #5361).
        string? approvedBy = await dbContext.SelfBillingInvoices.AsNoTracking()
            .Where(i => i.Id == remittance.InvoiceId)
            .Select(i => i.ApprovedBy)
            .FirstOrDefaultAsync();
        if (string.Equals(approvedBy, userId, StringComparison.Ordinal))
        {
            return Results.Conflict(new { detail = "The user who approved this invoice cannot also sign its remittance advice." });
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        DateTimeOffset now = clock.UtcNow;
        remittance.IsSigned = true;
        remittance.SignedBy = userId;
        remittance.SignedAt = now;
        remittance.SignerRole = rolesText;
        remittance.DispatchStatus = "SIGNED_SEALED";
        remittance.UpdatedAt = now;
        remittance.VersionIndex += 1;

        var audit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "RemittanceAdvice",
            EntityId = remittance.Id,
            Action = "REMITTANCE_ADVICE_SIGNED",
            UserId = userId,
            UserRoles = rolesText,
            Details = JsonSerializer.Serialize(new { remittance_id = remittance.Id, invoice_id = remittance.InvoiceId, signed_by = userId, reason = request.SignatureReason }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = request.SignatureReason ?? "21 CFR Part 11 electronic signature applied to remittance statement",
            VersionIndex = remittance.VersionIndex
        };
        dbContext.PaymentAuditLogs.Add(audit);
        await dbContext.SaveChangesAsync();

        return Results.Ok(new RemittanceAdviceDto(
            Id: remittance.Id,
            RemittanceNumber: remittance.RemittanceNumber,
            InvoiceId: remittance.InvoiceId,
            StudyId: remittance.StudyId,
            SiteId: remittance.SiteId,
            Currency: remittance.Currency,
            GrossAmount: remittance.GrossAmount,
            OverheadAmount: remittance.OverheadAmount,
            HoldbackAmount: remittance.HoldbackAmount,
            NetAmount: remittance.NetAmount,
            PdfSha256Hash: remittance.PdfSha256Hash,
            IsSealed: remittance.IsSealed,
            IsSigned: remittance.IsSigned,
            SignedBy: remittance.SignedBy,
            SignedAt: remittance.SignedAt,
            SignerRole: remittance.SignerRole,
            DispatchStatus: remittance.DispatchStatus,
            CreatedAt: remittance.CreatedAt,
            VersionIndex: remittance.VersionIndex
        ));
    }

    private static async Task<IResult> DownloadRemittancePdf(string id, PaymentsDbContext dbContext, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var remittance = await dbContext.RemittanceAdvices.AsNoTracking()
            .FirstOrDefaultAsync(r => r.InvoiceId == id || r.Id == id);

        if (remittance == null || !Reaches(httpContext, remittance.StudyId, remittance.SiteId))
        {
            return Results.NotFound(new { detail = $"Remittance statement for invoice '{id}' not found." });
        }

        string text = remittance.PdfContent ?? $"REMITTANCE STATEMENT {remittance.RemittanceNumber}\nGross: {remittance.GrossAmount:F2}\nNet: {remittance.NetAmount:F2}";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return Results.Bytes(bytes, contentType: "application/pdf", fileDownloadName: $"{remittance.RemittanceNumber}.pdf");
    }

    private static async Task<IResult> ReconcileInvoice(string id, ReconcileInvoiceRequest request, PaymentsDbContext dbContext, [FromServices] IClock clock, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        var inv = await dbContext.SelfBillingInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null || !Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Invoice with ID '{id}' not found." });
        }

        DateTimeOffset now = clock.UtcNow;
        try
        {
            inv.Reconcile(userId, now, request.PaymentReference, request.Notes, request.ErpSystem);
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }

        var audit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoice",
            EntityId = inv.Id,
            Action = "INVOICE_SETTLEMENT_RECONCILED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
            Details = JsonSerializer.Serialize(new { invoice_id = inv.Id, reference = request.PaymentReference, amount_paid = request.AmountPaid ?? inv.TotalNetAmount }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "Invoice settlement reconciled against bank wire statement",
            VersionIndex = inv.VersionIndex
        };
        dbContext.PaymentAuditLogs.Add(audit);
        await dbContext.SaveChangesAsync();

        var lineItems = await dbContext.InvoiceLineItems.AsNoTracking().Where(l => l.InvoiceId == id).ToListAsync();
        var remittance = await dbContext.RemittanceAdvices.AsNoTracking().FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(MapInvoiceToResponse(inv, lineItems, remittance));
    }

    private static async Task<IResult> ExportInvoiceToErp(string id, PaymentsDbContext dbContext, [FromServices] IClock clock, HttpContext httpContext, string? erp_system = "SAP")
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        var inv = await dbContext.SelfBillingInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null || !Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Invoice with ID '{id}' not found." });
        }

        DateTimeOffset now = clock.UtcNow;
        string sys = erp_system ?? "SAP";
        string erpRef = $"ERP-{sys}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        try
        {
            inv.ExportToErp(sys, erpRef, now);
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }

        var erpLog = new ErpSyncLog
        {
            Id = Guid.NewGuid().ToString(),
            InvoiceId = inv.Id,
            ErpSystem = sys,
            SyncDirection = "OUTBOUND_EXPORT",
            Status = "EXPORTED",
            ExternalReference = erpRef,
            Reconciled = false,
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = $"Self-billing invoice exported to {sys}",
            VersionIndex = 1
        };
        dbContext.ErpSyncLogs.Add(erpLog);

        var audit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoice",
            EntityId = inv.Id,
            Action = "INVOICE_ERP_EXPORTED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
            Details = JsonSerializer.Serialize(new { invoice_id = inv.Id, erp_system = sys, erp_reference_id = erpRef }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = $"Self-billing invoice synced to {sys} ERP gateway",
            VersionIndex = inv.VersionIndex
        };
        dbContext.PaymentAuditLogs.Add(audit);
        await dbContext.SaveChangesAsync();

        var lineItems = await dbContext.InvoiceLineItems.AsNoTracking().Where(l => l.InvoiceId == id).ToListAsync();
        var remittance = await dbContext.RemittanceAdvices.AsNoTracking().FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(MapInvoiceToResponse(inv, lineItems, remittance));
    }
}
