using System;
using System.Collections.Generic;

using Cadence.Kernel;

namespace Cadence.Apps.Payments.Domain.Ledger;

/// <summary>The clinical events that can create a payable line item.</summary>
public enum PaymentTriggerType
{
    /// <summary>A participant enrolled.</summary>
    Enrollment,

    /// <summary>A visit was completed.</summary>
    VisitCompleted,

    /// <summary>A form was approved.</summary>
    FormApproved,

    /// <summary>A data query was resolved.</summary>
    QueryResolved,

    /// <summary>An adverse event was reported.</summary>
    AeReported,

    /// <summary>A procedure outside the visit schedule was performed.</summary>
    UnscheduledProcedure,

    /// <summary>A pass-through expense was incurred.</summary>
    PassthroughExpense,

    /// <summary>A manual adjustment; paid at the event's own amount when no fee matrix item matches.</summary>
    ManualAdjustment,

    /// <summary>A contract milestone was achieved.</summary>
    MilestoneAchieved,
}

/// <summary>Lifecycle of a payable line item.</summary>
public enum PayableLineStatus
{
    /// <summary>Calculated from an event and waiting for approval.</summary>
    Pending,

    /// <summary>Approved for payment.</summary>
    Approved,

    /// <summary>On an invoice.</summary>
    Invoiced,

    /// <summary>Paid.</summary>
    Paid,

    /// <summary>Held back.</summary>
    Held,

    /// <summary>Cancelled.</summary>
    Cancelled,
}

/// <summary>
/// One marginal overhead bracket: <paramref name="OverheadRate"/> applies to the part of cumulative base spend between
/// <paramref name="MinThreshold"/> and <paramref name="MaxThreshold"/> (no upper bound when null).
/// </summary>
public sealed record OverheadTier(string TierId, decimal OverheadRate, decimal MinThreshold = 0m, decimal? MaxThreshold = null);

/// <summary>
/// A payee's share of every payable. <paramref name="IsPrimary"/> marks the payee that absorbs the rounding difference.
/// </summary>
public sealed record PayeeSplit(
    string PayeeId,
    string PayeeName,
    decimal Percentage,
    bool IsPrimary = false,
    string? AccountReference = null);

/// <summary>
/// The share of each gross amount kept in reserve until <paramref name="ReleaseTrigger"/>, up to
/// <paramref name="CapAmount"/> held at once when a cap is set.
/// </summary>
public sealed record HoldbackReserveRule(decimal HoldbackRate = 0.10m, decimal? CapAmount = null, string ReleaseTrigger = "STUDY_CLOSEOUT");

/// <summary>
/// A fee the contract pays when an event of <paramref name="TriggerType"/> occurs. When
/// <paramref name="EventIdentifier"/> is set, the event's procedure code, visit, form or identifier (the first one
/// present) must equal it. <paramref name="MaxAllowableEvents"/> caps how often it is paid per participant.
/// </summary>
public sealed record FeeMatrixItem(
    string ItemCode,
    string Description,
    PaymentTriggerType TriggerType,
    decimal BaseAmount,
    string? EventIdentifier = null,
    bool OverheadApplicable = true,
    bool WithholdingApplicable = true,
    bool HoldbackApplicable = true,
    int? MaxAllowableEvents = null);

/// <summary>A site's investigator grant agreement: its fee matrix, overhead, withholding, holdback and payees.</summary>
public sealed record GrantContract
{
    /// <summary>The contract's identifier.</summary>
    public required string ContractId { get; init; }

    /// <summary>The study the contract pays for.</summary>
    public required StudyId StudyId { get; init; }

    /// <summary>The site the contract pays.</summary>
    public required SiteId SiteId { get; init; }

    /// <summary>ISO 4217 currency of every amount.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>When the contract takes effect.</summary>
    public required DateTimeOffset EffectiveDate { get; init; }

    /// <summary>When the contract expires, if it does.</summary>
    public DateTimeOffset? ExpirationDate { get; init; }

    /// <summary>Marginal overhead brackets; when empty, <see cref="DefaultOverheadRate"/> applies to everything.</summary>
    public IReadOnlyList<OverheadTier> OverheadTiers { get; init; } = [];

    /// <summary>The flat overhead rate used when the contract has no tiers.</summary>
    public decimal DefaultOverheadRate { get; init; }

    /// <summary>The withholding rate applied to each gross amount.</summary>
    public decimal DefaultWithholdingRate { get; init; }

    /// <summary>The holdback reserve policy.</summary>
    public HoldbackReserveRule HoldbackRule { get; init; } = new();

    /// <summary>How each payable is split between payees; when empty, the site institution receives everything.</summary>
    public IReadOnlyList<PayeeSplit> PayeeSplits { get; init; } = [];

    /// <summary>The fees the contract pays.</summary>
    public IReadOnlyList<FeeMatrixItem> FeeMatrix { get; init; } = [];

