using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Cadence.Core.Synthetic;

public static class PropensityScoreMatchingEngine
{
    public static PropensityMatchingResult MatchSyntheticArm(
        PropensityMatchingRequest request,
        IReadOnlyList<SyntheticPatientRecord> rweCandidatePool)
    {
        var sw = Stopwatch.StartNew();
        string simId = $"SIM-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        // 1. Generate target/treatment cohort using Monte Carlo sampler
        int targetSize = Math.Max(50, Math.Min(5000, request.TargetSampleSize));
        var treatmentCohort = MonteCarloCohortSampler.GenerateCohort(
            sampleSize: targetSize,
            meanAge: 60.0,
            sdAge: 10.0,
            maleRatio: 0.55,
            meanBmi: 28.0,
            sdBmi: 4.0,
            meanSystolicBp: 130.0,
            sdSystolicBp: 12.0,
            meanBiomarker: 5.0,
            sdBiomarker: 2.0,
            seed: 101);

        // 2. Pool candidate controls from RWE pool or generate if pool empty
        var controlPool = (rweCandidatePool != null && rweCandidatePool.Count > 0)
            ? rweCandidatePool.ToList()
            : MonteCarloCohortSampler.GenerateCohort(
                sampleSize: targetSize * 3,
                meanAge: 56.0,
                sdAge: 13.0,
                maleRatio: 0.48,
                meanBmi: 26.5,
                sdBmi: 5.0,
                meanSystolicBp: 125.0,
                sdSystolicBp: 15.0,
                meanBiomarker: 3.8,
                sdBiomarker: 1.5,
                seed: 202);

        // 3. Compute Propensity Scores for Treatment and Control
        ComputePropensityScores(treatmentCohort, isTreatment: true);
        ComputePropensityScores(controlPool, isTreatment: false);

        // 4. Calculate propensity score standard deviation for caliper calculation
        var allScores = treatmentCohort.Select(t => t.PropensityScore)
            .Concat(controlPool.Select(c => c.PropensityScore)).ToList();
        double sdScore = CalculateStdDev(allScores);
        double maxDistanceCaliper = Math.Max(0.01, request.Caliper * (sdScore > 0 ? sdScore : 0.20));

        // 5. Nearest-Neighbor Matching within Caliper
        var matchedControlPool = new List<SyntheticPatientRecord>();
        var matchedTreatmentList = new List<SyntheticPatientRecord>();
        var availableControls = new List<SyntheticPatientRecord>(controlPool);

        foreach (var treatPat in treatmentCohort)
        {
            SyntheticPatientRecord? bestMatch = null;
            double bestDist = double.MaxValue;

            for (int i = 0; i < availableControls.Count; i++)
            {
                double dist = Math.Abs(treatPat.PropensityScore - availableControls[i].PropensityScore);
                if (dist <= maxDistanceCaliper && dist < bestDist)
                {
                    bestDist = dist;
                    bestMatch = availableControls[i];
                }
            }

            if (bestMatch != null)
            {
                availableControls.Remove(bestMatch);
                var matchedControl = bestMatch with
                {
                    IsMatched = true,
                    MatchedControlId = treatPat.PatientId
                };
                var matchedTreat = treatPat with
                {
                    IsMatched = true,
                    MatchedControlId = matchedControl.PatientId
                };

                matchedTreatmentList.Add(matchedTreat);
                matchedControlPool.Add(matchedControl);
            }
        }

        // 6. Compute Covariate Balance Metrics (Pre vs Post Match SMDs)
        var balanceMetrics = CalculateBalanceMetrics(
            treatmentCohort, controlPool, matchedTreatmentList, matchedControlPool);

        sw.Stop();

        int matchedCount = matchedControlPool.Count;
        double matchRate = treatmentCohort.Count > 0 ? (double)matchedCount / treatmentCohort.Count * 100.0 : 0.0;
        double meanPsTreat = matchedTreatmentList.Count > 0 ? matchedTreatmentList.Average(t => t.PropensityScore) : 0.0;
        double meanPsControl = matchedControlPool.Count > 0 ? matchedControlPool.Average(c => c.PropensityScore) : 0.0;

        return new PropensityMatchingResult(
            SimulationId: simId,
            StudyId: request.StudyId,
            TotalCandidateRecordsEvaluated: treatmentCohort.Count + controlPool.Count,
            MatchedTreatmentCount: matchedTreatmentList.Count,
            MatchedControlCount: matchedControlPool.Count,
            MatchRatePercentage: Math.Round(matchRate, 2),
            MeanPropensityScoreTreatment: Math.Round(meanPsTreat, 4),
            MeanPropensityScoreControl: Math.Round(meanPsControl, 4),
            CovariateBalance: balanceMetrics,
            MatchedSyntheticArm: matchedControlPool,
            LatencyMs: sw.ElapsedMilliseconds);
    }

