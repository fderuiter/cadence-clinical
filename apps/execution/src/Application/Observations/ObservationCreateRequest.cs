using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Observations;

/// <summary>
/// Ingestion DTO for recording a discrete clinical observation.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public sealed record ObservationCreateRequest(
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("study_id")] string? StudyId = null,
    [property: JsonPropertyName("site_id")] string? SiteId = null,
    [property: JsonPropertyName("visit_id")] string? VisitId = null,
    [property: JsonPropertyName("value")] double? Value = null,
    [property: JsonPropertyName("value_string")] string? ValueString = null,
    [property: JsonPropertyName("unit")] string? Unit = null,
    [property: JsonPropertyName("observation_date")] DateTime? ObservationDate = null,
    [property: JsonPropertyName("lab_source")] string? LabSource = null,
    [property: JsonPropertyName("lab_site_id")] string? LabSiteId = null,
    [property: JsonPropertyName("page_id")] string? PageId = null,
    [property: JsonPropertyName("protocol_version_tag")] string? ProtocolVersionTag = null,
    [property: JsonPropertyName("protocol_version_index")] int? ProtocolVersionIndex = null
);
