using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Core.Domain.Randomization;

public static class MinimizationEngine
{
    public static Dictionary<string, Dictionary<string, int>> CalculateMarginalCounts(
        List<string> arms,
        List<string> activeFactors,
        Dictionary<string, object> subjectFactors,
        List<Dictionary<string, object>> historicalSubjects)
    {
        var marginalCounts = activeFactors.ToDictionary(
            f => f,
            f => arms.ToDictionary(a => a, a => 0)
        );

        foreach (var subj in historicalSubjects)
        {
            string? assignedArm = null;
            if (subj.TryGetValue("assigned_arm", out var arm1) && arm1 != null) assignedArm = arm1.ToString();
            else if (subj.TryGetValue("arm", out var arm2) && arm2 != null) assignedArm = arm2.ToString();

            if (assignedArm == null || !arms.Contains(assignedArm))
                continue;

            Dictionary<string, object>? factors = subj;
            if (subj.TryGetValue("stratification_factors", out var f1) && f1 is Dictionary<string, object> d1) factors = d1;
            else if (subj.TryGetValue("factors", out var f2) && f2 is Dictionary<string, object> d2) factors = d2;

            foreach (var factor in activeFactors)
            {
                if (subjectFactors.TryGetValue(factor, out var targetVal) && targetVal != null &&
                    factors != null && factors.TryGetValue(factor, out var subjVal) && subjVal != null &&
                    string.Equals(subjVal.ToString()?.Trim(), targetVal.ToString()?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    marginalCounts[factor][assignedArm]++;
                }
            }
        }

        return marginalCounts;
    }

    public static double ComputeDistance(List<double> counts, ImbalanceDistanceMetric metric)
    {
        if (counts == null || counts.Count == 0) return 0.0;

        if (metric == ImbalanceDistanceMetric.RANGE)
        {
            return counts.Max() - counts.Min();
        }

        double mean = counts.Average();
        double variance = counts.Sum(x => Math.Pow(x - mean, 2)) / counts.Count;

        if (metric == ImbalanceDistanceMetric.VARIANCE) return variance;
        if (metric == ImbalanceDistanceMetric.STANDARD_DEVIATION) return Math.Sqrt(variance);

        return counts.Max() - counts.Min();
    }

    public static Dictionary<string, double> EvaluateCandidateImbalances(
        MinimizationConfig config,
        Dictionary<string, object> subjectFactors,
        List<Dictionary<string, object>> historicalSubjects)
    {
        var arms = config.ArmsRatios.Keys.ToList();
        var activeFactors = config.StratificationFactors;
        var factorWeights = config.FactorWeights ?? new Dictionary<string, double>();

        var baselineCounts = CalculateMarginalCounts(arms, activeFactors, subjectFactors, historicalSubjects);
        var imbalanceScores = new Dictionary<string, double>();

        foreach (var candidateArm in arms)
        {
            double totalImbalance = 0.0;

            foreach (var factor in activeFactors)
            {
                double weight = factorWeights.TryGetValue(factor, out var w) ? w : 1.0;
                var hypotheticalCounts = new List<double>();

                foreach (var arm in arms)
                {
                    int cnt = baselineCounts[factor][arm];
                    if (arm == candidateArm) cnt++;
                    int ratio = config.ArmsRatios.TryGetValue(arm, out var r) ? r : 1;
                    hypotheticalCounts.Add((double)cnt / ratio);
                }

                double dist = ComputeDistance(hypotheticalCounts, config.DistanceMetric);
                totalImbalance += weight * dist;
            }

            imbalanceScores[candidateArm] = Math.Round(totalImbalance, 4);
        }

        return imbalanceScores;
    }

    public static SubjectAllocationResult Allocate(
        MinimizationConfig config,
        SubjectAllocationRequest request)
    {
        var activeFactors = config.StratificationFactors;
        var subjectFactors = request.StratificationFactors ?? new Dictionary<string, object>();

        foreach (var factor in activeFactors)
        {
            if (!subjectFactors.ContainsKey(factor))
            {
                throw new ArgumentException($"Missing required stratification factor value for: '{factor}'");
            }
        }

        var arms = config.ArmsRatios.Keys.ToList();
        var history = request.AllSubjectFactors ?? new List<Dictionary<string, object>>();

        var imbalanceScores = EvaluateCandidateImbalances(config, subjectFactors, history);

        double minScore = imbalanceScores.Values.Min();
        var preferredArms = imbalanceScores.Where(kv => Math.Abs(kv.Value - minScore) < 1e-5).Select(kv => kv.Key).ToList();
        var nonPreferredArms = arms.Except(preferredArms).ToList();

        string seedStr = $"{config.Seed ?? 101}:{request.StudyId}:{request.SubjectId}:{history.Count}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seedStr));
        int seedHash = BitConverter.ToInt32(hash, 0) & 0x7FFFFFFF;
        var rng = new Random(seedHash);

        string assignedArm;
        if (preferredArms.Count == arms.Count || nonPreferredArms.Count == 0)
        {
            assignedArm = preferredArms[rng.Next(preferredArms.Count)];
        }
        else
        {
            double coin = rng.NextDouble();
            if (coin < config.PPreferred)
            {
                assignedArm = preferredArms[rng.Next(preferredArms.Count)];
            }
            else
            {
                assignedArm = nonPreferredArms[rng.Next(nonPreferredArms.Count)];
            }
        }

        int allocationIndex = history.Count;
        var stratumParts = activeFactors.OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => $"{f}={subjectFactors[f]}");
        string stratumKey = string.Join("|", stratumParts);

        string checksumPayload = $"{request.StudyId}:{request.SubjectId}:{assignedArm}:{allocationIndex}:{imbalanceScores[assignedArm]}";
        byte[] csBytes = SHA256.HashData(Encoding.UTF8.GetBytes(checksumPayload));
        string checksum = Convert.ToHexString(csBytes).ToLowerInvariant();

        return new SubjectAllocationResult(
            StudyId: request.StudyId,
            SubjectId: request.SubjectId,
            AssignedArm: assignedArm,
            StratumKey: stratumKey,
            AlgorithmType: RandomizationAlgorithmType.MINIMIZATION,
            AllocationIndex: allocationIndex,
            BlockIndex: null,
            ImbalanceScore: imbalanceScores[assignedArm],
            DeterministicChecksum: checksum,
            DecisionRationale: $"Assigned to arm '{assignedArm}' via Pocock-Simon Minimization ({config.DistanceMetric}). " +
                               $"Imbalance scores: [{string.Join(", ", imbalanceScores.Select(kv => $"{kv.Key}:{kv.Value}"))}]. Preferred: [{string.Join(", ", preferredArms)}] (p={config.PPreferred})."
        );
    }
}
