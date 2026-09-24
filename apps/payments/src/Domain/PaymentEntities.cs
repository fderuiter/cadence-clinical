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
    public double TotalBudget { get; set; }
    public double OverheadPercentage { get; set; }
    public double WithholdingPercentage { get; set; } = 10.0;
    public string? EffectiveDate { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    public double BaseAmount { get; set; }
    public double OverheadPercentage { get; set; }
    public double WithholdingPercentage { get; set; } = 10.0;
    public double PatientStipend { get; set; }
    public double TravelReimbursement { get; set; }
    public bool RequiresSdv { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    public double AllocationPercentage { get; set; }
    public string? BankAccountReference { get; set; }
    public string? TaxIdentifier { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    public double BaseAmount { get; set; }
    public double OverheadAmount { get; set; }
    public double PatientStipendAmount { get; set; }
    public double TravelReimbursementAmount { get; set; }
    public double GrossAmount { get; set; }
    public double WithholdingRate { get; set; } = 10.0;
    public double WithholdingAmount { get; set; }
    public double NetAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "TRIGGERED";
    public bool IsSdvGated { get; set; }
    public DateTime? SdvVerifiedAt { get; set; }
    public string? SdvVerifiedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    public double AllocationPercentage { get; set; }
    public double GrossShare { get; set; }
    public double WithholdingShare { get; set; }
    public double NetShare { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "ALLOCATED";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    public double HoldbackAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "HELD";
    public string? ReleaseAuthorizedBy { get; set; }
    public DateTime? ReleaseAuthorizedAt { get; set; }
    public string? DmSignoffBy { get; set; }
    public DateTime? DmSignoffAt { get; set; }
    public string? QualitySignoffBy { get; set; }
    public DateTime? QualitySignoffAt { get; set; }
    public string? ReleaseNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
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
    public double TotalBaseAmount { get; set; }
    public double TotalOverheadAmount { get; set; }
    public double TotalPatientStipend { get; set; }
    public double TotalTravelReimbursement { get; set; }
    public double TotalGrossAmount { get; set; }
    public double TotalHoldbackAmount { get; set; }
    public double TotalNetAmount { get; set; }
    public int PayableCount { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ErpSystem { get; set; } = "SAP";
    public string? ErpReferenceId { get; set; }
    public string ErpSyncStatus { get; set; } = "PENDING";
    public DateTime? ErpSyncedAt { get; set; }
    public string? ReconciledBy { get; set; }
    public DateTime? ReconciledAt { get; set; }
    public string? ReconciliationReference { get; set; }
    public string? ReconciliationNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    public double BaseAmount { get; set; }
    public double OverheadAmount { get; set; }
    public double PatientStipend { get; set; }
    public double TravelReimbursement { get; set; }
    public double GrossAmount { get; set; }
    public double WithholdingRate { get; set; } = 10.0;
    public double WithholdingAmount { get; set; }
    public double NetAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    public double GrossAmount { get; set; }
    public double OverheadAmount { get; set; }
    public double HoldbackAmount { get; set; }
    public double NetAmount { get; set; }
    public string PdfSha256Hash { get; set; } = string.Empty;
    public string? PdfContent { get; set; }
    public bool IsSealed { get; set; } = true;
    public bool IsSigned { get; set; }
    public string? SignedBy { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? SignerRole { get; set; }
    public string? SignatureManifestJson { get; set; }
    public string? DispatchedTo { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public string DispatchStatus { get; set; } = "PENDING";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class ParticipantWallet
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public double CurrentBalance { get; set; }
    public double LifetimeStipends { get; set; }
    public double LifetimeReimbursements { get; set; }
    public double LifetimeDisbursements { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "ACTIVE";
    public double TaxableYtd { get; set; }
    public bool Is1099ThresholdExceeded { get; set; }
    public string W9Status { get; set; } = "NOT_REQUIRED";
    public DateTime? W9SubmittedAt { get; set; }
    public DateTime? W9VerifiedAt { get; set; }
    public string? TaxIdLastFour { get; set; }
    public string CardType { get; set; } = "RELOADABLE_DEBIT";
    public string CardStatus { get; set; } = "ACTIVE";
    public string? VirtualCardToken { get; set; }
    public string? VirtualCardMask { get; set; }
    public string? VirtualCardExpiry { get; set; }
    public double? AntiCoercionCap { get; set; }
    public string? AchRoutingNumber { get; set; }
    public string? AchAccountMask { get; set; }
    public string? AchAccountHolder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}
