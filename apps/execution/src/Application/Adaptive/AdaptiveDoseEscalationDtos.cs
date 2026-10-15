using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Adaptive;

public record DoseLevelSummaryResponse(
    [property: JsonPropertyName("dose_level_index")] int DoseLevelIndex,
    [property: JsonPropertyName("dose_name")] string DoseName,
    [property: JsonPropertyName("dose_value")] double DoseValue,
    [property: JsonPropertyName("total_enrolled")] int TotalEnrolled,
    [property: JsonPropertyName("total_dlts")] int TotalDlts,
    [property: JsonPropertyName("observed_dlt_rate")] double ObservedDltRate,
    [property: JsonPropertyName("posterior_dlt_rate")] double? PosteriorDltRate,
    [property: JsonPropertyName("is_eliminated_for_safety")] bool IsEliminatedForSafety,
    [property: JsonPropertyName("overdose_probability")] double? OverdoseProbability
);

public record RarArmResponse(
    [property: JsonPropertyName("arm_id")] string ArmId,
    [property: JsonPropertyName("arm_name")] string ArmName,
    [property: JsonPropertyName("is_control")] bool IsControl,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("enrolled_count")] int EnrolledCount,
    [property: JsonPropertyName("response_count")] int ResponseCount,
    [property: JsonPropertyName("posterior_mean")] double PosteriorMean,
    [property: JsonPropertyName("allocation_probability")] double AllocationProbability,
    [property: JsonPropertyName("superiority_vs_control_prob")] double? SuperiorityVsControlProb
);

public record AdaptiveDoseEscalationStateResponse(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("design_type")] string DesignType,
    [property: JsonPropertyName("target_toxicity_rate")] double TargetToxicityRate,
    [property: JsonPropertyName("lambda_e")] double LambdaE,
    [property: JsonPropertyName("lambda_d")] double LambdaD,
    [property: JsonPropertyName("active_cohort_number")] int ActiveCohortNumber,
    [property: JsonPropertyName("current_dose_level_index")] int CurrentDoseLevelIndex,
    [property: JsonPropertyName("current_dose_name")] string CurrentDoseName,
    [property: JsonPropertyName("recommendation_action")] string RecommendationAction,
    [property: JsonPropertyName("recommended_dose_level_index")] int? RecommendedDoseLevelIndex,
    [property: JsonPropertyName("recommended_dose_name")] string? RecommendedDoseName,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("dose_summaries")] List<DoseLevelSummaryResponse> DoseSummaries,
    [property: JsonPropertyName("rar_arms")] List<RarArmResponse> RarArms
);

public record AdaptiveCohortTransitionRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("cohort_number")] int CohortNumber,
    [property: JsonPropertyName("decision_action")] string DecisionAction,
    [property: JsonPropertyName("target_dose_level_index")] int TargetDoseLevelIndex,
    [property: JsonPropertyName("override_reason")] string? OverrideReason = null,
    [property: JsonPropertyName("electronic_signature")] string? ElectronicSignature = null
);

public record AdaptiveCohortTransitionResponse(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("cohort_number")] int CohortNumber,
    [property: JsonPropertyName("previous_dose_level_index")] int PreviousDoseLevelIndex,
    [property: JsonPropertyName("new_dose_level_index")] int NewDoseLevelIndex,
    [property: JsonPropertyName("action_executed")] string ActionExecuted,
    [property: JsonPropertyName("is_override")] bool IsOverride,
    [property: JsonPropertyName("override_reason")] string? OverrideReason,
    [property: JsonPropertyName("authorized_by")] string AuthorizedBy,
    [property: JsonPropertyName("signature_timestamp")] DateTimeOffset SignatureTimestamp,
    [property: JsonPropertyName("audit_entry_id")] string AuditEntryId,
    [property: JsonPropertyName("updated_state")] AdaptiveDoseEscalationStateResponse UpdatedState
);
