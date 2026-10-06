using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Core.Domain.Randomization;

public static class ResponseAdaptiveEngine
{
    public static SubjectAllocationResult Allocate(
        ResponseAdaptiveConfig config,
        IReadOnlyList<ResponseArmState> armStates,
        SubjectAllocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(armStates);
        ArgumentNullException.ThrowIfNull(request);

        if (armStates.Count == 0)
        {
            throw new ArgumentException("At least one treatment arm state must be provided.", nameof(armStates));
        }

        string stratumKey = BlockRandomizationEngine.GenerateCanonicalStratumKey(
            request.StratificationFactors,
            request.StratificationFactors?.Keys.ToList()
        );

        int allocationIndex = request.CurrentStratumAllocations?.Count
            ?? request.AllSubjectFactors?.Count
            ?? 0;

        int totalEvaluatedSubjects = armStates.Sum(a => a.EvaluatedSubjects);
        bool isBurnInPhase = totalEvaluatedSubjects < config.BurnInThreshold;

        // 1. Beta-Binomial Posterior Updates: Beta(alpha_0 + y_k, beta_0 + n_k - y_k)
        var posteriorParams = armStates.ToDictionary(
            a => a.ArmId,
            a => (
                Alpha: Math.Max(1e-6, config.PriorAlpha + a.ObservedResponses),
                Beta: Math.Max(1e-6, config.PriorBeta + a.EvaluatedSubjects - a.ObservedResponses)
            )
        );

        // 2. Compute Superiority Probabilities V_k
        var posteriorSuperiority = CalculateSuperiorityProbabilities(config, armStates, posteriorParams);

        // 3. Evaluate Futility Dropping and Superiority Graduation
        var updatedArmStatuses = EvaluateArmStatuses(config, armStates, posteriorSuperiority, isBurnInPhase);

        // 4. Determine Active Allocation Candidate Pool
        var activeArmIds = updatedArmStatuses
            .Where(kv => !kv.Value.IsDropped)
            .Select(kv => kv.Key)
            .ToList();

        if (activeArmIds.Count == 0)
        {
            // Fallback: if all arms were marked dropped, allocate among all arms to prevent deadlock
            activeArmIds = armStates.Select(a => a.ArmId).ToList();
        }

        // 5. Compute Allocation Probabilities (Burn-In vs Adaptive Power Tuning)
        Dictionary<string, double> allocationProbabilities;

        if (isBurnInPhase)
        {
            double equalProb = 1.0 / activeArmIds.Count;
            allocationProbabilities = activeArmIds.ToDictionary(id => id, _ => equalProb);
        }
        else
        {
            allocationProbabilities = ComputeAdaptiveProbabilities(
                config,
                activeArmIds,
                posteriorSuperiority
            );
        }

        // 6. Deterministic Pseudo-Random Allocation Selection (21 CFR Part 11 SHA-256 Seed)
        string seedStr = $"{config.Seed ?? 2026}:{request.StudyId}:{request.SubjectId}:{stratumKey}:{allocationIndex}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seedStr));
        int seedHash = BitConverter.ToInt32(hash, 0) & 0x7FFFFFFF;
        var rng = new Random(seedHash);
        double coin = rng.NextDouble();

        string assignedArm = SelectArmByProbability(activeArmIds, allocationProbabilities, coin);

        // 7. Deterministic Checksum Calculation
        string checksumPayload = $"{request.StudyId}:{request.SubjectId}:{stratumKey}:{allocationIndex}:{assignedArm}:{allocationProbabilities[assignedArm]:F6}";
        byte[] csBytes = SHA256.HashData(Encoding.UTF8.GetBytes(checksumPayload));
        string checksum = Convert.ToHexString(csBytes).ToLowerInvariant();

        // 8. Decision Rationale Construction
        string phaseName = isBurnInPhase
            ? $"Burn-In Phase ({totalEvaluatedSubjects}/{config.BurnInThreshold} subjects evaluated)"
            : $"Adaptive Phase (Power c={config.PowerTuning})";

        var armDetails = armStates.Select(a =>
        {
            double v = posteriorSuperiority[a.ArmId];
            double p = allocationProbabilities.GetValueOrDefault(a.ArmId, 0.0);
            var (alpha, beta) = posteriorParams[a.ArmId];
            var (dropped, graduated) = updatedArmStatuses[a.ArmId];
            string status = "ACTIVE";
            if (dropped)
            {
                status = "DROPPED";
            }
            else if (graduated)
            {
                status = "GRADUATED";
            }
            return $"{a.ArmId}: n={a.EvaluatedSubjects}, y={a.ObservedResponses}, Beta({alpha:F1},{beta:F1}), V={v:F4}, p={p:F4} [{status}]";
        });

        string rationale = $"Assigned to arm '{assignedArm}' via Bayesian Response-Adaptive Randomization under {phaseName}. " +
                          $"Arm Metrics: [{string.Join("; ", armDetails)}].";

        return new SubjectAllocationResult(
            StudyId: request.StudyId,
            SubjectId: request.SubjectId,
            AssignedArm: assignedArm,
            StratumKey: stratumKey,
            AlgorithmType: RandomizationAlgorithmType.RESPONSE_ADAPTIVE,
            AllocationIndex: allocationIndex,
            BlockIndex: null,
            ImbalanceScore: Math.Round(posteriorSuperiority[assignedArm], 4),
            DeterministicChecksum: checksum,
            DecisionRationale: rationale
        );
    }

