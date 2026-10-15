using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Cadence.Core.Synthetic;

public static class EligibilityWaterfallEngine
{
    public static AttritionWaterfallResult EvaluateWaterfall(
        string protocolId,
        IEnumerable<EligibilityCriterion> criteria,
        IReadOnlyList<SyntheticPatientRecord> candidates)
    {
        var sw = Stopwatch.StartNew();
        var criterionList = criteria.ToList();
        int currentCount = candidates.Count;
        int initialCount = currentCount;

        var currentActiveCandidates = candidates.ToList();
        var steps = new List<WaterfallStep>();

        int stepIndex = 1;
        foreach (var c in criterionList)
        {
            var passedCandidates = currentActiveCandidates.Where(pat => EvaluatesTrue(c, pat)).ToList();

            int prevCount = currentActiveCandidates.Count;
            int newCount = passedCandidates.Count;
            int excluded = prevCount - newCount;
            double retention = prevCount > 0 ? (double)newCount / prevCount * 100.0 : 0.0;
            double attrition = prevCount > 0 ? (double)excluded / prevCount * 100.0 : 0.0;

            steps.Add(new WaterfallStep(
                StepNumber: stepIndex++,
                CriterionId: c.Id,
                CriterionName: c.Name,
                Category: c.Category,
                RemainingCount: newCount,
                ExcludedCount: excluded,
                RetentionPercentage: Math.Round(retention, 2),
                AttritionPercentage: Math.Round(attrition, 2)));

            currentActiveCandidates = passedCandidates;
        }

        sw.Stop();
        int finalCount = currentActiveCandidates.Count;
        double overallYield = initialCount > 0 ? (double)finalCount / initialCount * 100.0 : 0.0;

        return new AttritionWaterfallResult(
            ProtocolId: protocolId,
            InitialPopulationCount: initialCount,
            FinalEligibleCount: finalCount,
            OverallFeasibilityYieldPercentage: Math.Round(overallYield, 2),
            Steps: steps,
            LatencyMs: sw.ElapsedMilliseconds);
    }

    private static bool EvaluatesTrue(EligibilityCriterion criterion, SyntheticPatientRecord patient)
    {
        double val = GetNumericVariableValue(criterion.VariableName, patient);
        bool isInclusion = string.Equals(criterion.Category, "INCLUSION", StringComparison.OrdinalIgnoreCase);

        bool criterionSatisfied = false;

        switch (criterion.Operator.ToUpperInvariant())
        {
            case ">=":
                if (double.TryParse(criterion.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double minGte))
                    criterionSatisfied = val >= minGte;
                break;
            case "<=":
                if (double.TryParse(criterion.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxLte))
                    criterionSatisfied = val <= maxLte;
                break;
            case ">":
                if (double.TryParse(criterion.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double minGt))
                    criterionSatisfied = val > minGt;
                break;
            case "<":
                if (double.TryParse(criterion.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxLt))
                    criterionSatisfied = val < maxLt;
                break;
            case "==":
                if (string.Equals(criterion.VariableName, "Sex", StringComparison.OrdinalIgnoreCase))
                {
                    criterionSatisfied = string.Equals(patient.Sex, criterion.Value, StringComparison.OrdinalIgnoreCase);
                }
                else if (double.TryParse(criterion.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double eqVal))
                {
                    criterionSatisfied = Math.Abs(val - eqVal) < 0.0001;
                }
                break;
            case "BETWEEN":
                if (double.TryParse(criterion.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double bLow) &&
                    double.TryParse(criterion.HighValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double bHigh))
                {
                    criterionSatisfied = val >= bLow && val <= bHigh;
                }
                break;
            case "CONTAINS":
            case "IN":
                if (string.Equals(criterion.VariableName, "Sex", StringComparison.OrdinalIgnoreCase))
                {
                    criterionSatisfied = criterion.Value.Contains(patient.Sex, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    criterionSatisfied = true;
                }
                break;
            default:
                criterionSatisfied = true;
                break;
        }

        return isInclusion ? criterionSatisfied : !criterionSatisfied;
    }

    private static double GetNumericVariableValue(string varName, SyntheticPatientRecord patient) => varName.ToUpperInvariant() switch
    {
        "AGE" => patient.Age,
        "BMI" => patient.Bmi,
        "SYSTOLICBP" or "SYS_BP" or "BP_SYS" => patient.SystolicBp,
        "DIASTOLICBP" or "DIA_BP" or "BP_DIA" => patient.DiastolicBp,
        "BIOMARKER" or "LAB_BIOMARKER" => patient.BiomarkerValue,
        _ => 0.0
    };
}
