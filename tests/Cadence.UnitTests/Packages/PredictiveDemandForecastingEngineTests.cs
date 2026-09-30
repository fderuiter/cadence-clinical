using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain.Supply;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Behavioural tests for the C# port of <c>packages/engine_core/supply/forecasting.py</c>, deleted once these tests landed and kept in git history. Expected values
/// were produced by running the Python reference engine on the same parameters.
/// </summary>
public class PredictiveDemandForecastingEngineTests
{
    private static DemandForecastParameters Params(
        int activeSubjects = 0,
        double monthlyRecruitmentRate = 0.0,
        int horizon = 90,
        int leadTimeDays = 14,
        int safetyBufferDays = 7,
        int resupplyLotSize = 10,
        int onHand = 0,
        int inTransit = 0,
        int inTransitArrivalDay = 3,
        List<ScheduledVisitDispensation>? schedule = null,
        List<LotExpiryInfo>? lots = null) =>
        new(
            StudyId: "STUDY-001",
            SiteId: "SITE-01",
            ActiveSubjects: activeSubjects,
            MonthlyRecruitmentRate: monthlyRecruitmentRate,
            SimulationHorizonDays: horizon,
            LeadTimeDays: leadTimeDays,
            SafetyBufferDays: safetyBufferDays,
            ResupplyLotSize: resupplyLotSize,
            CurrentOnHand: onHand,
            CurrentInTransit: inTransit,
            InTransitArrivalDay: inTransitArrivalDay,
            DispensationSchedule: schedule,
            ActiveLots: lots);

    private static DailyDemandProjection Day(PredictiveForecastResult result, int day) =>
        result.DailyProjections!.Single(d => d.DayIndex == day);

    [Fact]
    public void Simulate_WithDefaultVisitSchedule_DispensesOnVisitDaysOnly()
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(Params(activeSubjects: 10, onHand: 100));

        result.StudyId.Should().Be("STUDY-001");
        result.SiteId.Should().Be("SITE-01");
        result.SimulationHorizonDays.Should().Be(90);
        result.StartingOnHand.Should().Be(100);
        result.DailyProjections.Should().HaveCount(91);
        result.DailyProjections!.Select(d => d.DayIndex).Should().Equal(Enumerable.Range(0, 91));
        result.DailyProjections!.Where(d => d.ExpectedDemandKits > 0).Select(d => d.DayIndex)
            .Should().Equal(1, 28, 56, 84);
        result.DailyProjections!.Where(d => d.ExpectedDemandKits > 0).Should().OnlyContain(d => d.ExpectedDemandKits == 10);
        Day(result, 1).ProjectedOnHand.Should().Be(90);
        Day(result, 28).ProjectedOnHand.Should().Be(80);
        Day(result, 90).ProjectedOnHand.Should().Be(60);
        result.DailyProjections!.Should().OnlyContain(d => d.ProjectedActiveSubjects == 10 && d.NewEnrollments == 0);
        result.DailyProjections!.Should().OnlyContain(d => d.StockoutRisk == StockoutRiskLevel.LOW && !d.IsReorderTriggered);

