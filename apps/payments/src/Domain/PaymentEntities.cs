using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cadence.Apps.Payments.Domain;

public class BudgetContract
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string ContractName { get; set; } = string.Empty;
    public string TierLevel { get; set; } = "TIER_1_ACADEMIC";
    public string Currency { get; set; } = "USD";
    public decimal TotalBudget { get; set; }
    public decimal OverheadPercentage { get; set; }
    public decimal WithholdingPercentage { get; set; } = 10.0m;
    public string? EffectiveDate { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public bool IsActive { get; set; } = true;
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class FeeSchedule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ContractId { get; set; } = string.Empty;
    public string VisitCode { get; set; } = string.Empty;
    public string? ActivityId { get; set; }
    public string? FormId { get; set; }
    public string ProcedureCode { get; set; } = string.Empty;
    public string ProcedureName { get; set; } = string.Empty;
    public decimal BaseAmount { get; set; }
    public decimal OverheadPercentage { get; set; }
    public decimal WithholdingPercentage { get; set; } = 10.0m;
    public decimal PatientStipend { get; set; }
    public decimal TravelReimbursement { get; set; }
    public bool RequiresSdv { get; set; }
    public bool IsActive { get; set; } = true;
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class SplitPayeeConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ContractId { get; set; } = string.Empty;
    public string PayeeId { get; set; } = string.Empty;
    public string PayeeName { get; set; } = string.Empty;
    public string PayeeRole { get; set; } = string.Empty;
    public decimal AllocationPercentage { get; set; }
    public string? BankAccountReference { get; set; }
    public string? TaxIdentifier { get; set; }
    public bool IsActive { get; set; } = true;
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class Payable
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string VisitCode { get; set; } = string.Empty;
    public string? FormId { get; set; }
    public string? ProcedureCode { get; set; }
    public string? ProcedureName { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public string TriggerEventId { get; set; } = string.Empty;
    public string TriggerSource { get; set; } = "EDC_SERVICE";
    public decimal BaseAmount { get; set; }
    public decimal OverheadAmount { get; set; }
    public decimal PatientStipendAmount { get; set; }
    public decimal TravelReimbursementAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal WithholdingRate { get; set; } = 10.0m;
    public decimal WithholdingAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = "USD";

    private string _status = PayableStatus.TRIGGERED.ToString();
    public string Status
    {
        get => _status;
        init
        {
            if (!PaymentStatusExtensions.IsValidPayableStatus(value))
            {
                throw new InvalidStateTransitionException(
                    nameof(Payable),
                    _status,
                    value,
                    $"'{value}' is not a valid declared status for {nameof(Payable)}.");
            }
            _status = value.ToUpperInvariant();
        }
    }

    public bool IsSdvGated { get; set; }
    public DateTimeOffset? SdvVerifiedAt { get; set; }
    public string? SdvVerifiedBy { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;

    /// <summary>
    /// Verifies source data (SDV) for this payable.
    /// </summary>
    public void VerifySourceData(string verifiedBy, DateTimeOffset verifiedAt)
    {
        if (string.IsNullOrWhiteSpace(verifiedBy))
        {
            throw new ArgumentException("Verifier user identifier is required.", nameof(verifiedBy));
        }

        if (string.Equals(_status, PayableStatus.INVOICED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(Payable),
                _status,
                PayableStatus.VERIFIED.ToString(),
                $"Cannot verify source data for payable '{Id}' because it has already been invoiced.");
        }

        if (string.Equals(_status, PayableStatus.CANCELLED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(Payable),
                _status,
                PayableStatus.VERIFIED.ToString(),
                $"Cannot verify source data for payable '{Id}' because it is cancelled.");
        }

        SdvVerifiedBy = verifiedBy;
        SdvVerifiedAt = verifiedAt;
        _status = PayableStatus.VERIFIED.ToString();
        UpdatedAt = verifiedAt;
        VersionIndex += 1;
    }

    /// <summary>
    /// Transitions this payable to INVOICED status, verifying that source data verification
    /// rules are satisfied if SDV is gated.
    /// </summary>
    public void MarkInvoiced(DateTimeOffset invoicedAt)
    {
        if (string.Equals(_status, PayableStatus.INVOICED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(Payable),
                _status,
                PayableStatus.INVOICED.ToString(),
                $"Payable '{Id}' is already invoiced. Duplicate invoicing is prohibited.");
        }

        if (string.Equals(_status, PayableStatus.CANCELLED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(Payable),
                _status,
                PayableStatus.INVOICED.ToString(),
                $"Cannot invoice payable '{Id}' because it has been cancelled.");
        }

        if (IsSdvGated && (!SdvVerifiedAt.HasValue || string.IsNullOrWhiteSpace(SdvVerifiedBy)))
        {
            throw new InvalidStateTransitionException(
                nameof(Payable),
                _status,
                PayableStatus.INVOICED.ToString(),
                $"Payable '{Id}' cannot be invoiced because source data verification (SDV) is pending.");
        }

        _status = PayableStatus.INVOICED.ToString();
        UpdatedAt = invoicedAt;
        VersionIndex += 1;
    }

    /// <summary>
    /// Cancels this payable, preventing subsequent invoicing.
    /// </summary>
    public void Cancel(DateTimeOffset cancelledAt, string? reason = null)
    {
        if (string.Equals(_status, PayableStatus.INVOICED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(Payable),
                _status,
                PayableStatus.CANCELLED.ToString(),
                $"Cannot cancel payable '{Id}' because it has already been invoiced.");
        }

        _status = PayableStatus.CANCELLED.ToString();
        if (!string.IsNullOrWhiteSpace(reason))
        {
            ReasonForChange = reason;
        }
        UpdatedAt = cancelledAt;
        VersionIndex += 1;
    }
}

public class PayableAllocation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PayableId { get; set; } = string.Empty;
    public string PayeeId { get; set; } = string.Empty;
    public string PayeeName { get; set; } = string.Empty;
    public string PayeeRole { get; set; } = string.Empty;
    public decimal AllocationPercentage { get; set; }
    public decimal GrossShare { get; set; }
    public decimal WithholdingShare { get; set; }
    public decimal NetShare { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "ALLOCATED";
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class HoldbackReserve
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string PayableId { get; set; } = string.Empty;
    public decimal HoldbackAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "HELD";
    public string? ReleaseAuthorizedBy { get; set; }
    public DateTimeOffset? ReleaseAuthorizedAt { get; set; }
    public string? DmSignoffBy { get; set; }
    public DateTimeOffset? DmSignoffAt { get; set; }
    public string? QualitySignoffBy { get; set; }
    public DateTimeOffset? QualitySignoffAt { get; set; }
    public string? ReleaseNotes { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class PaymentAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? PreviousStateJson { get; set; }
    public string? NewStateJson { get; set; }
    public string? Details { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserRoles { get; set; } = string.Empty;
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class SelfBillingInvoice
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string InvoiceNumber { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = "MONTHLY";
    public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public decimal TotalBaseAmount { get; set; }
    public decimal TotalOverheadAmount { get; set; }
    public decimal TotalPatientStipend { get; set; }
    public decimal TotalTravelReimbursement { get; set; }
    public decimal TotalGrossAmount { get; set; }
    public decimal TotalHoldbackAmount { get; set; }
    public decimal TotalNetAmount { get; set; }
    public int PayableCount { get; set; }

    private string _status = SelfBillingInvoiceStatus.DRAFT.ToString();
    public string Status
    {
        get => _status;
        init
        {
            if (!PaymentStatusExtensions.IsValidInvoiceStatus(value))
            {
                throw new InvalidStateTransitionException(
                    nameof(SelfBillingInvoice),
                    _status,
                    value,
                    $"'{value}' is not a valid declared status for {nameof(SelfBillingInvoice)}.");
            }
            _status = value.ToUpperInvariant();
        }
    }

    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ErpSystem { get; set; } = "SAP";
    public string? ErpReferenceId { get; set; }
    public string ErpSyncStatus { get; set; } = "PENDING";
    public DateTimeOffset? ErpSyncedAt { get; set; }
    public string? ReconciledBy { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }
    public string? ReconciliationReference { get; set; }
    public string? ReconciliationNotes { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;

    /// <summary>
    /// Approves this draft invoice, authorizing it for ERP export and settlement.
    /// </summary>
    public void Approve(string approvedBy, DateTimeOffset approvedAt)
    {
        if (string.IsNullOrWhiteSpace(approvedBy))
        {
            throw new ArgumentException("Approver user identifier is required.", nameof(approvedBy));
        }

        if (!string.Equals(_status, SelfBillingInvoiceStatus.DRAFT.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(SelfBillingInvoice),
                _status,
                SelfBillingInvoiceStatus.APPROVED.ToString(),
                $"Invoice '{InvoiceNumber}' cannot be approved because it is in status '{_status}', but '{SelfBillingInvoiceStatus.DRAFT}' is required.");
        }

        _status = SelfBillingInvoiceStatus.APPROVED.ToString();
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
        UpdatedAt = approvedAt;
        VersionIndex += 1;
    }

    /// <summary>
    /// Records export to an external ERP system. Requires APPROVED or RECONCILED status.
    /// Blocks export if the invoice is in DRAFT, CANCELLED, or any unapproved status.
    /// </summary>
    public void ExportToErp(string erpSystem, string erpReferenceId, DateTimeOffset exportedAt)
    {
        if (string.IsNullOrWhiteSpace(erpSystem))
        {
            throw new ArgumentException("ERP system identifier is required.", nameof(erpSystem));
        }

        if (string.IsNullOrWhiteSpace(erpReferenceId))
        {
            throw new ArgumentException("ERP reference identifier is required.", nameof(erpReferenceId));
        }

        if (!string.Equals(_status, SelfBillingInvoiceStatus.APPROVED.ToString(), StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_status, SelfBillingInvoiceStatus.RECONCILED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(SelfBillingInvoice),
                _status,
                "EXPORTED",
                $"Invoice '{InvoiceNumber}' must be in '{SelfBillingInvoiceStatus.APPROVED}' status before ERP export, but is currently in '{_status}'.");
        }

        ErpSystem = erpSystem;
        ErpReferenceId = erpReferenceId;
        ErpSyncStatus = "EXPORTED";
        ErpSyncedAt = exportedAt;
        UpdatedAt = exportedAt;
        VersionIndex += 1;
    }

    /// <summary>
    /// Reconciles settlement against bank wire or ERP clearing statement. Requires APPROVED status.
    /// </summary>
    public void Reconcile(
        string reconciledBy,
        DateTimeOffset reconciledAt,
        string? reconciliationReference,
        string? reconciliationNotes,
        string? erpSystem = null)
    {
        if (string.IsNullOrWhiteSpace(reconciledBy))
        {
            throw new ArgumentException("Reconciler user identifier is required.", nameof(reconciledBy));
        }

        if (!string.Equals(_status, SelfBillingInvoiceStatus.APPROVED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(SelfBillingInvoice),
                _status,
                SelfBillingInvoiceStatus.RECONCILED.ToString(),
                $"Invoice '{InvoiceNumber}' must be in '{SelfBillingInvoiceStatus.APPROVED}' status before settlement reconciliation, but is currently in '{_status}'.");
        }

        _status = SelfBillingInvoiceStatus.RECONCILED.ToString();
        ReconciledBy = reconciledBy;
        ReconciledAt = reconciledAt;
        ReconciliationReference = reconciliationReference;
        ReconciliationNotes = reconciliationNotes;
        if (!string.IsNullOrWhiteSpace(erpSystem))
        {
            ErpSystem = erpSystem;
        }
        UpdatedAt = reconciledAt;
        VersionIndex += 1;
    }

    /// <summary>
    /// Cancels this invoice. Cannot cancel an already reconciled invoice.
    /// </summary>
    public void Cancel(DateTimeOffset cancelledAt, string? reason = null)
    {
        if (string.Equals(_status, SelfBillingInvoiceStatus.RECONCILED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(SelfBillingInvoice),
                _status,
                SelfBillingInvoiceStatus.CANCELLED.ToString(),
                $"Cannot cancel invoice '{InvoiceNumber}' because it has already been reconciled.");
        }

        _status = SelfBillingInvoiceStatus.CANCELLED.ToString();
        if (!string.IsNullOrWhiteSpace(reason))
        {
            ReasonForChange = reason;
        }
        UpdatedAt = cancelledAt;
        VersionIndex += 1;
    }
}

public class InvoiceLineItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string InvoiceId { get; set; } = string.Empty;
    public string? PayableId { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public string VisitCode { get; set; } = string.Empty;
    public string? ProcedureCode { get; set; }
    public string? ProcedureName { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal OverheadAmount { get; set; }
    public decimal PatientStipend { get; set; }
    public decimal TravelReimbursement { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal WithholdingRate { get; set; } = 10.0m;
    public decimal WithholdingAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class RemittanceAdvice
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RemittanceNumber { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public decimal GrossAmount { get; set; }
    public decimal OverheadAmount { get; set; }
    public decimal HoldbackAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string PdfSha256Hash { get; set; } = string.Empty;
    public string? PdfContent { get; set; }
    public bool IsSealed { get; set; } = true;
    public bool IsSigned { get; set; }
    public string? SignedBy { get; set; }
    public DateTimeOffset? SignedAt { get; set; }
    public string? SignerRole { get; set; }
    public string? SignatureManifestJson { get; set; }
    public string? DispatchedTo { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public string DispatchStatus { get; set; } = "PENDING";
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class ErpSyncLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string InvoiceId { get; set; } = string.Empty;
    public string ErpSystem { get; set; } = string.Empty;
    public string SyncDirection { get; set; } = "OUTBOUND_EXPORT";
    public string Status { get; set; } = "PENDING";
    public string? PayloadJson { get; set; }
    public string? ResponseDataJson { get; set; }
    public string? ExternalReference { get; set; }
    public bool Reconciled { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class ParticipantWallet
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; }
    public decimal LifetimeStipends { get; set; }
    public decimal LifetimeReimbursements { get; set; }
    public decimal LifetimeDisbursements { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "ACTIVE";
    public decimal TaxableYtd { get; set; }
    public bool Is1099ThresholdExceeded { get; set; }
    public string W9Status { get; set; } = "NOT_REQUIRED";
    public DateTimeOffset? W9SubmittedAt { get; set; }
    public DateTimeOffset? W9VerifiedAt { get; set; }
    public string? TaxIdLastFour { get; set; }
    public string CardType { get; set; } = "RELOADABLE_DEBIT";
    public string CardStatus { get; set; } = "ACTIVE";
    /// <summary>
    /// The card processor's opaque reference for the participant's virtual card (never the card number or a raw
    /// card token). Encrypted at rest (#4770).
    /// </summary>
    public string? VirtualCardProcessorReference { get; set; }
    public string? VirtualCardMask { get; set; }
    public string? VirtualCardExpiry { get; set; }
    public decimal? AntiCoercionCap { get; set; }
    public decimal? AutoDisbursementThreshold { get; set; }
    /// <summary>
    /// The payment processor's opaque reference for the participant's bank account. Routing and account numbers
    /// are held by the processor and are not stored here; only their masks are (#4770). Encrypted at rest.
    /// </summary>
    public string? AchProcessorReference { get; set; }
    /// <summary>Masked routing number for display (last four digits only, for example <c>*****6789</c>).</summary>
    public string? AchRoutingMask { get; set; }
    public string? AchAccountMask { get; set; }
    public string? AchAccountHolder { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class ParticipantStipendRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string VisitCode { get; set; } = string.Empty;
    public string VisitName { get; set; } = string.Empty;
    public string? ProcedureCode { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public bool RequiresVerification { get; set; } = true;
    public bool Taxable { get; set; }
    public decimal? AntiCoercionCap { get; set; }
    public decimal? AutoDisbursementThreshold { get; set; }
    public bool IsMilestone { get; set; }
    public string? MilestoneCode { get; set; }
    public bool IsActive { get; set; } = true;
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class ParticipantTransaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WalletId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string ReferenceId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class ParticipantExpenseClaim
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WalletId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string ExpenseDate { get; set; } = string.Empty;
    public string Category { get; set; } = "TRANSIT";
    public string MerchantName { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal TaxDeduction { get; set; }
    public decimal NetAmount { get; set; }

    private string _status = ParticipantExpenseClaimStatus.PENDING_REVIEW.ToString();
    public string Status
    {
        get => _status;
        init
        {
            if (!PaymentStatusExtensions.IsValidExpenseClaimStatus(value))
            {
                throw new InvalidStateTransitionException(
                    nameof(ParticipantExpenseClaim),
                    _status,
                    value,
                    $"'{value}' is not a valid declared status for {nameof(ParticipantExpenseClaim)}.");
            }
            _status = value.ToUpperInvariant();
        }
    }

    public string? ReceiptImageName { get; set; }
    public string? ReceiptOcrDataJson { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
    public string? DisbursementReference { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;

    private bool _walletCredited;
    /// <summary>
    /// Indicates whether a wallet credit operation has already been applied for this claim.
    /// </summary>
    [NotMapped]
    public bool IsWalletCredited => _walletCredited || (!string.IsNullOrEmpty(DisbursementReference) && string.Equals(_status, ParticipantExpenseClaimStatus.APPROVED.ToString(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Approves this expense claim, authorizing reimbursement.
    /// Rejects multiple approvals or reviews from non-pending states.
    /// </summary>
    public void Approve(string reviewerId, DateTimeOffset reviewedAt, string? reviewNotes = null, decimal? adjustedGrossAmount = null)
    {
        if (string.IsNullOrWhiteSpace(reviewerId))
        {
            throw new ArgumentException("Reviewer user identifier is required.", nameof(reviewerId));
        }

        if (!string.Equals(_status, ParticipantExpenseClaimStatus.PENDING_REVIEW.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(ParticipantExpenseClaim),
                _status,
                ParticipantExpenseClaimStatus.APPROVED.ToString(),
                $"Expense claim '{Id}' cannot be approved because it is in status '{_status}', but '{ParticipantExpenseClaimStatus.PENDING_REVIEW}' is required.");
        }

        if (adjustedGrossAmount.HasValue)
        {
            GrossAmount = adjustedGrossAmount.Value;
            NetAmount = Math.Round(adjustedGrossAmount.Value - TaxDeduction, 2);
        }

        _status = ParticipantExpenseClaimStatus.APPROVED.ToString();
        ReviewedBy = reviewerId;
        ReviewedAt = reviewedAt;
        ReviewNotes = reviewNotes;
        UpdatedAt = reviewedAt;
        VersionIndex += 1;
    }

    /// <summary>
    /// Rejects this expense claim. Permits review action only from PENDING_REVIEW.
    /// </summary>
    public void Reject(string reviewerId, DateTimeOffset reviewedAt, string? reviewNotes = null)
    {
        if (string.IsNullOrWhiteSpace(reviewerId))
        {
            throw new ArgumentException("Reviewer user identifier is required.", nameof(reviewerId));
        }

        if (!string.Equals(_status, ParticipantExpenseClaimStatus.PENDING_REVIEW.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(ParticipantExpenseClaim),
                _status,
                ParticipantExpenseClaimStatus.REJECTED.ToString(),
                $"Expense claim '{Id}' cannot be rejected because it is in status '{_status}', but '{ParticipantExpenseClaimStatus.PENDING_REVIEW}' is required.");
        }

        _status = ParticipantExpenseClaimStatus.REJECTED.ToString();
        ReviewedBy = reviewerId;
        ReviewedAt = reviewedAt;
        ReviewNotes = reviewNotes;
        UpdatedAt = reviewedAt;
        VersionIndex += 1;
    }

    /// <summary>
    /// Records that the participant wallet was credited for this approved claim.
    /// Forbids multiple wallet credit operations.
    /// </summary>
    public void RecordWalletCredit()
    {
        if (!string.Equals(_status, ParticipantExpenseClaimStatus.APPROVED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(ParticipantExpenseClaim),
                _status,
                "CREDITED",
                $"Expense claim '{Id}' must be approved before crediting wallet, but is currently in '{_status}'.");
        }

        if (_walletCredited)
        {
            throw new InvalidStateTransitionException(
                nameof(ParticipantExpenseClaim),
                _status,
                "CREDITED",
                $"Wallet credit has already been processed for expense claim '{Id}'. Duplicate wallet credits are prohibited.");
        }

        _walletCredited = true;
    }

    /// <summary>
    /// Records a disbursement dispatch reference for this approved claim.
    /// Forbids duplicate disbursement assignment.
    /// </summary>
    public void RecordDisbursement(string disbursementReference)
    {
        if (string.IsNullOrWhiteSpace(disbursementReference))
        {
            throw new ArgumentException("Disbursement reference cannot be empty.", nameof(disbursementReference));
        }

        if (!string.Equals(_status, ParticipantExpenseClaimStatus.APPROVED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(ParticipantExpenseClaim),
                _status,
                "DISBURSED",
                $"Expense claim '{Id}' must be approved before disbursement, but is currently in '{_status}'.");
        }

        if (!string.IsNullOrEmpty(DisbursementReference))
        {
            throw new InvalidStateTransitionException(
                nameof(ParticipantExpenseClaim),
                _status,
                "DISBURSED",
                $"Expense claim '{Id}' already has a disbursement reference '{DisbursementReference}'. Duplicate disbursement is prohibited.");
        }

        DisbursementReference = disbursementReference;
    }
}

public class ParticipantDisbursement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WalletId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string DisbursementType { get; set; } = "VIRTUAL_CARD";
    public string ReferenceNumber { get; set; } = string.Empty;

    private string _status = ParticipantDisbursementStatus.DISPATCHED.ToString();
    public string Status
    {
        get => _status;
        init
        {
            if (!PaymentStatusExtensions.IsValidDisbursementStatus(value))
            {
                throw new InvalidStateTransitionException(
                    nameof(ParticipantDisbursement),
                    _status,
                    value,
                    $"'{value}' is not a valid declared status for {nameof(ParticipantDisbursement)}.");
            }
            _status = value.ToUpperInvariant();
        }
    }

    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public int VersionIndex { get; set; } = 1;

    /// <summary>
    /// Progresses lifecycle from DISPATCHED to SETTLED upon network confirmation.
    /// Forbids duplicate settlement or settling from non-dispatched status.
    /// </summary>
    public void Settle(DateTimeOffset settledAt)
    {
        if (string.Equals(_status, ParticipantDisbursementStatus.SETTLED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(ParticipantDisbursement),
                _status,
                ParticipantDisbursementStatus.SETTLED.ToString(),
                $"Disbursement '{Id}' (ref: {ReferenceNumber}) is already settled. Duplicate settlement is prohibited.");
        }

        if (!string.Equals(_status, ParticipantDisbursementStatus.DISPATCHED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(ParticipantDisbursement),
                _status,
                ParticipantDisbursementStatus.SETTLED.ToString(),
                $"Disbursement '{Id}' (ref: {ReferenceNumber}) in status '{_status}' cannot be settled. Only dispatched disbursements can be settled.");
        }

        _status = ParticipantDisbursementStatus.SETTLED.ToString();
        VersionIndex += 1;
    }

    /// <summary>
    /// Marks disbursement as failed. Only permissible from DISPATCHED status.
    /// </summary>
    public void Fail(DateTimeOffset failedAt, string? reason = null)
    {
        if (!string.Equals(_status, ParticipantDisbursementStatus.DISPATCHED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(ParticipantDisbursement),
                _status,
                ParticipantDisbursementStatus.FAILED.ToString(),
                $"Disbursement '{Id}' in status '{_status}' cannot be marked failed. Only dispatched disbursements can fail.");
        }

        _status = ParticipantDisbursementStatus.FAILED.ToString();
        VersionIndex += 1;
    }

    /// <summary>
    /// Cancels disbursement. Only permissible from DISPATCHED status.
    /// </summary>
    public void Cancel(DateTimeOffset cancelledAt, string? reason = null)
    {
        if (!string.Equals(_status, ParticipantDisbursementStatus.DISPATCHED.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateTransitionException(
                nameof(ParticipantDisbursement),
                _status,
                ParticipantDisbursementStatus.CANCELLED.ToString(),
                $"Disbursement '{Id}' in status '{_status}' cannot be cancelled. Only dispatched disbursements can be cancelled.");
        }

        _status = ParticipantDisbursementStatus.CANCELLED.ToString();
        VersionIndex += 1;
    }
}