    private static void ComputePropensityScores(List<SyntheticPatientRecord> cohort, bool isTreatment)
    {
        double intercept = isTreatment ? -3.0 : -3.5;
        for (int i = 0; i < cohort.Count; i++)
        {
            var p = cohort[i];
            // Logistic model approximation: logit = intercept + beta1*Age + beta2*Sex + beta3*BMI + beta4*SysBp + beta5*Biomarker
            double sexVal = string.Equals(p.Sex, "M", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
            double logit = intercept
                           + 0.035 * p.Age
                           + 0.25 * sexVal
                           + 0.04 * p.Bmi
                           + 0.015 * p.SystolicBp
                           + 0.15 * p.BiomarkerValue;

            double ps = 1.0 / (1.0 + Math.Exp(-logit));
            cohort[i] = p with { PropensityScore = Math.Round(ps, 4) };
        }
    }

    private static List<CovariateBalanceMetric> CalculateBalanceMetrics(
        List<SyntheticPatientRecord> preTreat,
        List<SyntheticPatientRecord> preControl,
        List<SyntheticPatientRecord> postTreat,
        List<SyntheticPatientRecord> postControl)
    {
        var metrics = new List<CovariateBalanceMetric>();

        metrics.Add(ComputeMetric("Age", p => p.Age, preTreat, preControl, postTreat, postControl));
        metrics.Add(ComputeMetric("Sex (Male %)", p => string.Equals(p.Sex, "M", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0, preTreat, preControl, postTreat, postControl));
        metrics.Add(ComputeMetric("BMI", p => p.Bmi, preTreat, preControl, postTreat, postControl));
        metrics.Add(ComputeMetric("Systolic BP", p => p.SystolicBp, preTreat, preControl, postTreat, postControl));
        metrics.Add(ComputeMetric("Biomarker Value", p => p.BiomarkerValue, preTreat, preControl, postTreat, postControl));

        return metrics;
    }

    private static CovariateBalanceMetric ComputeMetric(
        string name,
        Func<SyntheticPatientRecord, double> selector,
        List<SyntheticPatientRecord> preTreat,
        List<SyntheticPatientRecord> preControl,
        List<SyntheticPatientRecord> postTreat,
        List<SyntheticPatientRecord> postControl)
    {
        double preMeanT = preTreat.Count > 0 ? preTreat.Average(selector) : 0.0;
        double preMeanC = preControl.Count > 0 ? preControl.Average(selector) : 0.0;
        double preSmd = CalculateSmd(preMeanT, preMeanC, preTreat.Select(selector), preControl.Select(selector));

        double postMeanT = postTreat.Count > 0 ? postTreat.Average(selector) : 0.0;
        double postMeanC = postControl.Count > 0 ? postControl.Average(selector) : 0.0;
        double postSmd = CalculateSmd(postMeanT, postMeanC, postTreat.Select(selector), postControl.Select(selector));

        return new CovariateBalanceMetric(
            CovariateName: name,
            PreMatchMeanTreatment: Math.Round(preMeanT, 2),
            PreMatchMeanControl: Math.Round(preMeanC, 2),
            PreMatchSmd: Math.Round(preSmd, 3),
            PostMatchMeanTreatment: Math.Round(postMeanT, 2),
            PostMatchMeanControl: Math.Round(postMeanC, 2),
            PostMatchSmd: Math.Round(postSmd, 3),
            IsBalanced: Math.Abs(postSmd) < 0.10);
    }

    private static double CalculateSmd(double meanT, double meanC, IEnumerable<double> valsT, IEnumerable<double> valsC)
    {
        double varT = CalculateVariance(valsT, meanT);
        double varC = CalculateVariance(valsC, meanC);
        double pooledSd = Math.Sqrt((varT + varC) / 2.0);
        return pooledSd > 0 ? Math.Abs(meanT - meanC) / pooledSd : 0.0;
    }

    private static double CalculateVariance(IEnumerable<double> values, double mean)
    {
        var list = values.ToList();
        if (list.Count <= 1) return 0.0;
        double sumSq = list.Sum(v => Math.Pow(v - mean, 2));
        return sumSq / (list.Count - 1);
    }

    private static double CalculateStdDev(List<double> values)
    {
        if (values == null || values.Count <= 1) return 0.0;
        double mean = values.Average();
        double sumSq = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumSq / (values.Count - 1));
    }
}
