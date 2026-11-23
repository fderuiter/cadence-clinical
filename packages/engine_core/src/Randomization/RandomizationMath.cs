using System;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Core.Domain.Randomization;

internal static class RandomizationMath
{
    private static readonly double[] LanczosCoefficients =
    [
        676.5203681218851,
        -1259.139216722289,
        771.32342877765313,
        -176.61502916214059,
        12.507343278686905,
        -0.13857109526572012,
        9.9843695780195716e-6,
        1.5056327351493116e-7
    ];

    internal static bool HasIntegerShapeParameters(double a, double b) =>
        Math.Abs(a % 1) < 1e-9 && Math.Abs(b % 1) < 1e-9 && a >= 1 && b >= 1;

    internal static double RegularizedIncompleteBeta(double a, double b, double x)
    {
        if (x <= 0.0) return 0.0;
        if (x >= 1.0) return 1.0;

        if (HasIntegerShapeParameters(a, b))
        {
            int ia = (int)a;
            int ib = (int)b;
            int n = ia + ib - 1;

            double logTerm = LogGamma(n + 1.0) - LogGamma(ia + 1.0) - LogGamma(ib)
                           + ia * Math.Log(x) + (ib - 1.0) * Math.Log(1.0 - x);
            double currentTerm = Math.Exp(logTerm);
            double total = currentTerm;

            double ratioFactor = x / (1.0 - x);
            for (int k = ia; k < n; k++)
            {
                currentTerm *= ((double)(n - k) / (k + 1)) * ratioFactor;
                if (currentTerm <= 0.0)
                {
                    break;
                }
                total += currentTerm;
            }
            return Math.Clamp(total, 0.0, 1.0);
        }

        double logBeta = LogGamma(a) + LogGamma(b) - LogGamma(a + b);
        return IntegrateSimpson(t => BetaPdfWithLogBeta(a, b, t, logBeta), upperBound: x, steps: 1000);
    }

    internal static double BetaPdf(double a, double b, double x)
    {
        if (x <= 0.0 || x >= 1.0) return 0.0;
        double logBeta = LogGamma(a) + LogGamma(b) - LogGamma(a + b);
        return BetaPdfWithLogBeta(a, b, x, logBeta);
    }

    internal static double LogGamma(double x)
    {
        if (x < 0.5) return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1 - x);
        x -= 1;
        double a = 0.99999999999980993;
        double t = x + 7.5;
        for (int i = 0; i < LanczosCoefficients.Length; i++)
        {
            a += LanczosCoefficients[i] / (x + i + 1);
        }
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    internal static double IntegrateSimpson(Func<double, double> func, double upperBound = 1.0, int steps = 40)
    {
        if (steps % 2 != 0) steps++;
        double h = upperBound / steps;
        double totalSimp = 0.0;
        for (int i = 0; i <= steps; i++)
        {
            double t = i * h;
            double val = func(t);
            double weight;
            if (i == 0 || i == steps)
            {
                weight = 1.0;
            }
            else if (i % 2 == 1)
            {
                weight = 4.0;
            }
            else
            {
                weight = 2.0;
            }
            totalSimp += weight * val;
        }
        double res = (h / 3.0) * totalSimp;
        return Math.Clamp(res, 0.0, 1.0);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "SonarAnalyzer.CSharp",
        "S2245:Using pseudorandom number generators (PRNGs) is security-sensitive",
        Justification = "Deterministic replayable clinical randomization sequence seeded from SHA-256 of the randomization payload.")]
    internal static Random CreateSeededRandom(string seedPayload)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seedPayload));
        int seedHash = BitConverter.ToInt32(hash, 0) & 0x7FFFFFFF;
        return new Random(seedHash);
    }

    internal static string ComputeSha256Hex(string payload)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }

    private static double BetaPdfWithLogBeta(double a, double b, double x, double logBeta)
    {
        if (x <= 0.0 || x >= 1.0) return 0.0;
        double logVal = (a - 1.0) * Math.Log(x) + (b - 1.0) * Math.Log(1.0 - x) - logBeta;
        return Math.Exp(logVal);
    }
}
