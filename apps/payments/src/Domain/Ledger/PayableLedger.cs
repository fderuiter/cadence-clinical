using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Cadence.Kernel.Time;

using FluentValidation;

namespace Cadence.Apps.Payments.Domain.Ledger;

/// <summary>
/// Turns clinical events into payable line items under one grant contract, keeping the contract's cumulative base
/// spend (for tiered overhead), per-participant fee counts and holdback reserve between events. Port of
/// <c>packages/engine_core/financial/ledger.py</c> (#5047); in memory, no I/O, not thread-safe.
/// </summary>
public sealed class PayableLedger
{
    /// <summary>The item code of a manual adjustment that matched no fee matrix item.</summary>
    public const string ManualAdjustmentItemCode = "MANUAL_ADJ";

    /// <summary>The item code of a holdback release.</summary>
    public const string HoldbackReleaseItemCode = "HOLDBACK_RELEASE";

    private static readonly GrantContractValidator Validator = new();

    private readonly IClock _clock;
    private readonly Func<Guid> _newId;
    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);
    private decimal _cumulativeBaseSpent;
    private decimal _accumulatedHoldback;
    private decimal _releasedHoldback;

    /// <summary>
    /// Opens a ledger for <paramref name="contract"/>. Line item and statement IDs come from
    /// <paramref name="newId"/> (<see cref="Guid.CreateVersion7()"/> when omitted); times come from
    /// <paramref name="clock"/>.
    /// </summary>
    /// <exception cref="ValidationException">The contract breaks a limit of <see cref="GrantContractValidator"/>.</exception>
    public PayableLedger(GrantContract contract, IClock clock, Func<Guid>? newId = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(clock);
        Validator.ValidateAndThrow(contract);
        Contract = contract;
        _clock = clock;
        _newId = newId ?? (() => Guid.CreateVersion7(clock.UtcNow));
    }

    /// <summary>The contract the ledger pays under.</summary>
    public GrantContract Contract { get; }

    /// <summary>The holdback reserve as it stands.</summary>
    public HoldbackAccount HoldbackAccount =>
        new(Contract.ContractId, Contract.SiteId, Contract.Currency, _accumulatedHoldback, _releasedHoldback, CurrentHoldbackBalance);

    private decimal CurrentHoldbackBalance => ContractCalculations.QuantizeCurrency(_accumulatedHoldback - _releasedHoldback);

    /// <summary>
    /// The line items <paramref name="paymentEvent"/> earns: one per payee for every fee matrix item it matches and
    /// whose per-participant cap it has not reached. A manual adjustment that matches nothing is paid at its own
    /// amount, without holdback. A fee whose amount is zero produces no line items but still counts toward its cap.
    /// </summary>
    public IReadOnlyList<PayableLineItem> ProcessEvent(PaymentEvent paymentEvent)
    {
        ArgumentNullException.ThrowIfNull(paymentEvent);
        var matched = Contract.FeeMatrix.Where(item => Matches(item, paymentEvent)).ToList();

        if (matched.Count == 0 && paymentEvent.TriggerType == PaymentTriggerType.ManualAdjustment)
        {
            return LineItemsFor(
                paymentEvent,
                ManualAdjustmentItemCode,
                "Manual Financial Adjustment",
                paymentEvent.CustomAmount ?? 0m,
                overheadApplicable: true,
                withholdingApplicable: true,
                holdbackApplicable: false);
        }

        var lines = new List<PayableLineItem>();
        foreach (var item in matched)
        {
            string capKey = $"{paymentEvent.ParticipantId?.ToString() ?? "GLOBAL"}:{item.ItemCode}";
            int count = _occurrences.GetValueOrDefault(capKey);
            if (item.MaxAllowableEvents is { } cap && count >= cap) continue;

            lines.AddRange(LineItemsFor(
                paymentEvent,
                item.ItemCode,
                item.Description,
                paymentEvent.CustomAmount ?? item.BaseAmount,
                item.OverheadApplicable,
                item.WithholdingApplicable,
                item.HoldbackApplicable));
            _occurrences[capKey] = count + 1;
        }

        return lines;
    }

    /// <summary>
    /// Processes <paramref name="events"/> in the order they occurred (ties keep their submitted order) and returns the
    /// statement. The period defaults to the first and last event, or to now when there are none.
    /// </summary>
    public LedgerSummary ProcessBatch(IReadOnlyList<PaymentEvent> events, DateTimeOffset? periodStart = null, DateTimeOffset? periodEnd = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        var ordered = events.OrderBy(e => e.OccurredAt).ToList();
        var lines = ordered.SelectMany(ProcessEvent).ToList();

        var subtotals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            subtotals[line.PayeeId] = ContractCalculations.QuantizeCurrency(subtotals.GetValueOrDefault(line.PayeeId) + line.Breakdown.NetPayable);
        }

        decimal Total(Func<PayableBreakdown, decimal> amount) => ContractCalculations.QuantizeCurrency(lines.Sum(l => amount(l.Breakdown)));
        decimal totalNet = Total(b => b.NetPayable);
        DateTimeOffset now = _clock.UtcNow;

        return new LedgerSummary
        {
            SummaryId = _newId(),
            StudyId = Contract.StudyId,
            SiteId = Contract.SiteId,
            ContractId = Contract.ContractId,
            Currency = Contract.Currency,
            PeriodStart = periodStart ?? (ordered.Count > 0 ? ordered[0].OccurredAt : now),
            PeriodEnd = periodEnd ?? (ordered.Count > 0 ? ordered[^1].OccurredAt : now),
            TotalEventsProcessed = events.Count,
            TotalLineItems = lines.Count,
            TotalBaseAmount = Total(b => b.BaseAmount),
            TotalOverheadAmount = Total(b => b.OverheadAmount),
            TotalGrossPayable = Total(b => b.GrossAmount),
            TotalWithholdingDeducted = Total(b => b.WithholdingAmount),
            TotalHoldbackReserved = Total(b => b.HoldbackAmount),
            TotalNetPayable = totalNet,
            LineItems = lines,
            HoldbackAccount = HoldbackAccount,
            PayeeSubtotals = subtotals,
            Digest = ContractCalculations.SummaryDigest(Contract.ContractId, Contract.SiteId.ToString(), totalNet, lines.Select(l => l.AuditHash)),
        };
    }

    /// <summary>
    /// Releases <paramref name="amount"/> of the holdback reserve (all of it when null or more than the balance) to the
    /// contract's first payee, as an approved line item with no deductions.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is less than one cent.</exception>
    /// <exception cref="InvalidOperationException">The reserve is empty.</exception>
    public PayableLineItem ReleaseHoldback(decimal? amount = null, string reason = "Milestone Release")
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A holdback release must be greater than zero.");
        }

        decimal available = CurrentHoldbackBalance;
        if (available <= 0)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"No holdback available for release (balance={available:F2})"));
        }

        decimal release = amount is null || amount > available ? available : ContractCalculations.QuantizeCurrency(amount.Value);
        if (release <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A holdback release must be at least one cent.");
        }

        _releasedHoldback = ContractCalculations.QuantizeCurrency(_releasedHoldback + release);

        Guid lineId = _newId();
        string eventId = $"HOLDBACK_REL_{lineId.ToString()[..8]}";
        DateTimeOffset now = _clock.UtcNow;
        var payee = Contract.PayeeSplits.Count > 0 ? Contract.PayeeSplits[0] : null;
        string payeeId = payee?.PayeeId ?? ContractCalculations.DefaultPayeeId;

        return new PayableLineItem(
            lineId,
            eventId,
            Contract.ContractId,
            HoldbackReleaseItemCode,
            $"Holdback Retention Escrow Release: {reason}",
            payeeId,
            payee?.PayeeName ?? ContractCalculations.DefaultPayeeName,
            1m,
            Contract.Currency,
            new PayableBreakdown(release, 0m, release, 0m, 0m, release, 0m, 0m, 0m),
            PayableLineStatus.Approved,
            now,
            now,
            ContractCalculations.LineItemHash(lineId, eventId, Contract.ContractId, payeeId, release, Contract.Currency, now));
    }

    private static bool Matches(FeeMatrixItem item, PaymentEvent paymentEvent)
    {
        if (item.TriggerType != paymentEvent.TriggerType) return false;
        if (string.IsNullOrEmpty(item.EventIdentifier)) return true;

        // The first identifier the event carries, as Python's `a or b or c` picks it (an empty string counts as none).
        string? identifier = new[] { paymentEvent.ProcedureCode, paymentEvent.VisitId, paymentEvent.FormId, paymentEvent.Identifier }
            .FirstOrDefault(id => !string.IsNullOrEmpty(id));
        return string.Equals(identifier, item.EventIdentifier, StringComparison.Ordinal);
    }

    private List<PayableLineItem> LineItemsFor(
        PaymentEvent paymentEvent,
        string itemCode,
        string description,
        decimal baseAmount,
        bool overheadApplicable,
        bool withholdingApplicable,
        bool holdbackApplicable)
    {
        if (baseAmount <= 0) return [];

        var (overhead, effectiveOverheadRate) = overheadApplicable
            ? ContractCalculations.CalculateTieredOverhead(baseAmount, _cumulativeBaseSpent, Contract.OverheadTiers, Contract.DefaultOverheadRate)
            : (0m, 0m);
        _cumulativeBaseSpent = ContractCalculations.QuantizeCurrency(_cumulativeBaseSpent + baseAmount);

        decimal gross = ContractCalculations.QuantizeCurrency(baseAmount + overhead);

        decimal withholdingRate = withholdingApplicable && Contract.DefaultWithholdingRate > 0 ? Contract.DefaultWithholdingRate : 0m;
        decimal withholding = ContractCalculations.QuantizeCurrency(gross * withholdingRate);

        var rule = Contract.HoldbackRule;
        decimal holdbackRate = holdbackApplicable && rule.HoldbackRate > 0 ? rule.HoldbackRate : 0m;
        decimal holdback = ContractCalculations.QuantizeCurrency(gross * holdbackRate);
        if (holdbackRate > 0 && rule.CapAmount is { } cap)
        {
            // The cap limits the balance held at once, so a release makes room again.
            holdback = Math.Min(holdback, Math.Max(0m, cap - CurrentHoldbackBalance));
        }

        if (holdback > 0)
        {
            _accumulatedHoldback = ContractCalculations.QuantizeCurrency(_accumulatedHoldback + holdback);
        }

        DateTimeOffset now = _clock.UtcNow;
        return ContractCalculations.CalculatePayeeSplits(baseAmount, overhead, withholding, holdback, Contract.PayeeSplits)
            .Select(share =>
            {
                Guid lineId = _newId();
                return new PayableLineItem(
                    lineId,
                    paymentEvent.EventId,
                    Contract.ContractId,
                    itemCode,
                    description,
                    share.PayeeId,
                    share.PayeeName,
                    share.SplitPercentage,
                    Contract.Currency,
                    new PayableBreakdown(
                        share.BaseAmount,
                        share.OverheadAmount,
                        share.GrossAmount,
                        share.WithholdingAmount,
                        share.HoldbackAmount,
                        share.NetPayable,
                        effectiveOverheadRate,
                        withholdingRate,
                        holdbackRate),
                    PayableLineStatus.Pending,
                    paymentEvent.OccurredAt,
                    now,
                    ContractCalculations.LineItemHash(
                        lineId, paymentEvent.EventId, Contract.ContractId, share.PayeeId, share.NetPayable, Contract.Currency, paymentEvent.OccurredAt));
            })
            .ToList();
    }
}
