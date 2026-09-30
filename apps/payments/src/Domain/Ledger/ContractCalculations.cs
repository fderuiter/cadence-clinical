using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Cadence.Apps.Payments.Domain.Ledger;

/// <summary>One payee's share of a payable, before it becomes a line item.</summary>
public sealed record PayeeAllocation(
    string PayeeId,
    string PayeeName,
    decimal SplitPercentage,
    decimal BaseAmount,
    decimal OverheadAmount,
    decimal GrossAmount,
    decimal WithholdingAmount,
    decimal HoldbackAmount,
    decimal NetPayable);

/// <summary>
/// The grant contract arithmetic: tiered overhead, payee splits and the line-item digest. Port of
/// <c>packages/engine_core/financial/contracts.py</c> (#5047), in <see cref="decimal"/> throughout and with no I/O.
/// </summary>
public static class ContractCalculations
{
    /// <summary>The payee that receives everything when a contract has no payee splits.</summary>
    public const string DefaultPayeeId = "DEFAULT";

    /// <summary>The name of <see cref="DefaultPayeeId"/>.</summary>
    public const string DefaultPayeeName = "Primary Site Institution";

    private static readonly JsonWriterOptions DigestWriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Rounds <paramref name="amount"/> to cents, half away from zero (the Python <c>ROUND_HALF_UP</c>).</summary>
    public static decimal QuantizeCurrency(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The overhead on <paramref name="baseAmount"/> when <paramref name="cumulativeSpent"/> has already been spent:
    /// each tier's rate applies to the part of the new spend that falls in its bracket, and spend above every bracket
    /// takes the highest tier's rate. With no tiers, <paramref name="defaultRate"/> applies to all of it.
    /// </summary>
    /// <returns>The overhead in cents, and the effective rate (overhead over base), also rounded to two places.</returns>
    public static (decimal Overhead, decimal EffectiveRate) CalculateTieredOverhead(
        decimal baseAmount,
        decimal cumulativeSpent,
        IReadOnlyList<OverheadTier> tiers,
        decimal defaultRate = 0m)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        if (baseAmount <= 0) return (0m, 0m);

        if (tiers.Count == 0)
        {
            decimal flat = QuantizeCurrency(baseAmount * defaultRate);
            return (flat, QuantizeCurrency(flat / baseAmount));
        }

        var sorted = tiers.OrderBy(t => t.MinThreshold).ToList();
        decimal overhead = 0m;
        decimal remaining = baseAmount;
        decimal spent = cumulativeSpent;

        foreach (var tier in sorted)
        {
            // A tier with no upper bound takes everything that is left.
            bool unbounded = tier.MaxThreshold is null;
            decimal max = tier.MaxThreshold ?? 0m;
            if (unbounded || spent < max)
            {
                decimal applicable = unbounded ? remaining : Math.Min(remaining, max - Math.Max(spent, tier.MinThreshold));
                if (applicable > 0)
                {
                    overhead += applicable * tier.OverheadRate;
                    remaining -= applicable;
                    spent += applicable;
                }
            }

            if (remaining <= 0) break;
        }

        if (remaining > 0)
        {
            overhead += remaining * sorted[^1].OverheadRate;
        }

        decimal rounded = QuantizeCurrency(overhead);
        return (rounded, QuantizeCurrency(rounded / baseAmount));
    }

