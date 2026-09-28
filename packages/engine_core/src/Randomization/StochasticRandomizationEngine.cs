using System;
using System.Collections.Generic;

namespace Cadence.Core.Domain.Randomization;

public static class StochasticRandomizationEngine
{
    public static SubjectAllocationResult AllocateBlock(
        StratifiedBlockConfig config,
        SubjectAllocationRequest request)
    {
        return BlockRandomizationEngine.Allocate(config, request);
    }

    public static SubjectAllocationResult AllocateMinimization(
        MinimizationConfig config,
        SubjectAllocationRequest request)
    {
        return MinimizationEngine.Allocate(config, request);
    }

    public static BOINDoseRecommendation EvaluateBoinDose(
        BOINConfig config,
        IReadOnlyList<DoseLevelDefinition> doseLevels,
        DoseCohortState currentState,
        IReadOnlyList<DoseCohortState>? allDoseStates = null)
    {
        return BoinEngine.EvaluateDoseLevel(config, doseLevels, currentState, allDoseStates);
    }

    public static BOINDoseRecommendation Evaluate3Plus3Dose(
        ThreePlusThreeConfig config,
        IReadOnlyList<DoseLevelDefinition> doseLevels,
        DoseCohortState currentState)
    {
        return BoinEngine.Evaluate3Plus3Cohort(config, doseLevels, currentState);
    }

    public static string ComputeStratumKey(
        Dictionary<string, object>? subjectFactors,
        List<string>? activeFactors)
    {
        return BlockRandomizationEngine.GenerateCanonicalStratumKey(subjectFactors, activeFactors);
    }

    public static (double LambdaE, double LambdaD) GetBoinBoundaries(double targetDltRate)
    {
        return BoinEngine.CalculateBoinBoundaries(targetDltRate);
    }

    public static double GetPosteriorToxicityProbability(
        int observedDlts,
        int evaluatedSubjects,
        double targetDltRate)
    {
        return BoinEngine.CalculatePosteriorProbToxic(observedDlts, evaluatedSubjects, targetDltRate);
    }
}
