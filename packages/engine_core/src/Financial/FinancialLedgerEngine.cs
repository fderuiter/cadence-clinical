using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadence.Core.Domain.Financial;

/// <summary>
/// High-performance in-memory financial ledger engine evaluating event-triggered grant payables.
/// Produces 21 CFR Part 11 cryptographically sealed ledger statements.
/// @req:PRD-SYS-053
/// </summary>
public sealed class FinancialLedgerEngine
{
    private readonly GrantContract _contract;
    private decimal _cumulativeBaseSpent;
    private readonly Dictionary<string, int> _eventOccurrenceCounts = new();
    private HoldbackLedgerAccount _holdbackAccount;

    public GrantContract Contract => _contract;
    public HoldbackLedgerAccount HoldbackAccount => _holdbackAccount;
    public decimal CumulativeBaseSpent => _cumulativeBaseSpent;

    public FinancialLedgerEngine(GrantContract contract)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        _cumulativeBaseSpent = 0.0m;
        _holdbackAccount = new HoldbackLedgerAccount(
            ContractId: contract.ContractId,
            SiteId: contract.SiteId,
            Currency: contract.Currency
        );
    }

    public static decimal QuantizeCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static (decimal OverheadAmount, decimal EffectiveRate) CalculateTieredOverhead(
        decimal baseAmount,
        decimal cumulativeSpent,
        IReadOnlyList<OverheadTier> tiers,
        decimal defaultRate = 0.0m)
    {
        if (baseAmount <= 0.0m)
        {
            return (0.0m, 0.0m);
        }

        if (tiers == null || tiers.Count == 0)
        {
            decimal overhead = QuantizeCurrency(baseAmount * defaultRate);
            decimal effRate = baseAmount > 0.0m ? QuantizeCurrency(overhead / baseAmount) : defaultRate;
            return (overhead, effRate);
        }

        var sortedTiers = tiers.OrderBy(t => t.MinThreshold).ToList();
        decimal totalOverhead = 0.0m;
        decimal remainingBase = baseAmount;
        decimal currentSpent = cumulativeSpent;

        foreach (var tier in sortedTiers)
        {
            decimal tierMin = tier.MinThreshold;
            decimal tierMax = tier.MaxThreshold ?? decimal.MaxValue;
            decimal tierRate = tier.OverheadRate;

            if (currentSpent < tierMax)
            {
                decimal tierCapacity = tierMax - Math.Max(currentSpent, tierMin);
                if (tierCapacity > 0.0m)
                {
                    decimal applicableBase = Math.Min(remainingBase, tierCapacity);
                    decimal tierOverhead = applicableBase * tierRate;
                    totalOverhead += tierOverhead;
                    remainingBase -= applicableBase;
                    currentSpent += applicableBase;
                }
            }

            if (remainingBase <= 0.0m)
            {
                break;
            }
        }

        if (remainingBase > 0.0m)
        {
            decimal fallbackRate = sortedTiers.Count > 0 ? sortedTiers[^1].OverheadRate : defaultRate;
            totalOverhead += remainingBase * fallbackRate;
        }

        decimal quantizedOverhead = QuantizeCurrency(totalOverhead);
        decimal effectiveRate = baseAmount > 0.0m ? QuantizeCurrency(quantizedOverhead / baseAmount) : 0.0m;
        return (quantizedOverhead, effectiveRate);
    }

    public static List<PayeeSplitRecord> CalculatePayeeSplits(
        decimal baseAmount,
        decimal overheadAmount,
        decimal withholdingAmount,
        decimal holdbackAmount,
        IReadOnlyList<PayeeSplit> splits)
    {
        decimal grossTotal = baseAmount + overheadAmount;
        decimal netTotal = grossTotal - withholdingAmount - holdbackAmount;

        if (splits == null || splits.Count == 0)
        {
            return new List<PayeeSplitRecord>
            {
                new PayeeSplitRecord(
                    PayeeId: "DEFAULT",
                    PayeeName: "Primary Site Institution",
                    SplitPercentage: 1.0m,
                    BaseAmount: baseAmount,
                    OverheadAmount: overheadAmount,
                    GrossAmount: grossTotal,
                    WithholdingAmount: withholdingAmount,
                    HoldbackAmount: holdbackAmount,
                    NetPayable: netTotal
                )
            };
        }

        var records = new List<PayeeSplitRecord>();
        decimal accumulatedNet = 0.0m;
        int primaryIdx = 0;

        for (int i = 0; i < splits.Count; i++)
        {
            var split = splits[i];
            decimal pct = split.Percentage;
            decimal splitBase = QuantizeCurrency(baseAmount * pct);
            decimal splitOverhead = QuantizeCurrency(overheadAmount * pct);
            decimal splitGross = QuantizeCurrency(grossTotal * pct);
            decimal splitWithholding = QuantizeCurrency(withholdingAmount * pct);
            decimal splitHoldback = QuantizeCurrency(holdbackAmount * pct);
            decimal splitNet = splitGross - splitWithholding - splitHoldback;

            accumulatedNet += splitNet;
            if (split.IsPrimary)
            {
                primaryIdx = i;
            }

            records.Add(new PayeeSplitRecord(
                PayeeId: split.PayeeId,
                PayeeName: split.PayeeName,
                SplitPercentage: split.Percentage,
                BaseAmount: splitBase,
                OverheadAmount: splitOverhead,
                GrossAmount: splitGross,
                WithholdingAmount: splitWithholding,
                HoldbackAmount: splitHoldback,
                NetPayable: splitNet
            ));
        }

        decimal discrepancy = netTotal - accumulatedNet;
        if (discrepancy != 0.0m && records.Count > 0)
        {
            var primaryRec = records[primaryIdx];
            records[primaryIdx] = primaryRec with
            {
                NetPayable = QuantizeCurrency(primaryRec.NetPayable + discrepancy)
            };
        }

        return records;
    }

    public static string GenerateLineItemHash(
        string lineItemId,
        string eventId,
        string contractId,
        string payeeId,
        decimal netPayable,
        string currency,
        string occurredAtIso)
    {
        var canonicalObj = new SortedDictionary<string, string>
        {
            { "contract_id", contractId },
            { "currency", currency },
            { "event_id", eventId },
            { "line_item_id", lineItemId },
            { "net_payable", netPayable.ToString("F2") },
            { "occurred_at", occurredAtIso },
            { "payee_id", payeeId }
        };

        string json = JsonSerializer.Serialize(canonicalObj);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public List<PayableLineItem> ProcessEvent(ClinicalEvent clinicalEvent)
    {
        ArgumentNullException.ThrowIfNull(clinicalEvent);

        var matchedItems = MatchFeeMatrixItems(clinicalEvent);
        if (matchedItems.Count == 0 && clinicalEvent.TriggerType == TriggerType.MANUAL_ADJUSTMENT)
        {
            decimal baseAmt = clinicalEvent.CustomAmount ?? 0.0m;
            return GenerateLineItemsForAmount(
                clinicalEvent: clinicalEvent,
                itemCode: "MANUAL_ADJ",
                itemDescription: "Manual Financial Adjustment",
                baseAmount: baseAmt,
                overheadApplicable: true,
                withholdingApplicable: true,
                holdbackApplicable: false
            );
        }

        var generatedLines = new List<PayableLineItem>();
        foreach (var feeItem in matchedItems)
        {
            string capKey = $"{clinicalEvent.SubjectId ?? "GLOBAL"}:{feeItem.ItemCode}";
            if (feeItem.MaxAllowableEvents.HasValue)
            {
                _eventOccurrenceCounts.TryGetValue(capKey, out int currentCount);
                if (currentCount >= feeItem.MaxAllowableEvents.Value)
                {
                    continue;
                }
            }

            decimal baseAmount = clinicalEvent.CustomAmount ?? feeItem.BaseAmount;
            var lines = GenerateLineItemsForAmount(
                clinicalEvent: clinicalEvent,
                itemCode: feeItem.ItemCode,
                itemDescription: feeItem.Description,
                baseAmount: baseAmount,
                overheadApplicable: feeItem.OverheadApplicable,
                withholdingApplicable: feeItem.WithholdingApplicable,
                holdbackApplicable: feeItem.HoldbackApplicable
            );

            generatedLines.AddRange(lines);
            _eventOccurrenceCounts.TryGetValue(capKey, out int count);
            _eventOccurrenceCounts[capKey] = count + 1;
        }

        return generatedLines;
    }

    public LedgerSummary ProcessBatch(
        IEnumerable<ClinicalEvent> events,
        DateTime? periodStart = null,
        DateTime? periodEnd = null)
    {
        var sortedEvents = (events ?? Array.Empty<ClinicalEvent>())
            .OrderBy(e => e.OccurredAt)
            .ToList();

        var allLines = new List<PayableLineItem>();
        foreach (var ev in sortedEvents)
        {
            allLines.AddRange(ProcessEvent(ev));
        }

        DateTime nowUtc = DateTime.UtcNow;
        DateTime pStart = periodStart ?? (sortedEvents.Count > 0 ? sortedEvents[0].OccurredAt : nowUtc);
        DateTime pEnd = periodEnd ?? (sortedEvents.Count > 0 ? sortedEvents[^1].OccurredAt : nowUtc);

        decimal totalBase = QuantizeCurrency(allLines.Sum(l => l.Breakdown.BaseAmount));
        decimal totalOverhead = QuantizeCurrency(allLines.Sum(l => l.Breakdown.OverheadAmount));
        decimal totalGross = QuantizeCurrency(allLines.Sum(l => l.Breakdown.GrossAmount));
        decimal totalWithholding = QuantizeCurrency(allLines.Sum(l => l.Breakdown.WithholdingAmount));
        decimal totalHoldback = QuantizeCurrency(allLines.Sum(l => l.Breakdown.HoldbackAmount));
        decimal totalNet = QuantizeCurrency(allLines.Sum(l => l.Breakdown.NetPayable));

        var payeeSubtotals = new Dictionary<string, decimal>();
        foreach (var line in allLines)
        {
            payeeSubtotals.TryGetValue(line.PayeeId, out decimal currentSubtotal);
            payeeSubtotals[line.PayeeId] = QuantizeCurrency(currentSubtotal + line.Breakdown.NetPayable);
        }

        string digest = ComputeSummaryDigest(
            contractId: _contract.ContractId,
            siteId: _contract.SiteId,
            totalNet: totalNet,
            lineItemHashes: allLines.Select(l => l.AuditHash).ToList()
        );

        return new LedgerSummary(
            SummaryId: Guid.NewGuid().ToString("D"),
            StudyId: _contract.StudyId,
            SiteId: _contract.SiteId,
            ContractId: _contract.ContractId,
            Currency: _contract.Currency,
            PeriodStart: pStart,
            PeriodEnd: pEnd,
            TotalEventsProcessed: sortedEvents.Count,
            TotalLineItems: allLines.Count,
            TotalBaseAmount: totalBase,
            TotalOverheadAmount: totalOverhead,
            TotalGrossPayable: totalGross,
            TotalWithholdingDeducted: totalWithholding,
            TotalHoldbackReserved: totalHoldback,
            TotalNetPayable: totalNet,
            LineItems: allLines,
            HoldbackAccount: _holdbackAccount,
            PayeeSubtotals: payeeSubtotals,
            CryptographicDigest: digest
        );
    }

    public PayableLineItem ReleaseHoldback(
        decimal? amountToRelease = null,
        string reason = "Milestone Release")
    {
        decimal avail = _holdbackAccount.CurrentBalance;
        decimal releaseAmt = amountToRelease == null || amountToRelease.Value > avail
            ? avail
            : QuantizeCurrency(amountToRelease.Value);

        if (releaseAmt <= 0.0m)
        {
            throw new InvalidOperationException($"No holdback available for release (balance={avail:F2})");
        }

        decimal newReleased = QuantizeCurrency(_holdbackAccount.ReleasedHoldback + releaseAmt);
        decimal newBalance = QuantizeCurrency(_holdbackAccount.AccumulatedHoldback - newReleased);

        _holdbackAccount = _holdbackAccount with
        {
            ReleasedHoldback = newReleased,
            CurrentBalance = newBalance
        };

        string lineId = Guid.NewGuid().ToString("D");
        DateTime nowUtc = DateTime.UtcNow;

        var breakdown = new PayableBreakdown(
            BaseAmount: releaseAmt,
            OverheadAmount: 0.0m,
            GrossAmount: releaseAmt,
            WithholdingAmount: 0.0m,
            HoldbackAmount: 0.0m,
            NetPayable: releaseAmt,
            EffectiveOverheadRate: 0.0m,
            WithholdingRate: 0.0m,
            HoldbackRate: 0.0m
        );

        string primaryPayeeId = _contract.PayeeSplits.Count > 0 ? _contract.PayeeSplits[0].PayeeId : "DEFAULT";
        string primaryPayeeName = _contract.PayeeSplits.Count > 0 ? _contract.PayeeSplits[0].PayeeName : "Primary Site Institution";

        string auditHash = GenerateLineItemHash(
            lineItemId: lineId,
            eventId: $"HOLDBACK_REL_{lineId[..8]}",
            contractId: _contract.ContractId,
            payeeId: primaryPayeeId,
            netPayable: releaseAmt,
            currency: _contract.Currency,
            occurredAtIso: nowUtc.ToString("o")
        );

        return new PayableLineItem(
            LineItemId: lineId,
            EventId: $"HOLDBACK_REL_{lineId[..8]}",
            ContractId: _contract.ContractId,
            ItemCode: "HOLDBACK_RELEASE",
            ItemDescription: $"Holdback Retention Escrow Release: {reason}",
            PayeeId: primaryPayeeId,
            PayeeName: primaryPayeeName,
            SplitPercentage: 1.0m,
            Currency: _contract.Currency,
            Breakdown: breakdown,
            OccurredAt: nowUtc,
            CalculatedAt: nowUtc,
            AuditHash: auditHash,
            Status: PayableStatus.APPROVED
        );
    }

    private List<FeeMatrixItem> MatchFeeMatrixItems(ClinicalEvent clinicalEvent)
    {
        var matches = new List<FeeMatrixItem>();
        foreach (var item in _contract.FeeMatrix)
        {
            if (item.TriggerType != clinicalEvent.TriggerType)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(item.EventIdentifier))
            {
                string? idToCheck = clinicalEvent.ProcedureCode
                    ?? clinicalEvent.VisitId
                    ?? clinicalEvent.FormId;

                if (idToCheck == null && clinicalEvent.Metadata != null && clinicalEvent.Metadata.TryGetValue("identifier", out var metaVal))
                {
                    idToCheck = metaVal?.ToString();
                }

                if (idToCheck != item.EventIdentifier)
                {
                    continue;
                }
            }

            matches.Add(item);
        }

        return matches;
    }

    private List<PayableLineItem> GenerateLineItemsForAmount(
        ClinicalEvent clinicalEvent,
        string itemCode,
        string itemDescription,
        decimal baseAmount,
        bool overheadApplicable,
        bool withholdingApplicable,
        bool holdbackApplicable)
    {
        if (baseAmount <= 0.0m)
        {
            return new List<PayableLineItem>();
        }

        decimal overheadAmt = 0.0m;
        decimal effOverheadRate = 0.0m;
        if (overheadApplicable)
        {
            (overheadAmt, effOverheadRate) = CalculateTieredOverhead(
                baseAmount: baseAmount,
                cumulativeSpent: _cumulativeBaseSpent,
                tiers: _contract.OverheadTiers,
                defaultRate: _contract.DefaultOverheadRate
            );
        }

        _cumulativeBaseSpent = QuantizeCurrency(_cumulativeBaseSpent + baseAmount);
        decimal grossAmt = QuantizeCurrency(baseAmount + overheadAmt);

        decimal withholdingAmt = 0.0m;
        decimal wRate = 0.0m;
        if (withholdingApplicable && _contract.DefaultWithholdingRate > 0.0m)
        {
            withholdingAmt = QuantizeCurrency(grossAmt * _contract.DefaultWithholdingRate);
            wRate = _contract.DefaultWithholdingRate;
        }

        decimal holdbackAmt = 0.0m;
        decimal hRate = 0.0m;
        if (holdbackApplicable && _contract.HoldbackRule.HoldbackRate > 0.0m)
        {
            decimal potentialHoldback = QuantizeCurrency(grossAmt * _contract.HoldbackRule.HoldbackRate);
            if (_contract.HoldbackRule.CapAmount.HasValue)
            {
                decimal currentBal = _holdbackAccount.CurrentBalance;
                decimal roomUnderCap = Math.Max(0.0m, _contract.HoldbackRule.CapAmount.Value - currentBal);
                holdbackAmt = Math.Min(potentialHoldback, roomUnderCap);
            }
            else
            {
                holdbackAmt = potentialHoldback;
            }
            hRate = _contract.HoldbackRule.HoldbackRate;
        }

        if (holdbackAmt > 0.0m)
        {
            decimal accum = QuantizeCurrency(_holdbackAccount.AccumulatedHoldback + holdbackAmt);
            decimal bal = QuantizeCurrency(accum - _holdbackAccount.ReleasedHoldback);
            _holdbackAccount = _holdbackAccount with
            {
                AccumulatedHoldback = accum,
                CurrentBalance = bal
            };
        }

        var splitRecords = CalculatePayeeSplits(
            baseAmount: baseAmount,
            overheadAmount: overheadAmt,
            withholdingAmount: withholdingAmt,
            holdbackAmount: holdbackAmt,
            splits: _contract.PayeeSplits
        );

        DateTime nowUtc = DateTime.UtcNow;
        var lineItems = new List<PayableLineItem>();

        foreach (var rec in splitRecords)
        {
            string lineId = Guid.NewGuid().ToString("D");
            var breakdown = new PayableBreakdown(
                BaseAmount: rec.BaseAmount,
                OverheadAmount: rec.OverheadAmount,
                GrossAmount: rec.GrossAmount,
                WithholdingAmount: rec.WithholdingAmount,
                HoldbackAmount: rec.HoldbackAmount,
                NetPayable: rec.NetPayable,
                EffectiveOverheadRate: effOverheadRate,
                WithholdingRate: wRate,
                HoldbackRate: hRate
            );

            string auditHash = GenerateLineItemHash(
                lineItemId: lineId,
                eventId: clinicalEvent.EventId,
                contractId: _contract.ContractId,
                payeeId: rec.PayeeId,
                netPayable: rec.NetPayable,
                currency: _contract.Currency,
                occurredAtIso: clinicalEvent.OccurredAt.ToString("o")
            );

            lineItems.Add(new PayableLineItem(
                LineItemId: lineId,
                EventId: clinicalEvent.EventId,
                ContractId: _contract.ContractId,
                ItemCode: itemCode,
                ItemDescription: itemDescription,
                PayeeId: rec.PayeeId,
                PayeeName: rec.PayeeName,
                SplitPercentage: rec.SplitPercentage,
                Currency: _contract.Currency,
                Breakdown: breakdown,
                OccurredAt: clinicalEvent.OccurredAt,
                CalculatedAt: nowUtc,
                AuditHash: auditHash,
                Status: PayableStatus.PENDING
            ));
        }

        return lineItems;
    }

    private static string ComputeSummaryDigest(
        string contractId,
        string siteId,
        decimal totalNet,
        List<string> lineItemHashes)
    {
        var sortedHashes = lineItemHashes.OrderBy(h => h).ToList();
        var canonicalObj = new
        {
            contract_id = contractId,
            item_hashes = sortedHashes,
            site_id = siteId,
            total_net = totalNet.ToString("F2")
        };

        string json = JsonSerializer.Serialize(canonicalObj);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}

public record PayeeSplitRecord(
    string PayeeId,
    string PayeeName,
    decimal SplitPercentage,
    decimal BaseAmount,
    decimal OverheadAmount,
    decimal GrossAmount,
    decimal WithholdingAmount,
    decimal HoldbackAmount,
    decimal NetPayable
);
