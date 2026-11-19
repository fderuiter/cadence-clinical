using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Core.Domain.Randomization;

public static class BoinEngine
{
    public static (double LambdaE, double LambdaD) CalculateBoinBoundaries(
        double targetDltRate,
        double phi1Fraction = 0.6,
        double phi2Fraction = 1.4)
    {
        double phi = targetDltRate;
        double phi1 = phi * phi1Fraction;
        double phi2 = phi * phi2Fraction;

        double numE = Math.Log((1.0 - phi1) / (1.0 - phi));
        double denomE = Math.Log((phi * (1.0 - phi1)) / (phi1 * (1.0 - phi)));
        double lambdaE = numE / denomE;

        double numD = Math.Log((1.0 - phi) / (1.0 - phi2));
        double denomD = Math.Log((phi2 * (1.0 - phi)) / (phi * (1.0 - phi2)));
        double lambdaD = numD / denomD;

        return (Math.Round(lambdaE, 4), Math.Round(lambdaD, 4));
    }

    public static double BetaInc(double a, double b, double x)
    {
        double result = RandomizationMath.RegularizedIncompleteBeta(a, b, x);
        return RandomizationMath.HasIntegerShapeParameters(a, b)
            ? Math.Round(result, 6)
            : result;
    }

    public static double CalculatePosteriorProbToxic(int observedDlts, int evaluatedSubjects, double targetDltRate)
    {
        if (evaluatedSubjects == 0) return 0.0;

        double a = 1.0 + observedDlts;
        double b = 1.0 + evaluatedSubjects - observedDlts;
        double cdf = BetaInc(a, b, targetDltRate);
        double probExcess = 1.0 - cdf;
        return Math.Round(Math.Max(0.0, Math.Min(1.0, probExcess)), 4);
    }