    /// <summary>
    /// Splits one payable between the contract's payees. Each share is rounded to cents; the primary payee (the last
    /// one marked primary, else the first) absorbs the difference, so the net shares add up to the total net payable.
    /// With no splits, <see cref="DefaultPayeeId"/> receives the whole amount.
    /// </summary>
    public static IReadOnlyList<PayeeAllocation> CalculatePayeeSplits(
        decimal baseAmount,
        decimal overheadAmount,
        decimal withholdingAmount,
        decimal holdbackAmount,
        IReadOnlyList<PayeeSplit> splits)
    {
        ArgumentNullException.ThrowIfNull(splits);
        decimal grossTotal = baseAmount + overheadAmount;
        decimal netTotal = grossTotal - withholdingAmount - holdbackAmount;

        if (splits.Count == 0)
        {
            return
            [
                new PayeeAllocation(
                    DefaultPayeeId, DefaultPayeeName, 1m, baseAmount, overheadAmount, grossTotal, withholdingAmount, holdbackAmount, netTotal),
            ];
        }

        var allocations = new List<PayeeAllocation>(splits.Count);
        decimal allocatedNet = 0m;
        int primary = 0;
        for (int i = 0; i < splits.Count; i++)
        {
            var split = splits[i];
            decimal pct = split.Percentage;
            decimal gross = QuantizeCurrency(grossTotal * pct);
            decimal withholding = QuantizeCurrency(withholdingAmount * pct);
            decimal holdback = QuantizeCurrency(holdbackAmount * pct);
            decimal net = gross - withholding - holdback;
            allocatedNet += net;
            if (split.IsPrimary) primary = i;

            allocations.Add(new PayeeAllocation(
                split.PayeeId,
                split.PayeeName,
                pct,
                QuantizeCurrency(baseAmount * pct),
                QuantizeCurrency(overheadAmount * pct),
                gross,
                withholding,
                holdback,
                net));
        }

        decimal difference = netTotal - allocatedNet;
        if (difference != 0)
        {
            allocations[primary] = allocations[primary] with { NetPayable = QuantizeCurrency(allocations[primary].NetPayable + difference) };
        }

        return allocations;
    }

    /// <summary>
    /// SHA-256 (lower-case hex) of the line item's identifying fields as compact JSON with sorted keys, the net amount
    /// written with two decimals. For ASCII values this is byte-for-byte the digest <c>generate_line_item_hash</c>
    /// produced; it is a tamper-evidence digest, not a signature.
    /// </summary>
    public static string LineItemHash(
        Guid lineItemId,
        string eventId,
        string contractId,
        string payeeId,
        decimal netPayable,
        string currency,
        DateTimeOffset occurredAt) =>
        Digest(writer =>
        {
            writer.WriteString("contract_id", contractId);
            writer.WriteString("currency", currency);
            writer.WriteString("event_id", eventId);
            writer.WriteString("line_item_id", lineItemId.ToString());
            writer.WriteString("net_payable", FormatAmount(netPayable));
            writer.WriteString("occurred_at", ToPythonIsoFormat(occurredAt));
            writer.WriteString("payee_id", payeeId);
        });

    /// <summary>
    /// SHA-256 of a ledger statement: the contract, the site, the total net payable and the line-item digests in
    /// ordinal order, as compact JSON with sorted keys (the Python <c>_compute_summary_digest</c>).
    /// </summary>
    public static string SummaryDigest(string contractId, string siteId, decimal totalNet, IEnumerable<string> lineItemHashes) =>
        Digest(writer =>
        {
            writer.WriteString("contract_id", contractId);
            writer.WriteStartArray("item_hashes");
            foreach (var hash in lineItemHashes.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(hash);
            }

            writer.WriteEndArray();
            writer.WriteString("site_id", siteId);
            writer.WriteString("total_net", FormatAmount(totalNet));
        });

    /// <summary>
    /// <paramref name="value"/> as Python's <c>datetime.isoformat()</c> writes it: seconds, microseconds only when
    /// they are not zero, and the offset as <c>+HH:MM</c>.
    /// </summary>
    public static string ToPythonIsoFormat(DateTimeOffset value)
    {
        long microseconds = value.Ticks % TimeSpan.TicksPerSecond / (TimeSpan.TicksPerMillisecond / 1000);
        string fraction = microseconds == 0 ? "" : "." + microseconds.ToString("D6", CultureInfo.InvariantCulture);
        return value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + fraction + value.ToString("zzz", CultureInfo.InvariantCulture);
    }

    private static string FormatAmount(decimal amount) => QuantizeCurrency(amount).ToString("F2", CultureInfo.InvariantCulture);

    // Keys are written in ordinal order by the callers, which is what json.dumps(sort_keys=True) does for these keys.
    private static string Digest(Action<Utf8JsonWriter> writeProperties)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, DigestWriterOptions))
        {
            writer.WriteStartObject();
            writeProperties(writer);
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }
}