    public static Dictionary<string, double> CalculateSuperiorityProbabilities(
        ResponseAdaptiveConfig config,
        IReadOnlyList<ResponseArmState> armStates,
        Dictionary<string, (double Alpha, double Beta)> posteriorParams)
    {
        var result = new Dictionary<string, double>();
        bool hasControl = !string.IsNullOrEmpty(config.ControlArm) &&
                          armStates.Any(a => a.ArmId == config.ControlArm);

        string? controlArmId = hasControl ? config.ControlArm : armStates.FirstOrDefault(a => a.IsControl)?.ArmId;

        if (!string.IsNullOrEmpty(controlArmId) && posteriorParams.TryGetValue(controlArmId, out var ctrlParams))
        {
            double ctrlAlpha = ctrlParams.Alpha;
            double ctrlBeta = ctrlParams.Beta;

            var otherArmIds = armStates.Where(a => a.ArmId != controlArmId).Select(a => a.ArmId).ToList();

            foreach (string armId in armStates.Select(a => a.ArmId))
            {
                if (armId == controlArmId)
                {
                    // For Control Arm: compute probability of being the best arm among all
                    double pBest = IntegrateSimpson(x =>
                    {
                        double pdf = BetaPdf(ctrlAlpha, ctrlBeta, x);
                        double prod = 1.0;
                        foreach (var oId in otherArmIds)
                        {
                            var (oAlpha, oBeta) = posteriorParams[oId];
                            prod *= FastBetaCdf(oAlpha, oBeta, x);
                        }
                        return pdf * prod;
                    });
                    result[armId] = Math.Round(pBest, 6);
                }
                else
                {
                    // For Treatment Arm vs Control: Pr(theta_k > theta_control)
                    var (tAlpha, tBeta) = posteriorParams[armId];
                    double pSup = IntegrateSimpson(x =>
                    {
                        double pdf = BetaPdf(tAlpha, tBeta, x);
                        double cdfCtrl = FastBetaCdf(ctrlAlpha, ctrlBeta, x);
                        return pdf * cdfCtrl;
                    });
                    result[armId] = Math.Round(pSup, 6);
                }
            }
        }
        else
        {
            // Multi-arm superiority without explicit control: Pr(theta_k = max_j theta_j)
            foreach (string armId in armStates.Select(a => a.ArmId))
            {
                var (alpha, beta) = posteriorParams[armId];
                var otherArmIds = armStates.Where(a => a.ArmId != armId).Select(a => a.ArmId).ToList();

                double pMax = IntegrateSimpson(x =>
                {
                    double pdf = BetaPdf(alpha, beta, x);
                    double prod = 1.0;
                    foreach (var oId in otherArmIds)
                    {
                        var (oAlpha, oBeta) = posteriorParams[oId];
                        prod *= FastBetaCdf(oAlpha, oBeta, x);
                    }
                    return pdf * prod;
                });
                result[armId] = Math.Round(pMax, 6);
            }
        }

        return result;
    }

    private static Dictionary<string, (bool IsDropped, bool IsGraduated)> EvaluateArmStatuses(
        ResponseAdaptiveConfig config,
        IReadOnlyList<ResponseArmState> armStates,
        Dictionary<string, double> posteriorSuperiority,
        bool isBurnInPhase)
    {
        var result = new Dictionary<string, (bool IsDropped, bool IsGraduated)>();

        foreach (var arm in armStates)
        {
            double v = posteriorSuperiority[arm.ArmId];
            bool dropped = arm.IsDropped;
            bool graduated = arm.IsGraduated;

            if (!isBurnInPhase)
            {
                bool isControl = arm.IsControl || arm.ArmId == config.ControlArm;

                // Non-control arms are dropped if superiority falls below futility cutoff
                if (!isControl && !dropped && v < config.FutilityCutoff)
                {
                    dropped = true;
                }

                // Arm marks graduated if superiority meets/exceeds superiority cutoff
                if (!graduated && v >= config.SuperiorityCutoff)
                {
                    graduated = true;
                }
            }

            result[arm.ArmId] = (dropped, graduated);
        }

        return result;
    }

