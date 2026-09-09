using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Ingestion DTO for closing an answered or candidate clinical query.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public sealed record QueryCloseRequest(
    [property: JsonPropertyName("resolver")] string? Resolver = null
);