    /// <summary>Days from invoice to payment.</summary>
    public int PaymentTermsDays { get; init; } = 30;

    /// <summary>Whether the contract is in force.</summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>A clinical event reported to the ledger, such as a completed visit or an approved form.</summary>
public sealed record PaymentEvent
{
    /// <summary>The event's identifier from the system that reported it.</summary>
    public required string EventId { get; init; }

    /// <summary>What kind of event it is.</summary>
    public required PaymentTriggerType TriggerType { get; init; }

    /// <summary>When the event occurred.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The participant the event concerns, if any; per-participant fee caps count by it.</summary>
    public ParticipantId? ParticipantId { get; init; }

    /// <summary>The visit, when the event is a visit.</summary>
    public string? VisitId { get; init; }

    /// <summary>The form, when the event is about a form.</summary>
    public string? FormId { get; init; }

    /// <summary>The procedure code, when the event is a procedure.</summary>
    public string? ProcedureCode { get; init; }

    /// <summary>Any other identifier a fee matrix item can match (the Python <c>metadata["identifier"]</c>).</summary>
    public string? Identifier { get; init; }

    /// <summary>An amount that replaces the matched fee's base amount (and is the amount of a manual adjustment).</summary>
    public decimal? CustomAmount { get; init; }
}

/// <summary>How a line item's net payable was reached from its base amount.</summary>
public sealed record PayableBreakdown(
    decimal BaseAmount,
    decimal OverheadAmount,
    decimal GrossAmount,
    decimal WithholdingAmount,
    decimal HoldbackAmount,
    decimal NetPayable,
    decimal EffectiveOverheadRate,
    decimal WithholdingRate,
    decimal HoldbackRate);

/// <summary>One payee's share of one paid fee, with the SHA-256 digest of its identifying fields.</summary>
public sealed record PayableLineItem(
    Guid LineItemId,
    string EventId,
    string ContractId,
    string ItemCode,
    string ItemDescription,
    string PayeeId,
    string PayeeName,
    decimal SplitPercentage,
    string Currency,
    PayableBreakdown Breakdown,
    PayableLineStatus Status,
    DateTimeOffset OccurredAt,
    DateTimeOffset CalculatedAt,
    string AuditHash);

/// <summary>The holdback reserve of one contract: everything held, everything released, and what is left.</summary>
public sealed record HoldbackAccount(
    string ContractId,
    SiteId SiteId,
    string Currency,
    decimal AccumulatedHoldback,
    decimal ReleasedHoldback,
    decimal CurrentBalance);

/// <summary>A ledger statement for a batch of events, with its line items, totals and a digest over them.</summary>
public sealed record LedgerSummary
{
    /// <summary>The statement's identifier.</summary>
    public required Guid SummaryId { get; init; }

    /// <summary>The contract's study.</summary>
    public required StudyId StudyId { get; init; }

    /// <summary>The contract's site.</summary>
    public required SiteId SiteId { get; init; }

    /// <summary>The contract.</summary>
    public required string ContractId { get; init; }

    /// <summary>Currency of every amount.</summary>
    public required string Currency { get; init; }

    /// <summary>Start of the period covered.</summary>
    public required DateTimeOffset PeriodStart { get; init; }

    /// <summary>End of the period covered.</summary>
    public required DateTimeOffset PeriodEnd { get; init; }

    /// <summary>How many events were submitted, whether or not they produced line items.</summary>
    public required int TotalEventsProcessed { get; init; }

    /// <summary>How many line items the events produced.</summary>
    public required int TotalLineItems { get; init; }

    /// <summary>Sum of the line items' base amounts.</summary>
    public required decimal TotalBaseAmount { get; init; }

    /// <summary>Sum of the line items' overhead.</summary>
    public required decimal TotalOverheadAmount { get; init; }

    /// <summary>Sum of the line items' gross amounts.</summary>
    public required decimal TotalGrossPayable { get; init; }

    /// <summary>Sum of the line items' withholding.</summary>
    public required decimal TotalWithholdingDeducted { get; init; }

    /// <summary>Sum of the line items' holdback.</summary>
    public required decimal TotalHoldbackReserved { get; init; }

    /// <summary>Sum of the line items' net payable.</summary>
    public required decimal TotalNetPayable { get; init; }

    /// <summary>The line items, in event order.</summary>
    public required IReadOnlyList<PayableLineItem> LineItems { get; init; }

    /// <summary>The holdback reserve after the batch.</summary>
    public required HoldbackAccount HoldbackAccount { get; init; }

    /// <summary>Net payable per payee.</summary>
    public required IReadOnlyDictionary<string, decimal> PayeeSubtotals { get; init; }

    /// <summary>SHA-256 over the contract, site, total net payable and the sorted line-item digests.</summary>
    public required string Digest { get; init; }
}
