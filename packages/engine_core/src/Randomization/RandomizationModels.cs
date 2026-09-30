using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;

namespace Cadence.Core.Domain.Randomization;

public enum RandomizationAlgorithmType
{
    PERMUTED_BLOCK,
    STRATIFIED_BLOCK,
    MINIMIZATION,
    BOIN,
    THREE_PLUS_THREE,
    RESPONSE_ADAPTIVE
}

public enum ImbalanceDistanceMetric
{
    RANGE,
    VARIANCE,
    STANDARD_DEVIATION
}

public enum DoseEscalationAction
{
    ESCALATE,
    STAY,
    DEESCALATE,
    STOP_TRIAL,
    ELIMINATE_DOSE
}

public record StratifiedBlockConfig(
    [property: JsonPropertyName("arms_ratios")] Dictionary<string, int> ArmsRatios,
    [property: JsonPropertyName("stratification_factors")] List<string>? StratificationFactors = null,
    [property: JsonPropertyName("block_sizes")] List<int>? BlockSizes = null,
    [property: JsonPropertyName("seed")] int? Seed = null,
    [property: JsonPropertyName("algorithm_type")] RandomizationAlgorithmType AlgorithmType = RandomizationAlgorithmType.STRATIFIED_BLOCK
);

public record MinimizationConfig(
    [property: JsonPropertyName("arms_ratios")] Dictionary<string, int> ArmsRatios,
    [property: JsonPropertyName("stratification_factors")] List<string> StratificationFactors,
    [property: JsonPropertyName("factor_weights")] Dictionary<string, double>? FactorWeights = null,
    [property: JsonPropertyName("distance_metric")] ImbalanceDistanceMetric DistanceMetric = ImbalanceDistanceMetric.RANGE,
    [property: JsonPropertyName("p_preferred")] double PPreferred = 0.8,
    [property: JsonPropertyName("seed")] int? Seed = null,
    [property: JsonPropertyName("algorithm_type")] RandomizationAlgorithmType AlgorithmType = RandomizationAlgorithmType.MINIMIZATION
);

public record DoseLevelDefinition(
    [property: JsonPropertyName("dose_index")] int DoseIndex,
    [property: JsonPropertyName("dose_amount")] double DoseAmount,
    [property: JsonPropertyName("dose_unit")] string DoseUnit,
    [property: JsonPropertyName("formulation")] string? Formulation = null
);

[SuppressMessage("Naming", "S101:Types should be named in PascalCase",
    Justification = "BOIN (Bayesian Optimal Interval) is the dose-finding design's published name.")]
public record BOINConfig(
    [property: JsonPropertyName("target_dlt_rate")] double TargetDltRate = 0.25,
    [property: JsonPropertyName("escalation_boundary")] double? EscalationBoundary = null,
    [property: JsonPropertyName("deescalation_boundary")] double? DeescalationBoundary = null,
    [property: JsonPropertyName("stopping_cutoff")] double StoppingCutoff = 0.95,
    [property: JsonPropertyName("cohort_size")] int CohortSize = 3,
    [property: JsonPropertyName("max_dose_subjects")] int MaxDoseSubjects = 12,
    [property: JsonPropertyName("algorithm_type")] RandomizationAlgorithmType AlgorithmType = RandomizationAlgorithmType.BOIN
);

public record ThreePlusThreeConfig(
    [property: JsonPropertyName("cohort_size")] int CohortSize = 3,
    [property: JsonPropertyName("expansion_cohort_size")] int ExpansionCohortSize = 6,
    [property: JsonPropertyName("algorithm_type")] RandomizationAlgorithmType AlgorithmType = RandomizationAlgorithmType.THREE_PLUS_THREE
);

public record DoseCohortState(
    [property: JsonPropertyName("dose_index")] int DoseIndex,
    [property: JsonPropertyName("dose_amount")] double DoseAmount,
    [property: JsonPropertyName("dose_unit")] string DoseUnit,
    [property: JsonPropertyName("evaluated_subjects")] int EvaluatedSubjects = 0,
    [property: JsonPropertyName("observed_dlts")] int ObservedDlts = 0,
    [property: JsonPropertyName("is_eliminated")] bool IsEliminated = false
)
{
    [JsonPropertyName("empirical_dlt_rate")]
    public double EmpiricalDltRate => EvaluatedSubjects == 0 ? 0.0 : Math.Round((double)ObservedDlts / EvaluatedSubjects, 4);
}

[SuppressMessage("Naming", "S101:Types should be named in PascalCase",
    Justification = "BOIN (Bayesian Optimal Interval) is the dose-finding design's published name.")]
public record BOINDoseRecommendation(
    [property: JsonPropertyName("current_dose_index")] int CurrentDoseIndex,
    [property: JsonPropertyName("current_dose_amount")] double CurrentDoseAmount,
    [property: JsonPropertyName("current_dose_unit")] string CurrentDoseUnit,
    [property: JsonPropertyName("action")] DoseEscalationAction Action,
    [property: JsonPropertyName("empirical_dlt_rate")] double EmpiricalDltRate,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("recommended_dose_index")] int? RecommendedDoseIndex = null,
    [property: JsonPropertyName("recommended_dose_amount")] double? RecommendedDoseAmount = null,
    [property: JsonPropertyName("posterior_prob_toxic")] double? PosteriorProbToxic = null,
    [property: JsonPropertyName("is_trial_stopped")] bool IsTrialStopped = false
);

public record SubjectAllocationRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("stratification_factors")] Dictionary<string, object>? StratificationFactors = null,
    [property: JsonPropertyName("current_stratum_allocations")] List<string>? CurrentStratumAllocations = null,
    [property: JsonPropertyName("all_subject_factors")] List<Dictionary<string, object>>? AllSubjectFactors = null
);

public record SubjectAllocationResult(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("assigned_arm")] string AssignedArm,
    [property: JsonPropertyName("stratum_key")] string StratumKey,
    [property: JsonPropertyName("algorithm_type")] RandomizationAlgorithmType AlgorithmType,
    [property: JsonPropertyName("allocation_index")] int AllocationIndex,
    [property: JsonPropertyName("deterministic_checksum")] string DeterministicChecksum,
    [property: JsonPropertyName("decision_rationale")] string DecisionRationale,
    [property: JsonPropertyName("block_index")] int? BlockIndex = null,
    [property: JsonPropertyName("imbalance_score")] double? ImbalanceScore = null
);
