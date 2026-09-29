using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Observations;

/// <summary>
/// Ingestion DTO for recording a discrete clinical observation. <c>observation_date</c> is when the observation
/// was made and is required: a request without it is rejected (<c>OBSERVATION_DATE_REQUIRED</c>) rather than
/// stamped with the time it is recorded (#4769). It has no default, so the OpenAPI document lists it as required, and stays
/// nullable only so a missing value reaches the service and is reported as that domain error.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public sealed record ObservationCreateRequest(
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("observation_date")] DateTime? ObservationDate,
    [property: JsonPropertyName("study_id")] string? StudyId = null,
    [property: JsonPropertyName("site_id")] string? SiteId = null,
    [property: JsonPropertyName("visit_id")] string? VisitId = null,
    [property: JsonPropertyName("value")] double? Value = null,
    [property: JsonPropertyName("value_string")] string? ValueString = null,
    [property: JsonPropertyName("unit")] string? Unit = null,
    [property: JsonPropertyName("lab_source")] string? LabSource = null,
    [property: JsonPropertyName("lab_site_id")] string? LabSiteId = null,
    [property: JsonPropertyName("page_id")] string? PageId = null,
    [property: JsonPropertyName("protocol_version_tag")] string? ProtocolVersionTag = null,
    [property: JsonPropertyName("protocol_version_index")] int? ProtocolVersionIndex = null
);
