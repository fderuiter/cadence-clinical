using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Ingestion DTO for answering an open or reopened clinical query. The responder is always the authenticated
/// caller, never a body field (#4920).
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public sealed record QueryRespondRequest(
    [property: JsonPropertyName("response")] string Response
);
