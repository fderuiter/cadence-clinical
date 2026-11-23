using System;
using System.Collections.Generic;

using Cadence.Apps.Payments.Domain.Ledger;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Payments;

/// <summary>
/// Behavioural tests for the C# port of <c>packages/engine_core/financial/contracts.py</c> (#5047). Every expected value
/// was produced by running the Python reference on the same inputs.
/// </summary>
public class ContractCalculationsTests
{
    private static readonly OverheadTier Academic = new("a", 0.30m, 0m, 10_000m);
    private static readonly OverheadTier Standard = new("b", 0.25m, 10_000m, 50_000m);

    [Fact]
    public void TieredOverhead_AppliesEachBracketsRateToTheSpendInsideIt()
    {
        ContractCalculations.CalculateTieredOverhead(1000m, 0m, [Academic, Standard]).Should().Be((300m, 0.30m));

        // 2,000 at 30% and 2,000 at 25%; the tiers are sorted by threshold first.
        ContractCalculations.CalculateTieredOverhead(4000m, 8000m, [Standard, Academic]).Should().Be((1100m, 0.28m));
    }

    [Fact]
    public void TieredOverhead_ChargesSpendAboveEveryBracketAtTheHighestTiersRate()
    {
        var tiers = new List<OverheadTier> { new("a", 0.1m, 0m, 1000m), new("b", 0.2m, 1000m, 2000m) };

        ContractCalculations.CalculateTieredOverhead(3000m, 0m, tiers).Should().Be((500m, 0.17m));
    }

    [Fact]
    public void TieredOverhead_AnUnboundedTierTakesEverythingAboveItsThreshold()
    {
        var tiers = new List<OverheadTier> { Academic, new("c", 0.15m, 50_000m) };

        ContractCalculations.CalculateTieredOverhead(500m, 60_000m, tiers).Should().Be((75m, 0.15m));
    }

    [Fact]
    public void TieredOverhead_ChargesAGapBetweenBracketsAtTheNextBracketsRate()
    {
        // Pinned Python behaviour: spend between 100 and 200 has no bracket, and is charged at the 50% bracket.
        var tiers = new List<OverheadTier> { new("a", 0.1m, 0m, 100m), new("b", 0.5m, 200m, 300m) };

        ContractCalculations.CalculateTieredOverhead(250m, 0m, tiers).Should().Be((85m, 0.34m));
    }

    [Fact]
    public void TieredOverhead_WithoutTiers_UsesTheDefaultRate_AndRoundsHalfUp()
    {
        ContractCalculations.CalculateTieredOverhead(33.33m, 0m, [], 0.155m).Should().Be((5.17m, 0.16m));
        ContractCalculations.CalculateTieredOverhead(0m, 0m, [], 0.2m).Should().Be((0m, 0m));
    }

    [Fact]
    public void PayeeSplits_RoundEachShare_AndThePrimaryPayeeAbsorbsTheDifference()
    {
        var splits = new List<PayeeSplit>
        {
            new("INST", "Institution", 0.3333m),
            new("PI", "Investigator", 0.3333m, IsPrimary: true),
            new("COORD", "Coordinator", 0.3334m),
        };

        var shares = ContractCalculations.CalculatePayeeSplits(1000.01m, 250m, 125m, 112.50m, splits);

        shares.Should().Equal(
            new PayeeAllocation("INST", "Institution", 0.3333m, 333.30m, 83.33m, 416.63m, 41.66m, 37.50m, 337.47m),
            new PayeeAllocation("PI", "Investigator", 0.3333m, 333.30m, 83.33m, 416.63m, 41.66m, 37.50m, 337.48m),
            new PayeeAllocation("COORD", "Coordinator", 0.3334m, 333.40m, 83.35m, 416.75m, 41.68m, 37.51m, 337.56m));
        (shares[0].NetPayable + shares[1].NetPayable + shares[2].NetPayable).Should().Be(1250.01m - 125m - 112.50m);
    }

    [Fact]
    public void PayeeSplits_WithoutSplits_PayTheSiteInstitutionEverything()
    {
        ContractCalculations.CalculatePayeeSplits(100m, 10m, 5m, 0m, []).Should().Equal(
            new PayeeAllocation(ContractCalculations.DefaultPayeeId, ContractCalculations.DefaultPayeeName, 1m, 100m, 10m, 110m, 5m, 0m, 105m));
    }

    [Fact]
    public void LineItemHash_IsTheDigestThePythonReferenceProduced()
    {
        var occurred = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);

        ContractCalculations.LineItemHash(Guid.Parse("00000000-0000-4000-8000-000000000001"), "EV-1", "GC-001", "PI", 1234.5m, "USD", occurred)
            .Should().Be("153fb471a349e7d92d13799eb835b828dda5adffd49270ff2a554eda50091996"); // pragma: allowlist secret
        ContractCalculations.LineItemHash(
                Guid.Parse("00000000-0000-4000-8000-000000000002"), "EV-2", "GC-001", "INST", 0.1m, "EUR", occurred.AddTicks(1_234_560))
            .Should().Be("b09fdaab6d793ba6fd91a2d3fa69883948a09121e4a60be491ca9ff11fd8a582"); // pragma: allowlist secret
    }

    [Fact]
    public void SummaryDigest_SortsTheLineItemDigests()
    {
        ContractCalculations.SummaryDigest("GC-001", "11111111-1111-1111-1111-111111111111", 99.5m, ["b", "a"])
            .Should().Be("b687f57dee07c2935956981931930a19c3e091edc6897cbfc5d15b7002393518"); // pragma: allowlist secret
    }

    [Theory]
    [InlineData(0L, "2026-03-01T09:30:00+00:00")]
    [InlineData(1_234_560L, "2026-03-01T09:30:00.123456+00:00")]
    [InlineData(5L, "2026-03-01T09:30:00+00:00")]
    public void ToPythonIsoFormat_WritesMicrosecondsOnlyWhenPresent(long extraTicks, string expected)
    {
        var value = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero).AddTicks(extraTicks);

        ContractCalculations.ToPythonIsoFormat(value).Should().Be(expected);
    }

    [Fact]
    public void ToPythonIsoFormat_KeepsTheOffset()
    {
        ContractCalculations.ToPythonIsoFormat(new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.FromHours(-5)))
            .Should().Be("2026-03-01T09:30:00-05:00");
    }
}
