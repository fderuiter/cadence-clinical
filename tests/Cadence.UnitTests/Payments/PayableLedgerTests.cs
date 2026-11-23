using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Apps.Payments.Domain.Ledger;
using Cadence.Kernel;
using Cadence.Testing.Time;

using FluentAssertions;

using FluentValidation;

using Xunit;

namespace Cadence.UnitTests.Payments;

/// <summary>
/// Behavioural tests for the C# port of <c>packages/engine_core/financial/ledger.py</c> (#5047). The scenario and every
/// expected amount and digest come from running the Python reference with the same contract and events, with its
/// UUIDs and clock replaced by the same sequence and instant this test uses.
/// </summary>
public class PayableLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly SiteId Site = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly ParticipantId Alice = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly ParticipantId Bob = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"));

    private int _ids;

    private Guid NextId() => Guid.Parse($"00000000-0000-4000-8000-{++_ids:D12}");

    private static GrantContract Contract() => new()
    {
        ContractId = "GC-001",
        StudyId = new StudyId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
        SiteId = Site,
        EffectiveDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        OverheadTiers = [new OverheadTier("a", 0.30m, 0m, 2000m), new OverheadTier("b", 0.20m, 2000m)],
        DefaultWithholdingRate = 0.10m,
        HoldbackRule = new HoldbackReserveRule(0.10m, CapAmount: 400m),
        PayeeSplits = [new PayeeSplit("INST", "Institution", 0.6m), new PayeeSplit("PI", "Investigator", 0.4m, IsPrimary: true)],
        FeeMatrix =
        [
            new FeeMatrixItem("ENR", "Enrollment", PaymentTriggerType.Enrollment, 1500m, MaxAllowableEvents: 1),
            new FeeMatrixItem("V1", "Visit 1", PaymentTriggerType.VisitCompleted, 500m, EventIdentifier: "V1"),
            new FeeMatrixItem("V2", "Visit 2", PaymentTriggerType.VisitCompleted, 750m, EventIdentifier: "V2", HoldbackApplicable: false),
            new FeeMatrixItem("AE", "AE report", PaymentTriggerType.AeReported, 200m, OverheadApplicable: false, WithholdingApplicable: false),
        ],
    };

    private static PaymentEvent Event(string id, PaymentTriggerType type, int day, ParticipantId? participant = null) => new()
    {
        EventId = id,
        TriggerType = type,
        OccurredAt = new DateTimeOffset(2026, 2, day, 10, 0, 0, TimeSpan.Zero),
        ParticipantId = participant,
    };

    // Submitted out of order: the batch processes them by the time they occurred.
    private static List<PaymentEvent> Events() =>
    [
        Event("E5", PaymentTriggerType.VisitCompleted, 5, Alice) with { VisitId = "V1" },
        Event("E1", PaymentTriggerType.Enrollment, 1, Alice),
        Event("E2", PaymentTriggerType.Enrollment, 2, Alice),
        Event("E3", PaymentTriggerType.Enrollment, 3, Bob),
        Event("E6", PaymentTriggerType.VisitCompleted, 6, Bob) with { VisitId = "V2" },
        Event("E7", PaymentTriggerType.VisitCompleted, 7, Bob) with { VisitId = "V3" },
        Event("E8", PaymentTriggerType.AeReported, 8, Bob),
        Event("E9", PaymentTriggerType.ManualAdjustment, 9) with { CustomAmount = 99.99m },
        Event("E10", PaymentTriggerType.VisitCompleted, 10, Bob) with { VisitId = "V1", CustomAmount = 600m },
        Event("E11", PaymentTriggerType.FormApproved, 11, Bob) with { FormId = "F1" },
    ];

    private PayableLedger Ledger() => new(Contract(), new FakeClock(Now), NextId);

    [Fact]
    public void ProcessBatch_ProducesThePythonReferencesLineItems()
    {
        var summary = Ledger().ProcessBatch(Events());

        var expected = new (string Event, string Item, string Payee, decimal Base, decimal Overhead, decimal Gross, decimal Withholding, decimal Holdback, decimal Net, decimal OverheadRate, decimal WithholdingRate, decimal HoldbackRate, string Hash)[]
        {
            ("E1", "ENR", "INST", 900m, 270m, 1170m, 117m, 117m, 936m, 0.30m, 0.1m, 0.1m, "85c91be6f7c6b328400ea43cb3c43cdf439ee87e0a589040f0ef3e9a1d3955ac"), // pragma: allowlist secret
            ("E1", "ENR", "PI", 600m, 180m, 780m, 78m, 78m, 624m, 0.30m, 0.1m, 0.1m, "d17fe645082173ae2b7e7ef9c6182ade626ea5813dc52484be0609b56e27a9b6"), // pragma: allowlist secret
            ("E3", "ENR", "INST", 900m, 210m, 1110m, 111m, 111m, 888m, 0.23m, 0.1m, 0.1m, "8addb7451ec2123ea5cd0223009eee0dbd1fb201bdf29bc81856032da3b9e4d1"), // pragma: allowlist secret
            ("E3", "ENR", "PI", 600m, 140m, 740m, 74m, 74m, 592m, 0.23m, 0.1m, 0.1m, "2367406547aa147974c53e7588f919ce4d1831bd87c0ae77e3f62213e7cfa335"), // pragma: allowlist secret
            ("E5", "V1", "INST", 300m, 60m, 360m, 36m, 12m, 312m, 0.20m, 0.1m, 0.1m, "5bcd7e384a4d8dea51d7fd3120f5ce7d8303bd074946c87f344d609fa3476f7d"), // pragma: allowlist secret
            ("E5", "V1", "PI", 200m, 40m, 240m, 24m, 8m, 208m, 0.20m, 0.1m, 0.1m, "7aa8606604158ad1e6b38a63f31043dd8021626fde07bd64aef657ac842e5064"), // pragma: allowlist secret
            ("E6", "V2", "INST", 450m, 90m, 540m, 54m, 0m, 486m, 0.20m, 0.1m, 0m, "f176e478a20a4b016478fd18af399ceb76d4a1067b5bd571188680e7ce9e9c0e"), // pragma: allowlist secret
            ("E6", "V2", "PI", 300m, 60m, 360m, 36m, 0m, 324m, 0.20m, 0.1m, 0m, "0e3b9647ec08618418ddcdec16818cf1ee9f9bdbf99748a6eb1468d6c2e6e241"), // pragma: allowlist secret
            ("E8", "AE", "INST", 120m, 0m, 120m, 0m, 0m, 120m, 0m, 0m, 0.1m, "e01c1264df470b03820c0e076f63f6f7e30029319bf3a2c5c8dbf4e0ff535b4c"), // pragma: allowlist secret
            ("E8", "AE", "PI", 80m, 0m, 80m, 0m, 0m, 80m, 0m, 0m, 0.1m, "0e5b22e5c49ae38e85012814fd09361017e7355a2efc3d77d2f43997ad550346"), // pragma: allowlist secret
            // Python stored 64.78999999999999 here (binary float); the digest used "64.79" either way.
            ("E9", PayableLedger.ManualAdjustmentItemCode, "INST", 59.99m, 12m, 71.99m, 7.20m, 0m, 64.79m, 0.20m, 0.1m, 0m, "d1c9331f9a295c9c6be25d6d8f1b9dc51b7e9374742ad746b841051e2165a867"), // pragma: allowlist secret
            ("E9", PayableLedger.ManualAdjustmentItemCode, "PI", 40m, 8m, 48m, 4.80m, 0m, 43.20m, 0.20m, 0.1m, 0m, "036195ee00e53dd6859413afc69a1c12ac79f1057bac65bf8b999914b996f78c"), // pragma: allowlist secret
            // The holdback cap of 400 was reached at E5, so the rate still shows but nothing more is held.
            ("E10", "V1", "INST", 360m, 72m, 432m, 43.20m, 0m, 388.80m, 0.20m, 0.1m, 0.1m, "dbfd30c94db81f199e796c0d0cac9167a55471208fbb1afb680ee11410800e35"), // pragma: allowlist secret
            ("E10", "V1", "PI", 240m, 48m, 288m, 28.80m, 0m, 259.20m, 0.20m, 0.1m, 0.1m, "cd6f6a39226083cb480d5d45172fbb76558489bf6dd75d427408889a4cf4c462"), // pragma: allowlist secret
        };

        summary.LineItems.Select(l => (
                l.EventId, l.ItemCode, l.PayeeId, l.Breakdown.BaseAmount, l.Breakdown.OverheadAmount, l.Breakdown.GrossAmount,
                l.Breakdown.WithholdingAmount, l.Breakdown.HoldbackAmount, l.Breakdown.NetPayable, l.Breakdown.EffectiveOverheadRate,
                l.Breakdown.WithholdingRate, l.Breakdown.HoldbackRate, l.AuditHash))
            .Should().Equal(expected);
        summary.LineItems.Select(l => l.LineItemId).Should().Equal(Enumerable.Range(1, 14).Select(i => Guid.Parse($"00000000-0000-4000-8000-{i:D12}")));
        summary.LineItems.Should().OnlyContain(l => l.Status == PayableLineStatus.Pending && l.CalculatedAt == Now && l.Currency == "USD");
    }

    [Fact]
    public void ProcessBatch_TotalsTheLineItems_AndDigestsTheStatement()
    {
        var summary = Ledger().ProcessBatch(Events());

        summary.SummaryId.Should().Be(Guid.Parse("00000000-0000-4000-8000-000000000015"));
        summary.SiteId.Should().Be(Site);
        summary.PeriodStart.Should().Be(new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero));
        summary.PeriodEnd.Should().Be(new DateTimeOffset(2026, 2, 11, 10, 0, 0, TimeSpan.Zero));
        summary.TotalEventsProcessed.Should().Be(10);
        summary.TotalLineItems.Should().Be(14);
        summary.TotalBaseAmount.Should().Be(5149.99m);
        summary.TotalOverheadAmount.Should().Be(1190m);
        summary.TotalGrossPayable.Should().Be(6339.99m);
        summary.TotalWithholdingDeducted.Should().Be(614m);
        summary.TotalHoldbackReserved.Should().Be(400m);
        summary.TotalNetPayable.Should().Be(5325.99m);
        summary.PayeeSubtotals.Should().Equal(new Dictionary<string, decimal> { ["INST"] = 3195.59m, ["PI"] = 2130.40m });
        summary.HoldbackAccount.Should().Be(new HoldbackAccount("GC-001", Site, "USD", 400m, 0m, 400m));
        summary.Digest.Should().Be("26c89656a0794d303cade1cb448d06e0028cd3225275114e74276636202ecf74"); // pragma: allowlist secret
    }

    [Fact]
    public void ProcessBatch_WithNoEvents_CoversNow()
    {
        var summary = Ledger().ProcessBatch([]);

        summary.PeriodStart.Should().Be(Now);
        summary.PeriodEnd.Should().Be(Now);
        summary.TotalLineItems.Should().Be(0);
        summary.TotalNetPayable.Should().Be(0m);
    }

    [Fact]
    public void ReleaseHoldback_PaysTheFirstPayee_AndReducesTheBalance()
    {
        var ledger = Ledger();
        ledger.ProcessBatch(Events());

        var partial = ledger.ReleaseHoldback(50.004m, "Interim");

        partial.LineItemId.Should().Be(Guid.Parse("00000000-0000-4000-8000-000000000016"));
        partial.EventId.Should().Be("HOLDBACK_REL_00000000");
        partial.ItemCode.Should().Be(PayableLedger.HoldbackReleaseItemCode);
        partial.ItemDescription.Should().Be("Holdback Retention Escrow Release: Interim");
        partial.PayeeId.Should().Be("INST");
        partial.Status.Should().Be(PayableLineStatus.Approved);
        partial.OccurredAt.Should().Be(Now);
        partial.Breakdown.Should().Be(new PayableBreakdown(50m, 0m, 50m, 0m, 0m, 50m, 0m, 0m, 0m));
        partial.AuditHash.Should().Be("f3128d77c43cf6a6eb85747cfc7c43d303d361f9e2896adaa13d1323dea48acc"); // pragma: allowlist secret
        ledger.HoldbackAccount.Should().Be(new HoldbackAccount("GC-001", Site, "USD", 400m, 50m, 350m));

        var rest = ledger.ReleaseHoldback();

        rest.Breakdown.NetPayable.Should().Be(350m);
        rest.AuditHash.Should().Be("84c2452e4624f58c344b6f41c83875a829c310b67bf653f1caed274433b70c88"); // pragma: allowlist secret
        var empty = () => ledger.ReleaseHoldback();
        empty.Should().Throw<InvalidOperationException>().WithMessage("No holdback available for release (balance=0.00)");
    }

    [Fact]
    public void ReleaseHoldback_MakesRoomUnderTheCapAgain()
    {
        var ledger = Ledger();
        ledger.ProcessBatch(Events());
        ledger.ReleaseHoldback(100m);

        var lines = ledger.ProcessEvent(Event("E12", PaymentTriggerType.VisitCompleted, 12, Alice) with { VisitId = "V1" });

        // Gross 600 at 10% is 60, within the 100 of room the release made.
        lines.Sum(l => l.Breakdown.HoldbackAmount).Should().Be(60m);
        ledger.HoldbackAccount.CurrentBalance.Should().Be(360m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.004)]
    public void ReleaseHoldback_RejectsAnAmountBelowOneCent(double amount)
    {
        var ledger = Ledger();
        ledger.ProcessBatch(Events());

        var release = () => ledger.ReleaseHoldback((decimal)amount);

        release.Should().Throw<ArgumentOutOfRangeException>();
        ledger.HoldbackAccount.ReleasedHoldback.Should().Be(0m);
    }

    [Fact]
    public void AFeeWithAZeroAmount_ProducesNothing_ButCountsTowardItsCap()
    {
        var ledger = Ledger();

        ledger.ProcessEvent(Event("E1", PaymentTriggerType.Enrollment, 1, Alice) with { CustomAmount = 0m }).Should().BeEmpty();
        ledger.ProcessEvent(Event("E2", PaymentTriggerType.Enrollment, 2, Alice)).Should().BeEmpty();
    }

    [Fact]
    public void EventIdentifier_UsesTheFirstIdentifierTheEventCarries()
    {
        var ledger = Ledger();

        // The procedure code comes before the visit, so this does not match V1.
        ledger.ProcessEvent(Event("E1", PaymentTriggerType.VisitCompleted, 1, Alice) with { ProcedureCode = "P9", VisitId = "V1" })
            .Should().BeEmpty();
        ledger.ProcessEvent(Event("E2", PaymentTriggerType.VisitCompleted, 2, Alice) with { ProcedureCode = "", Identifier = "V1" })
            .Should().HaveCount(2);
    }

    [Fact]
    public void AContractWithSplitsThatDoNotTotal100Percent_IsRejected()
    {
        var contract = Contract() with { PayeeSplits = [new PayeeSplit("INST", "Institution", 0.6m), new PayeeSplit("PI", "Investigator", 0.3m)] };

        var open = () => new PayableLedger(contract, new FakeClock(Now));

        open.Should().Throw<ValidationException>().WithMessage("*Payee splits must sum to 1.0 (100%), got 0.9000*");
    }

    [Theory]
    [InlineData(1.01, 0.1, 0.1)]
    [InlineData(0.1, -0.1, 0.1)]
    [InlineData(0.1, 0.1, 0.51)]
    public void AContractWithARateOutOfRange_IsRejected(double overhead, double withholding, double holdback)
    {
        var contract = Contract() with
        {
            DefaultOverheadRate = (decimal)overhead,
            DefaultWithholdingRate = (decimal)withholding,
            HoldbackRule = new HoldbackReserveRule((decimal)holdback),
        };

        var open = () => new PayableLedger(contract, new FakeClock(Now));

        open.Should().Throw<ValidationException>();
    }
}
