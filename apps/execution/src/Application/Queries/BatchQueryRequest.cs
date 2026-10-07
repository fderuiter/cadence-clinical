using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Ingestion DTO for batch closing or cancelling clinical queries.
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// @req:SRS-EDC-004
/// </summary>
public sealed record BatchQueryRequest(
    [property: JsonPropertyName("query_ids")] IReadOnlyList<string> QueryIds,
    [property: JsonPropertyName("reason")] string? Reason = null
);
