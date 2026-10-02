using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Sentinels.Checks;

public class SiteVarianceSentinelCheck : IFraudSentinelCheck
{
    public string Name => "SiteVarianceCheck";
    public string Description => "Evaluates variance of site observation values against study baseline cohort variance for shrinkage or inflation anomalies.";

    public Task<SentinelResult> RunAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new SentinelResult(Name, Passed: true, Summary: Description));
    }

    public Task<SentinelResult> EvaluateAsync(FraudEvaluationContext context, CancellationToken ct = default)
    {
        if (context.Observations.Count == 0)
        {
            return Task.FromResult(new SentinelResult(
                Name,
                Passed: true,
                Summary: "No observations available for site variance analysis."));
        }

        var violations = new List<string>();
        var groupedByTest = context.Observations
            .GroupBy(o => o.TestCode, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedByTest)
        {
            string testCode = group.Key;
            var values = group.Select(o => o.Value).ToList();
            int n = values.Count;

            if (n < 2)
            {
                if (context.Baseline.FieldStats != null &&
                    context.Baseline.FieldStats.TryGetValue(testCode, out var stats) &&
                    stats.SampleSize >= 5 && stats.Variance > 0.0)
                {
                    double val = values[0];
                    double zScore = Math.Abs(val - stats.Mean) / Math.Sqrt(stats.Variance);
                    if (zScore > 4.0)
                    {
                        violations.Add($"Extreme single observation outlier for test {testCode}: value={val:F2}, baseline mean={stats.Mean:F2}, stdDev={Math.Sqrt(stats.Variance):F2} (Z={zScore:F2}).");
                    }
                }
                continue;
            }

            double mean = values.Average();
            double sumOfSquares = values.Sum(v => Math.Pow(v - mean, 2));
            double siteVariance = sumOfSquares / (n - 1);

            if (context.Baseline.FieldStats != null &&
                context.Baseline.FieldStats.TryGetValue(testCode, out var fieldStats) &&
                fieldStats.SampleSize >= 5 && fieldStats.Variance > 0.0)
            {
                double baselineVariance = fieldStats.Variance;
                double varianceRatio = siteVariance / baselineVariance;

                // 1. Variance Shrinkage (unusually low or zero variance indicating artificial/fabricated data)
                if ((siteVariance < 1e-9 || varianceRatio < 0.15) && n >= 2 && baselineVariance > 0.5)
                {
                    violations.Add($"Site variance shrinkage anomaly detected for test {testCode}: site variance ({siteVariance:F2}) is abnormally low compared to study baseline cohort variance ({baselineVariance:F2}), ratio F={varianceRatio:F2}. Potential data fabrication or missing biological variation.");
                }
                // 2. Variance Inflation (unusually high variance)
                else if (varianceRatio > 5.0 && n >= 2)
                {
                    violations.Add($"Site variance inflation anomaly detected for test {testCode}: site variance ({siteVariance:F2}) is abnormally high compared to study baseline cohort variance ({baselineVariance:F2}), ratio F={varianceRatio:F2}.");
                }
            }
        }

        bool passed = violations.Count == 0;

        return Task.FromResult(new SentinelResult(
            Name,
            Passed: passed,
            Summary: passed
                ? "Site variance checks passed across evaluated observation fields."
                : $"Site variance checks failed with {violations.Count} anomaly violation(s).",
            Violations: violations.Count > 0 ? violations : null,
            Remediation: passed ? null : "Review site data collection procedures and source logs for potential data entry manipulation or calibration defects."));
    }
}