    private static Dictionary<string, double> ComputeAdaptiveProbabilities(
        ResponseAdaptiveConfig config,
        List<string> activeArmIds,
        Dictionary<string, double> posteriorSuperiority)
    {
        int count = activeArmIds.Count;
        if (count == 0) return new Dictionary<string, double>();

        double pMinEff = Math.Min(config.MinAllocationCap, 1.0 / count);
        double pMaxEff = Math.Max(config.MaxAllocationCap, 1.0 / count);

        double c = config.PowerTuning;
        var rawWeights = activeArmIds.ToDictionary(
            id => id,
            id => Math.Pow(Math.Max(0.0, posteriorSuperiority.GetValueOrDefault(id, 0.0)), c)
        );

        double weightSum = rawWeights.Values.Sum();
        var p = new double[count];

        for (int i = 0; i < count; i++)
        {
            string armId = activeArmIds[i];
            p[i] = weightSum > 0 ? rawWeights[armId] / weightSum : 1.0 / count;
        }

        // Iterative Single-Most-Violated Capping Algorithm
        bool[] clamped = new bool[count];
        for (int iter = 0; iter < count; iter++)
        {
            int maxViolatedIdx = -1;
            double maxViolation = 1e-9;
            double targetVal = 0.0;

            for (int i = 0; i < count; i++)
            {
                if (clamped[i]) continue;

                if (p[i] < pMinEff && (pMinEff - p[i]) > maxViolation)
                {
                    maxViolation = pMinEff - p[i];
                    maxViolatedIdx = i;
                    targetVal = pMinEff;
                }
                else if (p[i] > pMaxEff && (p[i] - pMaxEff) > maxViolation)
                {
                    maxViolation = p[i] - pMaxEff;
                    maxViolatedIdx = i;
                    targetVal = pMaxEff;
                }
            }

            if (maxViolatedIdx == -1) break;

            p[maxViolatedIdx] = targetVal;
            clamped[maxViolatedIdx] = true;

            double unclampedSum = 0.0;
            for (int i = 0; i < count; i++)
            {
                if (!clamped[i]) unclampedSum += p[i];
            }

            double remainingMass = 1.0;
            for (int i = 0; i < count; i++)
            {
                if (clamped[i]) remainingMass -= p[i];
            }

            if (unclampedSum < 1e-9)
            {
                int remainingCount = count - clamped.Count(b => b);
                if (remainingCount > 0 && remainingMass > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (!clamped[i]) p[i] = remainingMass / remainingCount;
                    }
                }
                break;
            }

            for (int i = 0; i < count; i++)
            {
                if (!clamped[i])
                {
                    p[i] = remainingMass * (p[i] / unclampedSum);
                }
            }
        }

        double finalSum = p.Sum();
        var result = new Dictionary<string, double>();
        for (int i = 0; i < count; i++)
        {
            result[activeArmIds[i]] = Math.Round(finalSum > 0 ? p[i] / finalSum : 1.0 / count, 6);
        }

        return result;
    }

    private static string SelectArmByProbability(
        List<string> activeArmIds,
        Dictionary<string, double> probabilities,
        double coin)
    {
        double cumulative = 0.0;
        foreach (var armId in activeArmIds)
        {
            cumulative += probabilities.GetValueOrDefault(armId, 0.0);
            if (coin < cumulative)
            {
                return armId;
            }
        }
        return activeArmIds[activeArmIds.Count - 1];
    }

    public static double FastBetaCdf(double a, double b, double x)
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
            return Math.Clamp(total, 0.0, 1.0);
        }

        return BoinEngine.BetaInc(a, b, x);
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

    public static double BetaPdf(double a, double b, double x)
    {
        if (x <= 0.0 || x >= 1.0) return 0.0;
        double lbeta = LogGamma(a) + LogGamma(b) - LogGamma(a + b);
        double logVal = (a - 1.0) * Math.Log(x) + (b - 1.0) * Math.Log(1.0 - x) - lbeta;
        return Math.Exp(logVal);
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

    private static double IntegrateSimpson(Func<double, double> func, int steps = 40)
    {
        if (steps % 2 != 0) steps++;
        double h = 1.0 / steps;
        double totalSimp = 0.0;
        for (int i = 0; i <= steps; i++)
        {
            double x = i * h;
            double val = func(x);
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
        return Math.Max(0.0, Math.Min(1.0, res));
    }
}
