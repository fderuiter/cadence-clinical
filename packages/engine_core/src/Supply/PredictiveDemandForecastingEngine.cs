using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Core.Domain.Supply;

public static class PredictiveDemandForecastingEngine
{
    private sealed class CohortTracker
    {
        public int EnrolledDay { get; set; }
        public double SubjectCount { get; set; }
    }

    private sealed class LotTracker
    {
        public string LotNumber { get; set; } = string.Empty;
        public int ExpiryDay { get; set; }
        public int Qty { get; set; }
    }

    public static PredictiveForecastResult SimulateSiteInventory(DemandForecastParameters paramsVal)
    {
        ArgumentNullException.ThrowIfNull(paramsVal);
        Validate(paramsVal);

        int horizon = paramsVal.SimulationHorizonDays;
        double dailyRecruitmentRate = paramsVal.MonthlyRecruitmentRate / 30.0;

        int currentStock = paramsVal.CurrentOnHand;
        int inTransitStock = paramsVal.CurrentInTransit;
        int inTransitDay = paramsVal.InTransitArrivalDay;

        var dispSchedule = paramsVal.DispensationSchedule ?? new List<ScheduledVisitDispensation>
        {
            new(1, 1), new(28, 1), new(56, 1), new(84, 1)
        };

        // Later entries for the same visit day replace earlier ones, as in the Python reference's dict comprehension.
        var dispMap = new Dictionary<int, int>();
        foreach (var d in dispSchedule)
        {
            dispMap[d.VisitDayOffset] = d.DispensationQty;
        }

        var subjectCohorts = new List<CohortTracker>
        {
            new() { EnrolledDay = 0, SubjectCount = paramsVal.ActiveSubjects }
        };

        var activeLots = paramsVal.ActiveLots ?? new List<LotExpiryInfo>();
        var lotStock = activeLots.Select(l => new LotTracker
        {
            LotNumber = l.LotNumber,
            ExpiryDay = l.ExpiryDayOffset,
            Qty = l.Quantity
        }).ToList();

        var dailyProjections = new List<DailyDemandProjection>();
        int? daysUntilStockout = null;
        int? recommendedReorderDay = null;
        bool reorderTriggered = false;
        var lotExpiryWarnings = new List<string>();
        int totalProjectedDemand = 0;

        int leadTimeBufferDays = paramsVal.LeadTimeDays + paramsVal.SafetyBufferDays;

        for (int day = 0; day <= horizon; day++)
        {
            int arrivedQty = 0;
            if (inTransitStock > 0 && day == inTransitDay)
            {
                arrivedQty = inTransitStock;
                currentStock += arrivedQty;
                inTransitStock = 0;
            }

            int expiredQty = 0;
            foreach (var lot in lotStock)
            {
                if (lot.ExpiryDay == day && lot.Qty > 0)
                {
                    expiredQty += lot.Qty;
                    lotExpiryWarnings.Add($"Lot '{lot.LotNumber}' expired on day #{day} ({lot.Qty} units removed from usable inventory).");
                    currentStock = Math.Max(0, currentStock - lot.Qty);
                    lot.Qty = 0;
                }
            }

            double newAccruals = day > 0 ? dailyRecruitmentRate : 0.0;
            if (newAccruals > 0)
            {
                subjectCohorts.Add(new CohortTracker { EnrolledDay = day, SubjectCount = newAccruals });
            }

            double dayDemandFloat = 0.0;
            foreach (var cohort in subjectCohorts)
            {
                int daysSinceEnrollment = day - cohort.EnrolledDay;
                if (dispMap.TryGetValue(daysSinceEnrollment, out int qtyPerSubject))
                {
                    dayDemandFloat += cohort.SubjectCount * qtyPerSubject;
                }
            }

            int dayDemand = (int)Math.Ceiling(dayDemandFloat);
            totalProjectedDemand += dayDemand;

            currentStock -= dayDemand;

            if (currentStock <= 0 && daysUntilStockout is null)
            {
                daysUntilStockout = day;
            }

            int totalActiveSubjects = (int)subjectCohorts.Sum(c => c.SubjectCount);

            var riskLevel = StockoutRiskLevel.LOW;
            if (currentStock <= 0 || (daysUntilStockout != null && (daysUntilStockout.Value - day) <= 7))
            {
                riskLevel = StockoutRiskLevel.CRITICAL;
            }
            else if (daysUntilStockout != null && (daysUntilStockout.Value - day) <= leadTimeBufferDays)
            {
                riskLevel = StockoutRiskLevel.HIGH;
            }
            else if (currentStock <= (totalActiveSubjects * 2))
            {
                riskLevel = StockoutRiskLevel.MEDIUM;
            }

            bool isReorderDay = false;
            if (!reorderTriggered && daysUntilStockout != null && (daysUntilStockout.Value - day) <= leadTimeBufferDays)
            {
                isReorderDay = true;
                reorderTriggered = true;
                recommendedReorderDay = day;
            }

            dailyProjections.Add(new DailyDemandProjection(
                DayIndex: day,
                ProjectedActiveSubjects: totalActiveSubjects,
                NewEnrollments: (int)Math.Round(newAccruals),
                ExpectedDemandKits: dayDemand,
                ProjectedOnHand: currentStock,
                ExpiredKitsDeducted: expiredQty,
                InTransitReceived: arrivedQty,
                IsReorderTriggered: isReorderDay,
                StockoutRisk: riskLevel
            ));
        }

        int deficit = currentStock < 0 ? Math.Max(0, -currentStock) : (totalProjectedDemand - paramsVal.CurrentOnHand);
        int neededQty = Math.Max(paramsVal.ResupplyLotSize, deficit + (paramsVal.ActiveSubjects * 2));
        int resupplyLots = (int)Math.Ceiling((double)neededQty / paramsVal.ResupplyLotSize);
        int recommendedQty = resupplyLots * paramsVal.ResupplyLotSize;

        var overallRisk = StockoutRiskLevel.LOW;
        if (daysUntilStockout != null)
        {
            if (daysUntilStockout.Value <= 7) overallRisk = StockoutRiskLevel.CRITICAL;
            else if (daysUntilStockout.Value <= leadTimeBufferDays) overallRisk = StockoutRiskLevel.HIGH;
            else overallRisk = StockoutRiskLevel.MEDIUM;
        }

        return new PredictiveForecastResult(
            StudyId: paramsVal.StudyId,
            SiteId: paramsVal.SiteId,
            SimulationHorizonDays: horizon,
            StartingOnHand: paramsVal.CurrentOnHand,
            TotalProjectedDemand: totalProjectedDemand,
            DaysUntilStockout: daysUntilStockout,
            RecommendedReorderDayOffset: recommendedReorderDay,
            RecommendedReorderQty: recommendedQty,
            StockoutRiskLevel: overallRisk,
            LotExpiryWarnings: lotExpiryWarnings,
            DailyProjections: dailyProjections
        );
    }

