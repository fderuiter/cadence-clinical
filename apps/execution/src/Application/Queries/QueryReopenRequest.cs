using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Ingestion DTO for reopening an answered or closed clinical query.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public sealed record QueryReopenRequest(
    [property: JsonPropertyName("reason")] string Reason
);
