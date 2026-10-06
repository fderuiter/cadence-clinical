using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// Request DTO for multi-subject batch PI electronic sign-off.
/// @req:PRD-SYS-002
/// @req:SRS-EDC-003
/// </summary>
public sealed record BatchSignOffRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("target_type")] string TargetType,
    [property: JsonPropertyName("target_ids")] IReadOnlyList<string> TargetIds,
    [property: JsonPropertyName("signing_reason")] string SigningReason
);