    // Mirrors the field constraints of the Python reference's pydantic DemandForecastParameters,
    // ScheduledVisitDispensation and LotExpiryInfo models, which reject these values before simulation.
    private static void Validate(DemandForecastParameters p)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(p.ActiveSubjects);
        if (!double.IsFinite(p.MonthlyRecruitmentRate) || p.MonthlyRecruitmentRate < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(p), p.MonthlyRecruitmentRate, "MonthlyRecruitmentRate must be a finite value greater than or equal to 0.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(p.SimulationHorizonDays, 7);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(p.SimulationHorizonDays, 365);
        ArgumentOutOfRangeException.ThrowIfLessThan(p.LeadTimeDays, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(p.SafetyBufferDays);
        ArgumentOutOfRangeException.ThrowIfLessThan(p.ResupplyLotSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(p.CurrentOnHand);
        ArgumentOutOfRangeException.ThrowIfNegative(p.CurrentInTransit);
        ArgumentOutOfRangeException.ThrowIfNegative(p.InTransitArrivalDay);

        foreach (var visit in p.DispensationSchedule ?? [])
        {
            ArgumentNullException.ThrowIfNull(visit);
            ArgumentOutOfRangeException.ThrowIfNegative(visit.VisitDayOffset);
            ArgumentOutOfRangeException.ThrowIfLessThan(visit.DispensationQty, 1);
        }

        foreach (var lot in p.ActiveLots ?? [])
        {
            ArgumentNullException.ThrowIfNull(lot);
            ArgumentOutOfRangeException.ThrowIfNegative(lot.ExpiryDayOffset);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lot.Quantity);
        }
    }
}
