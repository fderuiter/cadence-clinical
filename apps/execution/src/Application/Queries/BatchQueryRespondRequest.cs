using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Ingestion DTO for batch answering open or reopened clinical queries.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public sealed record BatchQueryRespondRequest(
    [property: JsonPropertyName("query_ids")] IReadOnlyList<string> QueryIds,
    [property: JsonPropertyName("response")] string Response,
    [property: JsonPropertyName("reason")] string? Reason = null
);
