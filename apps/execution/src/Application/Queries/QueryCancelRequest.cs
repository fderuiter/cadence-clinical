using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Ingestion DTO for cancelling a clinical query raised in error.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public sealed record QueryCancelRequest(
    [property: JsonPropertyName("reason")] string Reason
);
