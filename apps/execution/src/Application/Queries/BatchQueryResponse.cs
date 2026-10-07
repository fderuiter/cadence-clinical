using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Response DTO for batch query lifecycle actions.
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// @req:SRS-EDC-004
/// </summary>
public sealed record BatchQueryResponse(
    [property: JsonPropertyName("queries")] IReadOnlyList<ClinicalQueryResponse> Queries,
    [property: JsonPropertyName("count")] int Count
);
