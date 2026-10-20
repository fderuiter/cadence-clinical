using System.Collections.Generic;
using System.Text.Json.Serialization;

using Cadence.Apps.Execution.Domain.Cec;

namespace Cadence.Apps.Execution.Application.Cec;

/// <summary>
/// DTO representing the Chair tie-breaker adjudication response payload.
/// Matches schema Execution_ChairAdjudicationDetermination.
/// @req:PRD-CEC-002
/// </summary>
public sealed record ChairAdjudicationResponse(
    [property: JsonPropertyName("determination_id")] string DeterminationId,
    [property: JsonPropertyName("dossier_id")] string DossierId,
    [property: JsonPropertyName("chair_id")] string ChairId,
    [property: JsonPropertyName("chair_name")] string ChairName,
    [property: JsonPropertyName("final_classification")] string FinalClassification,
    [property: JsonPropertyName("final_subtype")] string? FinalSubtype,
    [property: JsonPropertyName("final_adjudicated_onset_date")] string FinalAdjudicatedOnsetDate,
    [property: JsonPropertyName("binding_reader")] string BindingReader,
    [property: JsonPropertyName("criteria_confirmed")] List<string> CriteriaConfirmed,
    [property: JsonPropertyName("adjudication_rationale")] string AdjudicationRationale,
    [property: JsonPropertyName("electronic_signature")] SignatureManifest ElectronicSignature,
    [property: JsonPropertyName("finalized_at")] string FinalizedAt
);
