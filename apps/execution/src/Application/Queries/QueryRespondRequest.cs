using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Ingestion DTO for answering an open or reopened clinical query.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public sealed record QueryRespondRequest(
    [property: JsonPropertyName("response")] string Response,
    [property: JsonPropertyName("responder")] string? Responder = null
);