    public static BOINDoseRecommendation EvaluateDoseLevel(
        BOINConfig config,
        IReadOnlyList<DoseLevelDefinition> doseLevels,
        DoseCohortState currentState,
        IReadOnlyList<DoseCohortState>? allDoseStates = null)
    {
        double lambdaE = config.EscalationBoundary ?? 0;
        double lambdaD = config.DeescalationBoundary ?? 0;
        if (config.EscalationBoundary is null || config.DeescalationBoundary is null)
        {
            var (autoE, autoD) = CalculateBoinBoundaries(config.TargetDltRate);
            if (config.EscalationBoundary is null) lambdaE = autoE;
            if (config.DeescalationBoundary is null) lambdaD = autoD;
        }

        int nJ = currentState.EvaluatedSubjects;
        int dJ = currentState.ObservedDlts;
        double pHat = currentState.EmpiricalDltRate;

        double probToxic = nJ >= 3 ? CalculatePosteriorProbToxic(dJ, nJ, config.TargetDltRate) : 0.0;

        int currIdx = currentState.DoseIndex;
        var currDef = doseLevels.FirstOrDefault(d => d.DoseIndex == currIdx);
        double doseAmt = currDef?.DoseAmount ?? currentState.DoseAmount;
        string doseUnit = currDef?.DoseUnit ?? currentState.DoseUnit;

        if (nJ >= 3 && probToxic >= config.StoppingCutoff)
        {
            if (currIdx == 0)
            {
                return new BOINDoseRecommendation(
                    CurrentDoseIndex: currIdx,
                    CurrentDoseAmount: doseAmt,
                    CurrentDoseUnit: doseUnit,
                    Action: DoseEscalationAction.STOP_TRIAL,
                    EmpiricalDltRate: pHat,
                    PosteriorProbToxic: probToxic,
                    IsTrialStopped: true,
                    Rationale: $"Lowest dose tier #{currIdx} ({doseAmt} {doseUnit}) is excessively toxic " +
                               $"(Pr(toxicity > {config.TargetDltRate}) = {probToxic:F4} >= {config.StoppingCutoff}). " +
                               "Trial halted per 21 CFR Part 11 safety stopping rule."
                );
            }

            var prevDef = doseLevels.FirstOrDefault(d => d.DoseIndex == currIdx - 1);
            return new BOINDoseRecommendation(
                CurrentDoseIndex: currIdx,
                CurrentDoseAmount: doseAmt,
                CurrentDoseUnit: doseUnit,
                Action: DoseEscalationAction.DEESCALATE,
                RecommendedDoseIndex: currIdx - 1,
                RecommendedDoseAmount: prevDef?.DoseAmount,
                EmpiricalDltRate: pHat,
                PosteriorProbToxic: probToxic,
                IsTrialStopped: false,
                Rationale: $"Dose tier #{currIdx} eliminated due to excessive toxicity " +
                           $"(Pr(toxicity > {config.TargetDltRate}) = {probToxic:F4} >= {config.StoppingCutoff}). " +
                           $"De-escalating to tier #{currIdx - 1}."
            );
        }

        int maxIdx = doseLevels.Max(d => d.DoseIndex);

        if (pHat <= lambdaE)
        {
            if (currIdx < maxIdx)
            {
                int nextIdx = currIdx + 1;
                var nextDef = doseLevels.First(d => d.DoseIndex == nextIdx);
                return new BOINDoseRecommendation(
                    CurrentDoseIndex: currIdx,
                    CurrentDoseAmount: doseAmt,
                    CurrentDoseUnit: doseUnit,
                    Action: DoseEscalationAction.ESCALATE,
                    RecommendedDoseIndex: nextIdx,
                    RecommendedDoseAmount: nextDef.DoseAmount,
                    EmpiricalDltRate: pHat,
                    PosteriorProbToxic: probToxic,
                    Rationale: $"Observed DLT rate {pHat:F4} ({dJ}/{nJ}) <= escalation boundary lambda_e ({lambdaE}). " +
                               $"Escalating to dose tier #{nextIdx} ({nextDef.DoseAmount} {nextDef.DoseUnit})."
                );
            }

            return new BOINDoseRecommendation(
                CurrentDoseIndex: currIdx,
                CurrentDoseAmount: doseAmt,
                CurrentDoseUnit: doseUnit,
                Action: DoseEscalationAction.STAY,
                RecommendedDoseIndex: currIdx,
                RecommendedDoseAmount: doseAmt,
                EmpiricalDltRate: pHat,
                PosteriorProbToxic: probToxic,
                Rationale: $"Observed DLT rate {pHat:F4} ({dJ}/{nJ}) <= lambda_e ({lambdaE}), " +
                           $"but already at maximum dose tier #{currIdx}. Staying at current dose."
            );
        }

        if (pHat >= lambdaD)
        {
            if (currIdx > 0)
            {
                int prevIdx = currIdx - 1;
                var prevDef = doseLevels.First(d => d.DoseIndex == prevIdx);
                return new BOINDoseRecommendation(
                    CurrentDoseIndex: currIdx,
                    CurrentDoseAmount: doseAmt,
                    CurrentDoseUnit: doseUnit,
                    Action: DoseEscalationAction.DEESCALATE,
                    RecommendedDoseIndex: prevIdx,
                    RecommendedDoseAmount: prevDef.DoseAmount,
                    EmpiricalDltRate: pHat,
                    PosteriorProbToxic: probToxic,
                    Rationale: $"Observed DLT rate {pHat:F4} ({dJ}/{nJ}) >= de-escalation boundary lambda_d ({lambdaD}). " +
                               $"De-escalating to dose tier #{prevIdx} ({prevDef.DoseAmount} {prevDef.DoseUnit})."
                );
            }

            return new BOINDoseRecommendation(
                CurrentDoseIndex: currIdx,
                CurrentDoseAmount: doseAmt,
                CurrentDoseUnit: doseUnit,
                Action: DoseEscalationAction.STAY,
                RecommendedDoseIndex: currIdx,
                RecommendedDoseAmount: doseAmt,
                EmpiricalDltRate: pHat,
                PosteriorProbToxic: probToxic,
                Rationale: $"Observed DLT rate {pHat:F4} >= lambda_d ({lambdaD}), but at lowest dose. " +
                           $"Staying at dose #{currIdx} (safety threshold not breached)."
            );
        }

        return new BOINDoseRecommendation(
            CurrentDoseIndex: currIdx,
            CurrentDoseAmount: doseAmt,
            CurrentDoseUnit: doseUnit,
            Action: DoseEscalationAction.STAY,
            RecommendedDoseIndex: currIdx,
            RecommendedDoseAmount: doseAmt,
            EmpiricalDltRate: pHat,
            PosteriorProbToxic: probToxic,
            Rationale: $"Observed DLT rate {pHat:F4} ({dJ}/{nJ}) is within optimal interval " +
                       $"({lambdaE}, {lambdaD}). Continuing at current dose tier #{currIdx}."
        );
    }

