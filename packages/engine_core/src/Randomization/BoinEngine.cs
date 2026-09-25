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

    private static double Combination(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        if (k == 0 || k == n) return 1;
        if (k > n / 2) k = n - k;
        double result = 1;
        for (int i = 1; i <= k; i++)
        {
            result = result * (n - i + 1) / i;
        }
        return result;
    }

    public static double BetaInc(double a, double b, double x)
    {
        if (x <= 0.0) return 0.0;
        if (x >= 1.0) return 1.0;

        if (Math.Abs(a % 1) < 1e-9 && Math.Abs(b % 1) < 1e-9 && a >= 1 && b >= 1)
        {
            int ia = (int)a;
            int ib = (int)b;
            int n = ia + ib - 1;
            double total = 0.0;
            for (int k = ia; k <= n; k++)
            {
                total += Combination(n, k) * Math.Pow(x, k) * Math.Pow(1.0 - x, n - k);
            }
            return Math.Round(total, 6);
        }

        double lbeta = LogGamma(a) + LogGamma(b) - LogGamma(a + b);
        int steps = 1000;
        double h = x / steps;
        double totalSimp = 0.0;
        for (int i = 0; i <= steps; i++)
        {
            double t = i * h;
            double val;
            if (t <= 0.0 || t >= 1.0) val = 0.0;
            else val = Math.Exp((a - 1.0) * Math.Log(t) + (b - 1.0) * Math.Log(1.0 - t) - lbeta);

            double weight;
            if (i == 0 || i == steps) weight = 1.0;
            else if (i % 2 == 1) weight = 4.0;
            else weight = 2.0;

            totalSimp += weight * val;
        }
        double res = (h / 3.0) * totalSimp;
        return Math.Max(0.0, Math.Min(1.0, res));
    }

    private static double LogGamma(double x)
    {
        double[] p = {
            676.5203681218851, -1259.139216722289, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012,
            9.9843695780195716e-6, 1.5056327351493116e-7
        };
        if (x < 0.5) return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1 - x);
        x -= 1;
        double a = 0.99999999999980993;
        double t = x + 7.5;
        for (int i = 0; i < p.Length; i++) a += p[i] / (x + i + 1);
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
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
