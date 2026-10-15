using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Cadence.Kernel;

namespace Cadence.Apps.Safety.Application;

public static class SafetySignalAlertStatus
{
    public const string Triggered = "TRIGGERED";
    public const string UnderReview = "UNDER_REVIEW";
    public const string ConfirmedSignal = "CONFIRMED_SIGNAL";
    public const string Refuted = "REFUTED";
    public const string Closed = "CLOSED";
}

public static class SafetySignalReviewDecision
{
    public const string ConfirmSignal = "CONFIRM_SIGNAL";
    public const string RefuteSignal = "REFUTE_SIGNAL";
    public const string RequestSafetyInquiry = "REQUEST_SAFETY_INQUIRY";
    public const string CloseAlert = "CLOSE_ALERT";
}

public class CaseReactionInput
{
    [JsonPropertyName("case_id")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("study_id")]
    public StudyId? StudyId { get; set; }

    [JsonPropertyName("drug_name")]
    public string DrugName { get; set; } = string.Empty;

    [JsonPropertyName("meddra_pt")]
    public string MeddraPt { get; set; } = string.Empty;

    [JsonPropertyName("meddra_soc")]
    public string? MeddraSoc { get; set; }

    [JsonPropertyName("is_blinded")]
    public bool IsBlinded { get; set; }
}

public class SignalDetectionScanRequest
{
    [JsonPropertyName("study_id")]
    public StudyId? StudyId { get; set; }

    [JsonPropertyName("target_drug")]
    public string TargetDrug { get; set; } = string.Empty;

    [JsonPropertyName("comparator_drugs")]
    public List<string> ComparatorDrugs { get; set; } = new();

    [JsonPropertyName("benchmark_dataset")]
    public string BenchmarkDataset { get; set; } = "INTERNAL_TRIAL_POOL";

    [JsonPropertyName("stratification_levels")]
    public List<string> StratificationLevels { get; set; } = new() { "PT", "SOC" };

    [JsonPropertyName("min_case_count")]
    public int MinCaseCount { get; set; } = 3;

    [JsonPropertyName("prr_threshold")]
    public double PrrThreshold { get; set; } = 2.0;

    [JsonPropertyName("chi_square_threshold")]
    public double ChiSquareThreshold { get; set; } = 4.0;

    [JsonPropertyName("ror_ci_lower_threshold")]
    public double RorCiLowerThreshold { get; set; } = 1.0;

    [JsonPropertyName("eb05_threshold")]
    public double Eb05Threshold { get; set; } = 2.0;

    [JsonPropertyName("auto_trigger_alerts")]
    public bool AutoTriggerAlerts { get; set; } = true;

    [JsonPropertyName("cases")]
    public List<CaseReactionInput>? Cases { get; set; }

    [JsonPropertyName("benchmark_cases")]
    public List<CaseReactionInput>? BenchmarkCases { get; set; }
}

public class DisproportionalityMetricResult
{
    [JsonPropertyName("drug_name")]
    public string DrugName { get; set; } = string.Empty;

    [JsonPropertyName("meddra_pt")]
    public string MeddraPt { get; set; } = string.Empty;

    [JsonPropertyName("meddra_soc")]
    public string? MeddraSoc { get; set; }

    [JsonPropertyName("term_level")]
    public string TermLevel { get; set; } = "PT";

    [JsonPropertyName("case_count_a")]
    public int CaseCountA { get; set; }

    [JsonPropertyName("case_count_b")]
    public int CaseCountB { get; set; }

    [JsonPropertyName("case_count_c")]
    public int CaseCountC { get; set; }

    [JsonPropertyName("case_count_d")]
    public int CaseCountD { get; set; }

    [JsonPropertyName("total_n")]
    public int TotalN { get; set; }

    [JsonPropertyName("expected_count_e")]
    public double ExpectedCountE { get; set; }

    [JsonPropertyName("prr")]
    public double Prr { get; set; }

    [JsonPropertyName("prr_ci_lower")]
    public double PrrCiLower { get; set; }

    [JsonPropertyName("prr_ci_upper")]
    public double PrrCiUpper { get; set; }

    [JsonPropertyName("chi_square_yates")]
    public double ChiSquareYates { get; set; }

    [JsonPropertyName("ror")]
    public double Ror { get; set; }

    [JsonPropertyName("ror_ci_lower")]
    public double RorCiLower { get; set; }

    [JsonPropertyName("ror_ci_upper")]
    public double RorCiUpper { get; set; }

    [JsonPropertyName("ebgm")]
    public double Ebgm { get; set; }

    [JsonPropertyName("eb05")]
    public double Eb05 { get; set; }

    [JsonPropertyName("eb95")]
    public double Eb95 { get; set; }

    [JsonPropertyName("is_prr_signal")]
    public bool IsPrrSignal { get; set; }

    [JsonPropertyName("is_ror_signal")]
    public bool IsRorSignal { get; set; }

    [JsonPropertyName("is_ebgm_signal")]
    public bool IsEbgmSignal { get; set; }

    [JsonPropertyName("overall_signal_flag")]
    public bool OverallSignalFlag { get; set; }

    [JsonPropertyName("signal_strength")]
    public string SignalStrength { get; set; } = "NONE";

    [JsonPropertyName("stratum")]
    public string? Stratum { get; set; }

    [JsonPropertyName("benchmark_dataset")]
    public string? BenchmarkDataset { get; set; }

