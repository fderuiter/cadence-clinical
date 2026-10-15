using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// Represents an itemized queued delta in an offline batch synchronization request.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public sealed record OfflineDeltaItem(
    [property: JsonPropertyName("delta_id")] string DeltaId,
    [property: JsonPropertyName("entity_type")] string EntityType,
    [property: JsonPropertyName("entity_id")] string EntityId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("client_timestamp_utc")] string ClientTimestampUtc,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("sig_token")] string? SigToken = null
)
{
    /// <summary>
    /// Extracts the e-signature token from top-level property or payload dictionary.
    /// </summary>
    public string? ResolveSigToken()
    {
        if (!string.IsNullOrWhiteSpace(SigToken))
        {
            return SigToken;
        }

        if (Payload.ValueKind == JsonValueKind.Object)
        {
            if (Payload.TryGetProperty("sig_token", out var tok1) && tok1.ValueKind == JsonValueKind.String)
            {
                return tok1.GetString();
            }
            if (Payload.TryGetProperty("sigToken", out var tok2) && tok2.ValueKind == JsonValueKind.String)
            {
                return tok2.GetString();
            }
            if (Payload.TryGetProperty("signature_token", out var tok3) && tok3.ValueKind == JsonValueKind.String)
            {
                return tok3.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether this delta represents a signature-gated action requiring e-signature re-authentication.
    /// </summary>
    public bool IsSignatureGated()
    {
        string act = Action?.Trim().ToUpperInvariant() ?? string.Empty;
        if (act is "APPROVE" or "SIGN" or "BATCH_SIGN_OFF" or "ELECTRONIC_SIGNATURE")
        {
            return true;
        }

        string entity = EntityType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (entity is "formsubmission" or "form" && act is "APPROVE" or "SIGN")
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(ResolveSigToken()))
        {
            return true;
        }

        if (Payload.ValueKind == JsonValueKind.Object &&
            Payload.TryGetProperty("action", out var pAct) &&
            pAct.ValueKind == JsonValueKind.String &&
            string.Equals(pAct.GetString(), "APPROVE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// Request schema for offline batch sync.
/// @req:PRD-SYS-001
/// </summary>
public sealed record OfflineBatchSyncRequest(
    [property: JsonPropertyName("client_batch_id")] string ClientBatchId,
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("deltas")] IReadOnlyList<OfflineDeltaItem> Deltas
);

/// <summary>
/// Response schema for offline batch sync.
/// @req:PRD-SYS-001
/// </summary>
public sealed record OfflineBatchSyncResponse(
    [property: JsonPropertyName("client_batch_id")] string ClientBatchId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("processed_count")] int ProcessedCount,
    [property: JsonPropertyName("conflicts")] IReadOnlyList<object>? Conflicts = null
);
