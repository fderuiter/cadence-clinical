using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Cec;

/// <summary>
/// DTO representing the Chair tie-breaker adjudication request payload.
/// Matches schema Execution_ChairAdjudicateDTO.
/// @req:PRD-CEC-002
/// </summary>
public sealed record ChairAdjudicateRequest(
    [property: JsonPropertyName("final_classification")] string FinalClassification,
    [property: JsonPropertyName("final_subtype")] string? FinalSubtype,
    [property: JsonPropertyName("final_adjudicated_onset_date")] string FinalAdjudicatedOnsetDate,
    [property: JsonPropertyName("binding_reader")] string? BindingReader = "CHAIR_CUSTOM_DETERMINATION",
    [property: JsonPropertyName("criteria_confirmed")] List<string>? CriteriaConfirmed = null,
    [property: JsonPropertyName("adjudication_rationale")] string AdjudicationRationale = "",
    [property: JsonPropertyName("signature_token")] string? SignatureToken = null,
    [property: JsonPropertyName("signer_name")] string? SignerName = null,
    [property: JsonPropertyName("password")] string? Password = null,
    [property: JsonPropertyName("signature_meaning")] string? SignatureMeaning = null
);
