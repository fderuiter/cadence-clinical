using System;
using System.Collections.Generic;

namespace Cadence.Core.Synthetic;

public record EligibilityCriterion(
    string Id,
    string Name,
    string Category, // "INCLUSION" or "EXCLUSION"
    string VariableName,
    string Operator, // ">=", "<=", "==", "!=", "CONTAINS", "BETWEEN"
    string Value,
    string? HighValue = null,
    string? UsdmElementId = null);

public record EligibilityExpressionRequest(
    string ProtocolId,
    List<EligibilityCriterion> Criteria,
    string? UsdmVersionTag = null);

public record CriterionValidationResult(
    bool IsValid,
    List<string> Errors,
    List<string> Warnings);

public record PatientBaselineFeature(
    double Age,
    string Sex, // "M", "F", "U"
    double Bmi,
    double SystolicBp,
    double DiastolicBp,
    double BiomarkerValue,
    Dictionary<string, bool>? Comorbidities = null);

public record SyntheticPatientRecord(
    string PatientId,
    double Age,
    string Sex,
    double Bmi,
    double SystolicBp,
    double DiastolicBp,
    double BiomarkerValue,
    double PropensityScore,
    bool IsMatched,
    string? MatchedControlId = null,
    Dictionary<string, string>? AdditionalAttributes = null);

public record CovariateBalanceMetric(
    string CovariateName,
    double PreMatchMeanTreatment,
    double PreMatchMeanControl,
    double PreMatchSmd, // Standardized Mean Difference
    double PostMatchMeanTreatment,
    double PostMatchMeanControl,
    double PostMatchSmd,
    bool IsBalanced); // True if PostMatchSmd < 0.10

public record PropensityMatchingRequest(
    string StudyId,
    List<EligibilityCriterion> EligibilityCriteria,
    int TargetSampleSize = 500,
    double Caliper = 0.25,
    string MatchingRatio = "1:1",
    string? RweCohortProfileId = null);

public record PropensityMatchingResult(
    string SimulationId,
    string StudyId,
    int TotalCandidateRecordsEvaluated,
    int MatchedTreatmentCount,
    int MatchedControlCount,
    double MatchRatePercentage,
    double MeanPropensityScoreTreatment,
    double MeanPropensityScoreControl,
    List<CovariateBalanceMetric> CovariateBalance,
    List<SyntheticPatientRecord> MatchedSyntheticArm,
    long LatencyMs);

public record WaterfallStep(
    int StepNumber,
    string CriterionId,
    string CriterionName,
    string Category,
    int RemainingCount,
    int ExcludedCount,
    double RetentionPercentage,
    double AttritionPercentage);

public record AttritionWaterfallResult(
    string ProtocolId,
    int InitialPopulationCount,
    int FinalEligibleCount,
    double OverallFeasibilityYieldPercentage,
    List<WaterfallStep> Steps,
    long LatencyMs);

public record CohortFeasibilitySummary(
    string SimulationId,
    string ProtocolId,
    int EvaluatedCohortSize,
    int EligibleCohortSize,
    double OverallYieldPercentage,
    AttritionWaterfallResult Waterfall,
    PropensityMatchingResult? SyntheticControlArm,
    List<string> SentinelValidationErrors,
    DateTimeOffset GeneratedAt);
