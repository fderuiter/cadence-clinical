using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// Response DTO returning a single historical field revision entry extracted from AuditLogEntries.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public sealed record FieldAuditHistoryEntryResponse(
    [property: JsonPropertyName("version_index")] long VersionIndex,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("change_reason")] string ChangeReason,
    [property: JsonPropertyName("old_value")] string? OldValue,
    [property: JsonPropertyName("new_value")] string? NewValue,
    [property: JsonPropertyName("block_hash")] string BlockHash,
    [property: JsonPropertyName("predecessor_hash")] string? PredecessorHash = null,
    [property: JsonPropertyName("action")] string? Action = null
);
