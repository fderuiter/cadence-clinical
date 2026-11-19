using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Toxicity;

public record StagedAdverseEventReviewResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string? SiteId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("visit_id")] string? VisitId,
    [property: JsonPropertyName("observation_id")] string? ObservationId,
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("lab_value")] double LabValue,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("ctcae_grade")] int CtcaeGrade,
    [property: JsonPropertyName("grading_scale")] string GradingScale,
    [property: JsonPropertyName("toxicity_term")] string ToxicityTerm,
    [property: JsonPropertyName("clinical_description")] string ClinicalDescription,
    [property: JsonPropertyName("is_hys_law_warning")] bool IsHysLawWarning,
    [property: JsonPropertyName("is_delta_check_alert")] bool IsDeltaCheckAlert,
    [property: JsonPropertyName("delta_percentage")] double? DeltaPercentage,
    [property: JsonPropertyName("review_status")] string ReviewStatus,
    [property: JsonPropertyName("reviewed_by")] string? ReviewedBy,
    [property: JsonPropertyName("reviewed_at")] DateTimeOffset? ReviewedAt,
    [property: JsonPropertyName("investigator_notes")] string? InvestigatorNotes,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("is_deleted")] bool IsDeleted,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt
);

public record StagedAdverseEventActionRequest(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("investigator_notes")] string? InvestigatorNotes = null
);

public record StagedAdverseEventActionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("review_status")] string ReviewStatus,
    [property: JsonPropertyName("action_taken")] string ActionTaken,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp
);

public record LabToxicityEvaluateRequest(
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("ref_low")] double? RefLow = null,
    [property: JsonPropertyName("ref_high")] double? RefHigh = null,
    [property: JsonPropertyName("baseline_value")] double? BaselineValue = null,
    [property: JsonPropertyName("evaluated_at")] DateTimeOffset? EvaluatedAt = null
);

public record LabToxicityEvaluateResponse(
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("observed_value")] double ObservedValue,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("ctcae_grade")] int CtcaeGrade,
    [property: JsonPropertyName("grading_scale")] string GradingScale,
    [property: JsonPropertyName("toxicity_description")] string ToxicityDescription,
    [property: JsonPropertyName("is_critical_ae_trigger")] bool IsCriticalAeTrigger,
    [property: JsonPropertyName("is_hys_law_warning")] bool IsHysLawWarning,
    [property: JsonPropertyName("is_delta_check_alert")] bool IsDeltaCheckAlert,
    [property: JsonPropertyName("delta_percentage")] double? DeltaPercentage
);
