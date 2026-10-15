using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// Request DTO for multi-subject batch PI electronic sign-off.
/// @req:SRS-EDC-003
/// </summary>
public sealed record BatchSignOffRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("target_type")] string TargetType,
    [property: JsonPropertyName("target_ids")] IReadOnlyList<string> TargetIds,
    [property: JsonPropertyName("signing_reason")] string SigningReason
)
{
    /// <summary>
    /// The batch an e-signature token must be bound to (its <c>batch_id</c> claim): the lower-case hex SHA-256 of
    /// <c>study:TYPE:ids:reason</c>, with the study, ids and reason trimmed, the type upper-cased and the ids sorted
    /// ordinally and comma-joined. The web client computes the same value before re-authenticating, so a token
    /// signs exactly the targets and meaning the signer confirmed.
    /// </summary>
    public string BatchBindingId()
    {
        string ids = string.Join(",", (TargetIds ?? []).Select(id => id.Trim()).OrderBy(id => id, StringComparer.Ordinal));
        string binding = $"{StudyId?.Trim()}:{TargetType?.Trim().ToUpperInvariant()}:{ids}:{SigningReason?.Trim()}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(binding)));
    }
}