        result.TotalProjectedDemand.Should().Be(40);
        result.DaysUntilStockout.Should().BeNull();
        result.RecommendedReorderDayOffset.Should().BeNull();
        result.RecommendedReorderQty.Should().Be(10);
        result.StockoutRiskLevel.Should().Be(StockoutRiskLevel.LOW);
        result.LotExpiryWarnings.Should().BeEmpty();
    }

    [Fact]
    public void Simulate_WithEmptyVisitSchedule_ProjectsNoDemand()
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(
            Params(activeSubjects: 10, onHand: 50, horizon: 30, schedule: []));

        result.TotalProjectedDemand.Should().Be(0);
        result.DailyProjections!.Should().OnlyContain(d => d.ExpectedDemandKits == 0 && d.ProjectedOnHand == 50);
        result.DaysUntilStockout.Should().BeNull();
    }

    [Fact]
    public void Simulate_WhenStockRunsOut_RecordsStockoutDayReorderAndDailyRisk()
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(Params(activeSubjects: 10, onHand: 25));

        result.DaysUntilStockout.Should().Be(56);
        result.RecommendedReorderDayOffset.Should().Be(56);
        result.DailyProjections!.Where(d => d.IsReorderTriggered).Select(d => d.DayIndex).Should().Equal(56);
        result.TotalProjectedDemand.Should().Be(40);
        Day(result, 56).ProjectedOnHand.Should().Be(-5);
        Day(result, 90).ProjectedOnHand.Should().Be(-15);

        // Day 0 is above the two-kits-per-subject threshold; days 1-55 at or below it; stockout from day 56 on.
        Day(result, 0).StockoutRisk.Should().Be(StockoutRiskLevel.LOW);
        result.DailyProjections!.Where(d => d.DayIndex is >= 1 and <= 55)
            .Should().OnlyContain(d => d.StockoutRisk == StockoutRiskLevel.MEDIUM);
        result.DailyProjections!.Where(d => d.DayIndex >= 56)
            .Should().OnlyContain(d => d.StockoutRisk == StockoutRiskLevel.CRITICAL);

        // Final deficit 15 + 2 kits x 10 subjects = 35, rounded up to 4 lots of 10.
        result.RecommendedReorderQty.Should().Be(40);
        result.StockoutRiskLevel.Should().Be(StockoutRiskLevel.MEDIUM);
    }

    [Theory]
    [InlineData(7, 14, 7, StockoutRiskLevel.CRITICAL)]
    [InlineData(8, 14, 7, StockoutRiskLevel.HIGH)]
    [InlineData(21, 14, 7, StockoutRiskLevel.HIGH)]
    [InlineData(22, 14, 7, StockoutRiskLevel.MEDIUM)]
    [InlineData(8, 3, 2, StockoutRiskLevel.MEDIUM)]
    [InlineData(12, 10, 2, StockoutRiskLevel.HIGH)]
    public void Simulate_ClassifiesOverallRiskByStockoutDayAgainstSevenDaysAndLeadTimeBuffer(
        int stockoutDay, int leadTimeDays, int safetyBufferDays, StockoutRiskLevel expected)
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(Params(
            activeSubjects: 4,
            onHand: 5,
            horizon: 30,
            leadTimeDays: leadTimeDays,
            safetyBufferDays: safetyBufferDays,
            schedule: [new ScheduledVisitDispensation(stockoutDay, 2)]));

        result.DaysUntilStockout.Should().Be(stockoutDay);
        result.RecommendedReorderDayOffset.Should().Be(stockoutDay);
        result.StockoutRiskLevel.Should().Be(expected);
        result.TotalProjectedDemand.Should().Be(8);
        result.RecommendedReorderQty.Should().Be(20);
        Day(result, stockoutDay - 1).StockoutRisk.Should().Be(StockoutRiskLevel.MEDIUM);
        Day(result, stockoutDay).StockoutRisk.Should().Be(StockoutRiskLevel.CRITICAL);
        Day(result, stockoutDay).ProjectedOnHand.Should().Be(-3);
    }

    [Theory]
    [InlineData(30, StockoutRiskLevel.MEDIUM)]
    [InlineData(31, StockoutRiskLevel.LOW)]
    public void Simulate_DailyRiskIsMediumAtOrBelowTwoKitsPerActiveSubject(int onHand, StockoutRiskLevel expectedDayOneRisk)
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(
            Params(activeSubjects: 10, onHand: onHand, horizon: 7));

        Day(result, 1).ProjectedOnHand.Should().Be(onHand - 10);
        Day(result, 1).StockoutRisk.Should().Be(expectedDayOneRisk);
        result.StockoutRiskLevel.Should().Be(StockoutRiskLevel.LOW);
    }

    [Fact]
    public void Simulate_RemovesExpiredLotsOnTheirExpiryDayAndWarns()
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(Params(
            activeSubjects: 2,
            onHand: 40,
            horizon: 10,
            lots:
            [
                new LotExpiryInfo("LOT-A", 5, 15),
                new LotExpiryInfo("LOT-B", 200, 99),
                new LotExpiryInfo("LOT-C", 5, 3),
            ]));

        result.LotExpiryWarnings.Should().Equal(
            "Lot 'LOT-A' expired on day #5 (15 units removed from usable inventory).",
            "Lot 'LOT-C' expired on day #5 (3 units removed from usable inventory).");
        Day(result, 4).ProjectedOnHand.Should().Be(38);
        Day(result, 5).ExpiredKitsDeducted.Should().Be(18);
        Day(result, 5).ProjectedOnHand.Should().Be(20);
        result.DailyProjections!.Where(d => d.DayIndex != 5).Should().OnlyContain(d => d.ExpiredKitsDeducted == 0);
        Day(result, 10).ProjectedOnHand.Should().Be(20);
        result.DaysUntilStockout.Should().BeNull();
        result.StockoutRiskLevel.Should().Be(StockoutRiskLevel.LOW);
    }

    [Fact]
    public void Simulate_LotExpiryNeverTakesStockBelowZeroButCountsAsStockout()
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(Params(
            activeSubjects: 2,
            onHand: 10,
            horizon: 10,
            lots: [new LotExpiryInfo("LOT-A", 2, 30)]));

        Day(result, 1).ProjectedOnHand.Should().Be(8);
        Day(result, 2).ExpiredKitsDeducted.Should().Be(30);
        Day(result, 2).ProjectedOnHand.Should().Be(0);
        Day(result, 2).IsReorderTriggered.Should().BeTrue();
        Day(result, 2).StockoutRisk.Should().Be(StockoutRiskLevel.CRITICAL);
        result.LotExpiryWarnings.Should().ContainSingle()
            .Which.Should().Be("Lot 'LOT-A' expired on day #2 (30 units removed from usable inventory).");
        result.DaysUntilStockout.Should().Be(2);
        result.RecommendedReorderDayOffset.Should().Be(2);
        result.StockoutRiskLevel.Should().Be(StockoutRiskLevel.CRITICAL);
        result.RecommendedReorderQty.Should().Be(10);
    }

    [Fact]
    public void Simulate_ReceivesInTransitStockOnArrivalDay_RiskStaysCriticalAfterStockout()
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(Params(
            activeSubjects: 5,
            onHand: 3,
            inTransit: 20,
            inTransitArrivalDay: 3,
            horizon: 10));

        Day(result, 0).StockoutRisk.Should().Be(StockoutRiskLevel.MEDIUM);
        Day(result, 1).ProjectedOnHand.Should().Be(-2);
        Day(result, 1).IsReorderTriggered.Should().BeTrue();
        Day(result, 3).InTransitReceived.Should().Be(20);
        Day(result, 3).ProjectedOnHand.Should().Be(18);
        result.DailyProjections!.Where(d => d.DayIndex != 3).Should().OnlyContain(d => d.InTransitReceived == 0);
        result.DailyProjections!.Where(d => d.DayIndex >= 1)
            .Should().OnlyContain(d => d.StockoutRisk == StockoutRiskLevel.CRITICAL);
        result.DaysUntilStockout.Should().Be(1);
        result.RecommendedReorderQty.Should().Be(20);
        result.StockoutRiskLevel.Should().Be(StockoutRiskLevel.CRITICAL);
    }

    [Fact]
    public void Simulate_ReceivesInTransitStockOnDayZero()
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(Params(
            activeSubjects: 3,
            onHand: 0,
            inTransit: 20,
            inTransitArrivalDay: 0,
            horizon: 7));

        Day(result, 0).InTransitReceived.Should().Be(20);
        Day(result, 0).ProjectedOnHand.Should().Be(20);
        Day(result, 1).ProjectedOnHand.Should().Be(17);
        result.DaysUntilStockout.Should().BeNull();
        result.StockoutRiskLevel.Should().Be(StockoutRiskLevel.LOW);
    }

    [Fact]
    public void Simulate_IgnoresInTransitStockArrivingAfterHorizon()
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(Params(
            activeSubjects: 5,
            onHand: 3,
            inTransit: 20,
            inTransitArrivalDay: 40,
            horizon: 30));

        result.DailyProjections!.Should().OnlyContain(d => d.InTransitReceived == 0);
        result.DaysUntilStockout.Should().Be(1);
        result.TotalProjectedDemand.Should().Be(10);
        result.RecommendedReorderQty.Should().Be(20);
    }

    [Fact]
    public void Simulate_FractionalDailyAccrualsRoundDemandUpPerDay()
    {
        // 3 subjects per 30 days = 0.1 per day; each daily cohort reaches its day-1 visit one day later.
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(
            Params(monthlyRecruitmentRate: 3.0, onHand: 50, horizon: 10));

        Day(result, 0).ExpectedDemandKits.Should().Be(0);
        Day(result, 1).ExpectedDemandKits.Should().Be(0);
        result.DailyProjections!.Where(d => d.DayIndex >= 2).Should().OnlyContain(d => d.ExpectedDemandKits == 1);
        result.DailyProjections!.Should().OnlyContain(d => d.NewEnrollments == 0 && d.ProjectedActiveSubjects == 0);
        Day(result, 10).ProjectedOnHand.Should().Be(41);
        result.TotalProjectedDemand.Should().Be(9);
    }

    [Fact]
    public void Simulate_NewEnrollmentsUseBankersRoundingAndActiveSubjectsTruncate()
    {
        // 75 subjects per 30 days = 2.5 per day, which rounds half to even (2), as Python's round() does.
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(
            Params(monthlyRecruitmentRate: 75, onHand: 1000, horizon: 7));

        Day(result, 0).NewEnrollments.Should().Be(0);
        result.DailyProjections!.Where(d => d.DayIndex >= 1).Should().OnlyContain(d => d.NewEnrollments == 2);
        result.DailyProjections!.Select(d => d.ProjectedActiveSubjects).Should().Equal(0, 2, 5, 7, 10, 12, 15, 17);
        result.DailyProjections!.Select(d => d.ExpectedDemandKits).Should().Equal(0, 0, 3, 3, 3, 3, 3, 3);
        result.TotalProjectedDemand.Should().Be(18);
        Day(result, 7).ProjectedOnHand.Should().Be(982);
    }

    [Fact]
    public void Simulate_DuplicateVisitDaysKeepTheLastScheduledQuantity()
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(Params(
            activeSubjects: 3,
            onHand: 30,
            horizon: 10,
            schedule: [new ScheduledVisitDispensation(2, 1), new ScheduledVisitDispensation(2, 4)]));

        Day(result, 2).ExpectedDemandKits.Should().Be(12);
        result.TotalProjectedDemand.Should().Be(12);
    }

    [Theory]
    [InlineData(10, 0.0, 100, 7, 25, 25)]
    [InlineData(10, 0.0, 0, 7, 25, 50)]
    [InlineData(10, 30.0, 35, 30, 7, 42)]
    public void Simulate_RoundsRecommendedReorderQuantityUpToWholeResupplyLots(
        int activeSubjects, double monthlyRecruitmentRate, int onHand, int horizon, int lotSize, int expectedQty)
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(Params(
            activeSubjects: activeSubjects,
            monthlyRecruitmentRate: monthlyRecruitmentRate,
            onHand: onHand,
            horizon: horizon,
            resupplyLotSize: lotSize));

        result.RecommendedReorderQty.Should().Be(expectedQty);
        (result.RecommendedReorderQty % lotSize).Should().Be(0);
    }

    [Fact]
    public void Simulate_WithModelDefaults_MatchesPythonReference()
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(new DemandForecastParameters("STUDY-001", "SITE-01"));

        result.SimulationHorizonDays.Should().Be(90);
        result.TotalProjectedDemand.Should().Be(89);
        result.DaysUntilStockout.Should().Be(0);
        result.RecommendedReorderDayOffset.Should().Be(0);
        result.RecommendedReorderQty.Should().Be(90);
        result.StockoutRiskLevel.Should().Be(StockoutRiskLevel.CRITICAL);
        Day(result, 0).IsReorderTriggered.Should().BeTrue();
        Day(result, 2).ProjectedOnHand.Should().Be(-1);
        Day(result, 30).ProjectedActiveSubjects.Should().Be(1);
        Day(result, 90).ProjectedActiveSubjects.Should().Be(5);
        Day(result, 90).ProjectedOnHand.Should().Be(-89);
    }

    public static TheoryData<string, DemandForecastParameters> InvalidParameters => new()
    {
        { "negative active subjects", Params(activeSubjects: -1) },
        { "negative recruitment rate", Params(monthlyRecruitmentRate: -0.5) },
        { "NaN recruitment rate", Params(monthlyRecruitmentRate: double.NaN) },
        { "horizon below 7", Params(horizon: 6) },
        { "horizon above 365", Params(horizon: 366) },
        { "lead time below 1", Params(leadTimeDays: 0) },
        { "negative safety buffer", Params(safetyBufferDays: -1) },
        { "resupply lot size below 1", Params(resupplyLotSize: 0) },
        { "negative on hand", Params(onHand: -1) },
        { "negative in transit", Params(inTransit: -1) },
        { "negative arrival day", Params(inTransitArrivalDay: -1) },
        { "negative visit day", Params(schedule: [new ScheduledVisitDispensation(-1)]) },
        { "zero dispensation quantity", Params(schedule: [new ScheduledVisitDispensation(1, 0)]) },
        { "negative lot expiry day", Params(lots: [new LotExpiryInfo("LOT-A", -1, 5)]) },
        { "zero lot quantity", Params(lots: [new LotExpiryInfo("LOT-A", 5, 0)]) },
    };

    [Theory]
    [MemberData(nameof(InvalidParameters))]
    public void Simulate_RejectsParametersOutsideTheReferenceModelConstraints(string because, DemandForecastParameters parameters)
    {
        var act = () => PredictiveDemandForecastingEngine.SimulateSiteInventory(parameters);

        act.Should().Throw<ArgumentOutOfRangeException>(because);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(365)]
    public void Simulate_AcceptsHorizonBounds(int horizon)
    {
        var result = PredictiveDemandForecastingEngine.SimulateSiteInventory(Params(horizon: horizon));

        result.DailyProjections.Should().HaveCount(horizon + 1);
    }

    [Fact]
    public void Simulate_RejectsNullParameters()
    {
        var act = () => PredictiveDemandForecastingEngine.SimulateSiteInventory(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
