using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cadence.Core.Domain.Toxicity;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "Member names are the serialized enum values shared with the API and the Python reference; renaming them would change the wire format.")]
public enum CTCAEGrade
{
    GRADE_0 = 0,
    GRADE_1 = 1,
    GRADE_2 = 2,
    GRADE_3 = 3,
    GRADE_4 = 4,
    GRADE_5 = 5
}

public enum ToxicityCategory
{
    HEPATIC,
    RENAL,
    HEMATOLOGY,
    ELECTROLYTES,
    METABOLIC,
    COAGULATION,
    GENERAL
}

public record GradeCutoff(
    [property: JsonPropertyName("grade")] CTCAEGrade Grade,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("criteria_type")] string CriteriaType = "ULN_MULTIPLIER",
    [property: JsonPropertyName("min_multiplier")] double? MinMultiplier = null,
    [property: JsonPropertyName("max_multiplier")] double? MaxMultiplier = null,
    [property: JsonPropertyName("min_value")] double? MinValue = null,
    [property: JsonPropertyName("max_value")] double? MaxValue = null
);

public class ToxicityRuleDefinition
{
    [JsonPropertyName("test_code")] public string TestCode { get; set; } = string.Empty;
    [JsonPropertyName("test_name")] public string TestName { get; set; } = string.Empty;
    [JsonPropertyName("category")] public ToxicityCategory Category { get; set; } = ToxicityCategory.GENERAL;
    [JsonPropertyName("scale")] public string Scale { get; set; } = "CTCAE_V5";
    [JsonPropertyName("grades")] public List<GradeCutoff> Grades { get; set; } = new();
    [JsonPropertyName("meddra_term")] public string? MeddraTerm { get; set; }
    [JsonPropertyName("version_index")] public int VersionIndex { get; set; } = 1;
    [JsonPropertyName("is_active")] public bool IsActive { get; set; } = true;
    [JsonPropertyName("reason_for_change")] public string? ReasonForChange { get; set; }
    [JsonPropertyName("sha256_checksum")] public string? Sha256Checksum { get; set; }

    public void ComputeChecksum()
    {
        if (string.IsNullOrEmpty(Sha256Checksum))
        {
            var payload = new JsonObject
            {
                ["test_code"] = TestCode,
                ["scale"] = Scale,
                ["version_index"] = VersionIndex,
                ["grades"] = new JsonArray(Grades.Select(g => (JsonNode)new JsonObject
                {
                    ["grade"] = g.Grade.ToString(),
                    ["min_multiplier"] = g.MinMultiplier,
                    ["max_multiplier"] = g.MaxMultiplier,
                    ["min_value"] = g.MinValue,
                    ["max_value"] = g.MaxValue
                }).ToArray())
            };
            string raw = payload.ToJsonString();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            Sha256Checksum = Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

public record AdverseEventDraftPayload(
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("observed_value")] double ObservedValue,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("grade")] CTCAEGrade Grade,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("meddra_term")] string? MeddraTerm = null,
    [property: JsonPropertyName("is_serious")] bool IsSerious = true,
    [property: JsonPropertyName("hys_law_indicated")] bool HysLawIndicated = false,
    [property: JsonPropertyName("draft_timestamp")] string DraftTimestamp = "",
    [property: JsonPropertyName("requires_physician_signoff")] bool RequiresPhysicianSignoff = true
);

public record ToxicityAssessmentResult(
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("observed_value")] double ObservedValue,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("ref_low")] double? RefLow = null,
    [property: JsonPropertyName("ref_high")] double? RefHigh = null,
    [property: JsonPropertyName("baseline_value")] double? BaselineValue = null,
    [property: JsonPropertyName("grade")] CTCAEGrade Grade = CTCAEGrade.GRADE_0,
    [property: JsonPropertyName("scale")] string Scale = "CTCAE_V5",
    [property: JsonPropertyName("is_critical_ae_trigger")] bool IsCriticalAeTrigger = false,
    [property: JsonPropertyName("is_hys_law_warning")] bool IsHysLawWarning = false,
    [property: JsonPropertyName("is_delta_check_alert")] bool IsDeltaCheckAlert = false,
    [property: JsonPropertyName("delta_percentage")] double? DeltaPercentage = null,
    [property: JsonPropertyName("ae_draft")] AdverseEventDraftPayload? AeDraft = null,
    [property: JsonPropertyName("eval_timestamp")] string EvalTimestamp = ""
);

public record HysLawEvaluation(
    [property: JsonPropertyName("alt_ast_ratio_uln")] double AltAstRatioUln,
    [property: JsonPropertyName("tbil_ratio_uln")] double TbilRatioUln,
    [property: JsonPropertyName("alkp_ratio_uln")] double AlkpRatioUln,
    [property: JsonPropertyName("alert_level")] string AlertLevel,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("is_hys_law_met")] bool IsHysLawMet = false,
    [property: JsonPropertyName("eval_timestamp")] string EvalTimestamp = ""
);