    [JsonPropertyName("benchmark_relative_risk")]
    public double? BenchmarkRelativeRisk { get; set; }
}

public class SignalDetectionScanResponse
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("study_id")]
    public StudyId? StudyId { get; set; }

    [JsonPropertyName("target_drug")]
    public string TargetDrug { get; set; } = string.Empty;

    [JsonPropertyName("benchmark_dataset")]
    public string BenchmarkDataset { get; set; } = "INTERNAL_TRIAL_POOL";

    [JsonPropertyName("total_cases_analyzed")]
    public int TotalCasesAnalyzed { get; set; }

    [JsonPropertyName("total_terms_evaluated")]
    public int TotalTermsEvaluated { get; set; }

    [JsonPropertyName("signals_detected_count")]
    public int SignalsDetectedCount { get; set; }

    [JsonPropertyName("alerts_triggered_count")]
    public int AlertsTriggeredCount { get; set; }

    [JsonPropertyName("results")]
    public List<DisproportionalityMetricResult> Results { get; set; } = new();

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

public class SafetySignalAlertDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("detection_run_id")]
    public string? DetectionRunId { get; set; }

    [JsonPropertyName("study_id")]
    public StudyId? StudyId { get; set; }

    [JsonPropertyName("target_drug")]
    public string TargetDrug { get; set; } = string.Empty;

    [JsonPropertyName("meddra_term")]
    public string MeddraTerm { get; set; } = string.Empty;

    [JsonPropertyName("term_level")]
    public string TermLevel { get; set; } = "PT";

    [JsonPropertyName("meddra_soc")]
    public string? MeddraSoc { get; set; }

    [JsonPropertyName("case_count_a")]
    public int CaseCountA { get; set; }

    [JsonPropertyName("expected_count_e")]
    public double ExpectedCountE { get; set; }

    [JsonPropertyName("prr")]
    public double Prr { get; set; }

    [JsonPropertyName("prr_ci_lower")]
    public double PrrCiLower { get; set; }

    [JsonPropertyName("prr_ci_upper")]
    public double PrrCiUpper { get; set; }

    [JsonPropertyName("chi_square_yates")]
    public double ChiSquareYates { get; set; }

    [JsonPropertyName("ror")]
    public double Ror { get; set; }

    [JsonPropertyName("ror_ci_lower")]
    public double RorCiLower { get; set; }

    [JsonPropertyName("ror_ci_upper")]
    public double RorCiUpper { get; set; }

    [JsonPropertyName("ebgm")]
    public double Ebgm { get; set; }

    [JsonPropertyName("eb05")]
    public double Eb05 { get; set; }

    [JsonPropertyName("eb95")]
    public double Eb95 { get; set; }

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "HIGH";

    [JsonPropertyName("status")]
    public string Status { get; set; } = SafetySignalAlertStatus.Triggered;

    [JsonPropertyName("triage_summary")]
    public string TriageSummary { get; set; } = string.Empty;

    [JsonPropertyName("assigned_safety_officer")]
    public string? AssignedSafetyOfficer { get; set; }

    [JsonPropertyName("review_decision")]
    public string? ReviewDecision { get; set; }

    [JsonPropertyName("review_justification")]
    public string? ReviewJustification { get; set; }

    [JsonPropertyName("clinical_summary")]
    public string? ClinicalSummary { get; set; }

    [JsonPropertyName("reviewed_by")]
    public string? ReviewedBy { get; set; }

    [JsonPropertyName("reviewed_at")]
    public DateTimeOffset? ReviewedAt { get; set; }

    [JsonPropertyName("esignature_manifest_id")]
    public string? EsignatureManifestId { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("created_by")]
    public string CreatedBy { get; set; } = string.Empty;

    [JsonPropertyName("reason_for_change")]
    public string ReasonForChange { get; set; } = string.Empty;

    [JsonPropertyName("version_index")]
    public int VersionIndex { get; set; } = 1;
}

public class SafetySignalAlertReviewRequest
{
    [JsonPropertyName("decision")]
    public string Decision { get; set; } = string.Empty;

    [JsonPropertyName("justification")]
    public string Justification { get; set; } = string.Empty;

    [JsonPropertyName("clinical_summary")]
    public string? ClinicalSummary { get; set; }

    [JsonPropertyName("assigned_safety_officer")]
    public string? AssignedSafetyOfficer { get; set; }

    [JsonPropertyName("recommended_action")]
    public string? RecommendedAction { get; set; }

    [JsonPropertyName("esignature_manifest_id")]
    public string? EsignatureManifestId { get; set; }
}

public class SafetySignalAlertSignRequest
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("meaning_of_signature")]
    public string MeaningOfSignature { get; set; } = "Approval of Signal Review Assessment & Disposition";

    [JsonPropertyName("signature_secret")]
    public string SignatureSecret { get; set; } = string.Empty;

    [JsonPropertyName("decision")]
    public string Decision { get; set; } = string.Empty;

    [JsonPropertyName("justification")]
    public string Justification { get; set; } = string.Empty;

    [JsonPropertyName("clinical_summary")]
    public string? ClinicalSummary { get; set; }
}

public class SafetySignalAlertSignResponse
{
    [JsonPropertyName("alert_id")]
    public string AlertId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("reviewed_by")]
    public string ReviewedBy { get; set; } = string.Empty;

    [JsonPropertyName("reviewed_at")]
    public string ReviewedAt { get; set; } = string.Empty;

    [JsonPropertyName("esignature_manifest_id")]
    public string EsignatureManifestId { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
