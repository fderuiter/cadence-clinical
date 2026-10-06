using System;

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
    public string Status { get; set; } = "TRIGGERED";
    public bool IsSdvGated { get; set; }
    public DateTimeOffset? SdvVerifiedAt { get; set; }
    public string? SdvVerifiedBy { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
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
    public string Status { get; set; } = "DRAFT";
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
    public string Status { get; set; } = "PENDING_REVIEW";
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
}

public class ParticipantDisbursement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WalletId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string DisbursementType { get; set; } = "VIRTUAL_CARD";
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "DISPATCHED";
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public int VersionIndex { get; set; } = 1;
}
