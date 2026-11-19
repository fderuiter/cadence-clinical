using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Core.Domain.Randomization;

public static class BlockRandomizationEngine
{
    public static string GenerateCanonicalStratumKey(
        Dictionary<string, object>? subjectFactors,
        List<string>? activeFactors)
    {
        if (activeFactors == null || activeFactors.Count == 0)
        {
            return "DEFAULT";
        }

        var sortedFactors = activeFactors.OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (subjectFactors == null)
        {
            throw new ArgumentException("Subject stratification factors are missing, but stratification is configured.");
        }

        var parts = new List<string>();
        foreach (var factor in sortedFactors)
        {
            if (!subjectFactors.TryGetValue(factor, out var valObj) || valObj == null)
            {
                throw new ArgumentException($"Missing required stratification factor value for: '{factor}'");
            }
            string val = valObj.ToString()?.Trim() ?? string.Empty;
            parts.Add($"{factor}={val}");
        }

        return string.Join("|", parts);
    }

    public static List<string> GeneratePermutedBlock(
        Dictionary<string, int> armsRatios,
        int blockSize,
        Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return GeneratePermutedBlock(armsRatios, blockSize, rng.Next);
    }

    /// <summary>
    /// One permuted block drawing its shuffle from <paramref name="nextBelow"/> (an integer in <c>[0, n)</c>), so a
    /// live randomisation list can be generated from a cryptographic source rather than a seeded <see cref="Random"/>
    /// whose seed could be recovered from a few known allocations (#5607).
    /// </summary>
    public static List<string> GeneratePermutedBlock(
        Dictionary<string, int> armsRatios,
        int blockSize,
        Func<int, int> nextBelow)
    {
        ArgumentNullException.ThrowIfNull(armsRatios);
        ArgumentNullException.ThrowIfNull(nextBelow);
        int ratioSum = armsRatios.Values.Sum();
        int multiplier = blockSize / ratioSum;

        var block = new List<string>();
        foreach (var (arm, ratio) in armsRatios)
        {
            int count = ratio * multiplier;
            for (int i = 0; i < count; i++)
            {
                block.Add(arm);
            }
        }

        // Fisher-Yates shuffle
        for (int i = block.Count - 1; i > 0; i--)
        {
            int j = nextBelow(i + 1);
            (block[i], block[j]) = (block[j], block[i]);
        }

        return block;
    }

    public static (List<string> FullSequence, List<(int BlockIndex, int BlockSize)> BlockMetadata) GenerateStratumBlockSequence(
        StratifiedBlockConfig config,
        string stratumKey,
        int targetCount)
    {
        string baseSeedStr = $"{config.Seed ?? 42}:{stratumKey}";
        var rng = RandomizationMath.CreateSeededRandom(baseSeedStr);

        int ratioSum = config.ArmsRatios.Values.Sum();
        var allowedBlockSizes = config.BlockSizes != null && config.BlockSizes.Count > 0
            ? config.BlockSizes
            : new List<int> { ratioSum };

        var fullAllocations = new List<string>();
        var blockMetadata = new List<(int, int)>();

        int blockIdx = 0;
        while (fullAllocations.Count < targetCount)
        {
            int chosenSize = allowedBlockSizes[rng.Next(allowedBlockSizes.Count)];
            var block = GeneratePermutedBlock(config.ArmsRatios, chosenSize, rng);
            fullAllocations.AddRange(block);
            blockMetadata.Add((blockIdx, chosenSize));
            blockIdx++;
        }

        return (fullAllocations, blockMetadata);
    }

    public static SubjectAllocationResult Allocate(
        StratifiedBlockConfig config,
        SubjectAllocationRequest request)
    {
        var activeFactors = config.StratificationFactors ?? new List<string>();
        string stratumKey = GenerateCanonicalStratumKey(request.StratificationFactors, activeFactors);

        var currentHistory = request.CurrentStratumAllocations ?? new List<string>();
        int nextAllocationIdx = currentHistory.Count;

        int neededCount = Math.Max(nextAllocationIdx + 1, 100);
        var (fullSequence, blockMetadata) = GenerateStratumBlockSequence(config, stratumKey, neededCount);

        string assignedArm = fullSequence[nextAllocationIdx];

        int cumulativeSlots = 0;
        int currentBlockIdx = 0;
        foreach (var (bIdx, bSize) in blockMetadata)
        {
            if (nextAllocationIdx >= cumulativeSlots && nextAllocationIdx < cumulativeSlots + bSize)
            {
                currentBlockIdx = bIdx;
                break;
            }
            cumulativeSlots += bSize;
        }

        string checksumPayload = $"{request.StudyId}:{request.SubjectId}:{stratumKey}:{nextAllocationIdx}:{currentBlockIdx}:{assignedArm}";
        string checksum = RandomizationMath.ComputeSha256Hex(checksumPayload);

        return new SubjectAllocationResult(
            StudyId: request.StudyId,
            SubjectId: request.SubjectId,
            AssignedArm: assignedArm,
            StratumKey: stratumKey,
            AlgorithmType: config.AlgorithmType,
            AllocationIndex: nextAllocationIdx,
            BlockIndex: currentBlockIdx,
            ImbalanceScore: null,
            DeterministicChecksum: checksum,
            DecisionRationale: $"Assigned to arm '{assignedArm}' via {config.AlgorithmType} in stratum '{stratumKey}' (block #{currentBlockIdx}, slot #{nextAllocationIdx})."
        );
    }
}
