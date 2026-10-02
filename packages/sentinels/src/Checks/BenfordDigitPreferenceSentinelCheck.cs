using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Sentinels.Checks;

public class BenfordDigitPreferenceSentinelCheck : IFraudSentinelCheck
{
    private static readonly double[] TheoreticalBenfordProbabilities =
    [
        0.30103, // d = 1
        0.17609, // d = 2
        0.12494, // d = 3
        0.09691, // d = 4
        0.07918, // d = 5
        0.06695, // d = 6
        0.05799, // d = 7
        0.05115, // d = 8
        0.04576  // d = 9
    ];

    public string Name => "BenfordDigitPreferenceCheck";
    public string Description => "Evaluates leading digit frequencies of numeric observations against Benford's Law distribution via Chi-Square goodness-of-fit.";

    public Task<SentinelResult> RunAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new SentinelResult(Name, Passed: true, Summary: Description));
    }

    public Task<SentinelResult> EvaluateAsync(FraudEvaluationContext context, CancellationToken ct = default)
    {
        var localDigitCounts = new long[10]; // index 1..9
        long localTotal = 0;

        foreach (var obs in context.Observations)
        {
            int leadingDigit = ExtractLeadingDigit(obs.Value);
            if (leadingDigit >= 1 && leadingDigit <= 9)
            {
                localDigitCounts[leadingDigit]++;
                localTotal++;
            }
        }

        if (localTotal == 0)
        {
            return Task.FromResult(new SentinelResult(
                Name,
                Passed: true,
                Summary: "No numeric observations available for Benford analysis."));
        }

        // Combine local counts with baseline global counts if available
        var combinedDigitCounts = new double[10];
        long totalObserved = 0;

        for (int d = 1; d <= 9; d++)
        {
            long baselineCount = 0;
            if (context.Baseline.GlobalDigitCounts != null &&
                context.Baseline.GlobalDigitCounts.TryGetValue(d, out long bc))
            {
                baselineCount = bc;
            }

            long combined = localDigitCounts[d] + baselineCount;
            combinedDigitCounts[d] = combined;
            totalObserved += combined;
        }

        if (totalObserved == 0)
        {
            return Task.FromResult(new SentinelResult(
                Name,
                Passed: true,
                Summary: "No observations found for digit preference evaluation."));
        }

        double chiSquare = 0.0;
        var violations = new List<string>();

        for (int d = 1; d <= 9; d++)
        {
            double expected = totalObserved * TheoreticalBenfordProbabilities[d - 1];
            double observed = combinedDigitCounts[d];
            if (expected > 0.0)
            {
                double diff = observed - expected;
                chiSquare += (diff * diff) / expected;
            }
        }

        // Chi-square critical value for 8 degrees of freedom at alpha = 0.05 is ~15.51
        const double Threshold = 15.51;
        bool passed = chiSquare <= Threshold;

        if (!passed)
        {
            violations.Add($"Benford's Law Chi-Square digit preference anomaly detected: Chi2={chiSquare:F2} (threshold={Threshold:F2}, sample_size={totalObserved}). Non-conforming digit distribution indicates potential data fabrication.");
        }

        return Task.FromResult(new SentinelResult(
            Name,
            Passed: passed,
            Summary: passed
                ? $"Benford's Law digit preference check passed (Chi2={chiSquare:F2}, N={totalObserved})."
                : $"Benford's Law digit preference check failed with Chi2={chiSquare:F2}.",
            Violations: violations.Count > 0 ? violations : null,
            Remediation: passed ? null : "Review site source documents and raw eCRF observations for potential digit manipulation or synthetic data entry."));
    }

    public static int ExtractLeadingDigit(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 1e-12 || double.IsNaN(abs) || double.IsInfinity(abs))
        {
            return 0;
        }

        string s = abs.ToString("G17", CultureInfo.InvariantCulture);
        foreach (char c in s)
        {
            if (c >= '1' && c <= '9')
            {
                return c - '0';
            }
        }

        return 0;
    }
}
