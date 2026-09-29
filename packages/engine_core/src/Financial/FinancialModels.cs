using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Core.Domain.Financial;

/// <summary>
/// Event triggers initiating payable line items.
/// @req:PRD-SYS-053
/// </summary>
public enum TriggerType
{
    ENROLLMENT,
    VISIT_COMPLETED,
    FORM_APPROVED,
    QUERY_RESOLVED,
    AE_REPORTED,
    UNSCHEDULED_PROCEDURE,
    PASSTHROUGH_EXPENSE,
    MANUAL_ADJUSTMENT,
    MILESTONE_ACHIEVED
}

/// <summary>
/// Lifecycle status of payable ledger line items.
/// @req:PRD-SYS-053
/// </summary>
public enum PayableStatus
{
    PENDING,
    APPROVED,
    INVOICED,
    PAID,
    HELD,
    CANCELLED
}

/// <summary>
/// Multi-tiered cumulative or bracketed overhead rate definition.
/// @req:PRD-SYS-053
/// </summary>
public record OverheadTier(
    string TierId,
    decimal OverheadRate,
    decimal MinThreshold = 0.0m,
    decimal? MaxThreshold = null
);

/// <summary>
/// Split-payee distribution allocation rule.
/// @req:PRD-SYS-053
/// </summary>
public record PayeeSplit(
    string PayeeId,
    string PayeeName,
    decimal Percentage,
    bool IsPrimary = false,
    string? AccountReference = null
);

/// <summary>
/// Holdback retention reserve policy.
/// @req:PRD-SYS-053
/// </summary>
public record HoldbackReserveRule(
    decimal HoldbackRate = 0.10m,
    decimal? CapAmount = null,
    string ReleaseTrigger = "STUDY_CLOSEOUT"
);

/// <summary>
/// Per-procedure, per-visit, or milestone fee definition.
/// @req:PRD-SYS-053
/// </summary>
public record FeeMatrixItem(
    string ItemCode,
    string Description,
    TriggerType TriggerType,
    decimal BaseAmount,
    string? EventIdentifier = null,
    bool OverheadApplicable = true,
    bool WithholdingApplicable = true,
    bool HoldbackApplicable = true,
    int? MaxAllowableEvents = null
);

/// <summary>
/// Site Investigator Grant Agreement configuration.
/// @req:PRD-SYS-053
/// </summary>
public record GrantContract(
    string ContractId,
    string StudyId,
    string SiteId,
    DateTime EffectiveDate,
    string Currency = "USD",
    DateTime? ExpirationDate = null,
    IReadOnlyList<OverheadTier>? OverheadTiers = null,
    decimal DefaultOverheadRate = 0.0m,
    decimal DefaultWithholdingRate = 0.0m,
    HoldbackReserveRule? HoldbackRule = null,
    IReadOnlyList<PayeeSplit>? PayeeSplits = null,
    IReadOnlyList<FeeMatrixItem>? FeeMatrix = null,
    int PaymentTermsDays = 30,
    bool IsActive = true
)
{
    public IReadOnlyList<OverheadTier> OverheadTiers { get; init; } = OverheadTiers ?? Array.Empty<OverheadTier>();
    public HoldbackReserveRule HoldbackRule { get; init; } = HoldbackRule ?? new HoldbackReserveRule();
    public IReadOnlyList<PayeeSplit> PayeeSplits { get; init; } = PayeeSplits ?? Array.Empty<PayeeSplit>();
    public IReadOnlyList<FeeMatrixItem> FeeMatrix { get; init; } = FeeMatrix ?? Array.Empty<FeeMatrixItem>();

    public Result<bool> ValidatePayeeSplits()
    {
        if (PayeeSplits.Count == 0)
        {
            return true;
        }

        decimal total = PayeeSplits.Sum(s => s.Percentage);
        if (total < 0.999m || total > 1.001m)
        {
            return DomainError.Validation("ERR_PAYEE_SPLIT_SUM", $"Payee splits must sum to 1.0 (100%), got {total:F4}");
        }

        return true;
    }
}

/// <summary>
/// Inbound clinical execution milestone event.
/// @req:PRD-SYS-053
/// </summary>
public record ClinicalEvent(
    string EventId,
    string StudyId,
    string SiteId,
    TriggerType TriggerType,
    DateTime OccurredAt,
    string? SubjectId = null,
    string? VisitId = null,
    string? FormId = null,
    string? ProcedureCode = null,
    decimal? CustomAmount = null,
    IReadOnlyDictionary<string, object?>? Metadata = null
);

/// <summary>
/// Detailed mathematical breakdown of line item deductions and additions.
/// @req:PRD-SYS-053
/// </summary>
public record PayableBreakdown(
    decimal BaseAmount,
    decimal OverheadAmount,
    decimal GrossAmount,
    decimal WithholdingAmount,
    decimal HoldbackAmount,
    decimal NetPayable,
    decimal EffectiveOverheadRate,
    decimal WithholdingRate,
    decimal HoldbackRate
);

/// <summary>
/// Computed payable transaction line item with 21 CFR Part 11 audit hash.
/// @req:PRD-SYS-053
/// </summary>
public record PayableLineItem(
    string LineItemId,
    string EventId,
    string ContractId,
    string ItemCode,
    string ItemDescription,
    string PayeeId,
    string PayeeName,
    decimal SplitPercentage,
    string Currency,
    PayableBreakdown Breakdown,
    DateTime OccurredAt,
    DateTime CalculatedAt,
    string AuditHash,
    PayableStatus Status = PayableStatus.PENDING
);

/// <summary>
/// Holdback retention escrow tracking account.
/// @req:PRD-SYS-053
/// </summary>
public record HoldbackLedgerAccount(
    string ContractId,
    string SiteId,
    string Currency,
    decimal AccumulatedHoldback = 0.0m,
    decimal ReleasedHoldback = 0.0m,
    decimal CurrentBalance = 0.0m
);

/// <summary>
/// Consolidated payable ledger statement with cryptographic digest.
/// @req:PRD-SYS-053
/// </summary>
public record LedgerSummary(
    string SummaryId,
    string StudyId,
    string SiteId,
    string ContractId,
    string Currency,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int TotalEventsProcessed,
    int TotalLineItems,
    decimal TotalBaseAmount,
    decimal TotalOverheadAmount,
    decimal TotalGrossPayable,
    decimal TotalWithholdingDeducted,
    decimal TotalHoldbackReserved,
    decimal TotalNetPayable,
    IReadOnlyList<PayableLineItem> LineItems,
    HoldbackLedgerAccount HoldbackAccount,
    IReadOnlyDictionary<string, decimal> PayeeSubtotals,
    string CryptographicDigest
);
