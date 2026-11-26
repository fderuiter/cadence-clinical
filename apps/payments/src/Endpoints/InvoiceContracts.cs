using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Payments.Endpoints;

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