    public static BOINDoseRecommendation Evaluate3Plus3Cohort(
        ThreePlusThreeConfig config,
        IReadOnlyList<DoseLevelDefinition> doseLevels,
        DoseCohortState currentState)
    {
        int nJ = currentState.EvaluatedSubjects;
        int dJ = currentState.ObservedDlts;
        double pHat = currentState.EmpiricalDltRate;
        int currIdx = currentState.DoseIndex;

        var currDef = doseLevels.FirstOrDefault(d => d.DoseIndex == currIdx);
        double doseAmt = currDef?.DoseAmount ?? currentState.DoseAmount;
        string doseUnit = currDef?.DoseUnit ?? currentState.DoseUnit;
        int maxIdx = doseLevels.Max(d => d.DoseIndex);

        if (nJ == 3)
        {
            if (dJ == 0)
            {
                if (currIdx < maxIdx)
                {
                    int nextIdx = currIdx + 1;
                    var nextDef = doseLevels.First(d => d.DoseIndex == nextIdx);
                    return new BOINDoseRecommendation(
                        CurrentDoseIndex: currIdx, CurrentDoseAmount: doseAmt, CurrentDoseUnit: doseUnit,
                        Action: DoseEscalationAction.ESCALATE, RecommendedDoseIndex: nextIdx, RecommendedDoseAmount: nextDef.DoseAmount,
                        EmpiricalDltRate: pHat, Rationale: $"3+3 Rule: 0/3 DLTs observed. Escalating to dose tier #{nextIdx}."
                    );
                }
                return new BOINDoseRecommendation(
                    CurrentDoseIndex: currIdx, CurrentDoseAmount: doseAmt, CurrentDoseUnit: doseUnit,
                    Action: DoseEscalationAction.STAY, RecommendedDoseIndex: currIdx, RecommendedDoseAmount: doseAmt,
                    EmpiricalDltRate: pHat, Rationale: $"3+3 Rule: 0/3 DLTs observed at maximum dose #{currIdx}. Recommended MTD reached."
                );
            }
            if (dJ == 1)
            {
                return new BOINDoseRecommendation(
                    CurrentDoseIndex: currIdx, CurrentDoseAmount: doseAmt, CurrentDoseUnit: doseUnit,
                    Action: DoseEscalationAction.STAY, RecommendedDoseIndex: currIdx, RecommendedDoseAmount: doseAmt,
                    EmpiricalDltRate: pHat, Rationale: $"3+3 Rule: 1/3 DLTs observed. Expanding cohort to 6 subjects at current dose #{currIdx}."
                );
            }

            if (currIdx > 0)
            {
                int prevIdx = currIdx - 1;
                var prevDef = doseLevels.First(d => d.DoseIndex == prevIdx);
                return new BOINDoseRecommendation(
                    CurrentDoseIndex: currIdx, CurrentDoseAmount: doseAmt, CurrentDoseUnit: doseUnit,
                    Action: DoseEscalationAction.DEESCALATE, RecommendedDoseIndex: prevIdx, RecommendedDoseAmount: prevDef.DoseAmount,
                    EmpiricalDltRate: pHat, Rationale: $"3+3 Rule: {dJ}/3 DLTs observed. MTD exceeded. De-escalating to tier #{prevIdx}."
                );
            }
            return new BOINDoseRecommendation(
                CurrentDoseIndex: currIdx, CurrentDoseAmount: doseAmt, CurrentDoseUnit: doseUnit,
                Action: DoseEscalationAction.STOP_TRIAL, EmpiricalDltRate: pHat, IsTrialStopped: true,
                Rationale: $"3+3 Rule: {dJ}/3 DLTs observed at lowest dose #{currIdx}. Trial halted."
            );
        }

        if (nJ >= 6)
        {
            if (dJ <= 1)
            {
                if (currIdx < maxIdx)
                {
                    int nextIdx = currIdx + 1;
                    var nextDef = doseLevels.First(d => d.DoseIndex == nextIdx);
                    return new BOINDoseRecommendation(
                        CurrentDoseIndex: currIdx, CurrentDoseAmount: doseAmt, CurrentDoseUnit: doseUnit,
                        Action: DoseEscalationAction.ESCALATE, RecommendedDoseIndex: nextIdx, RecommendedDoseAmount: nextDef.DoseAmount,
                        EmpiricalDltRate: pHat, Rationale: $"3+3 Rule: {dJ}/{nJ} DLTs in expanded cohort. Escalating to tier #{nextIdx}."
                    );
                }
                return new BOINDoseRecommendation(
                    CurrentDoseIndex: currIdx, CurrentDoseAmount: doseAmt, CurrentDoseUnit: doseUnit,
                    Action: DoseEscalationAction.STAY, RecommendedDoseIndex: currIdx, RecommendedDoseAmount: doseAmt,
                    EmpiricalDltRate: pHat, Rationale: $"3+3 Rule: {dJ}/{nJ} DLTs at maximum tier. Dose #{currIdx} confirmed as MTD."
                );
            }

            if (currIdx > 0)
            {
                int prevIdx = currIdx - 1;
                var prevDef = doseLevels.First(d => d.DoseIndex == prevIdx);
                return new BOINDoseRecommendation(
                    CurrentDoseIndex: currIdx, CurrentDoseAmount: doseAmt, CurrentDoseUnit: doseUnit,
                    Action: DoseEscalationAction.DEESCALATE, RecommendedDoseIndex: prevIdx, RecommendedDoseAmount: prevDef.DoseAmount,
                    EmpiricalDltRate: pHat, Rationale: $"3+3 Rule: {dJ}/{nJ} DLTs in expanded cohort. MTD exceeded. De-escalating to tier #{prevIdx}."
                );
            }
            return new BOINDoseRecommendation(
                CurrentDoseIndex: currIdx, CurrentDoseAmount: doseAmt, CurrentDoseUnit: doseUnit,
                Action: DoseEscalationAction.STOP_TRIAL, EmpiricalDltRate: pHat, IsTrialStopped: true,
                Rationale: $"3+3 Rule: {dJ}/{nJ} DLTs at lowest dose #{currIdx}. Trial halted."
            );
        }

        return new BOINDoseRecommendation(
            CurrentDoseIndex: currIdx, CurrentDoseAmount: doseAmt, CurrentDoseUnit: doseUnit,
            Action: DoseEscalationAction.STAY, RecommendedDoseIndex: currIdx, RecommendedDoseAmount: doseAmt,
            EmpiricalDltRate: pHat, Rationale: $"Cohort in progress ({nJ} subjects evaluated). Continuing at current dose."
        );
    }
}
