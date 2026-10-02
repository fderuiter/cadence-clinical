using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Observations;

/// <summary>
/// Response DTO returning details of a discrete clinical observation.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public sealed record ObservationResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("observation_date")] DateTime ObservationDate,
    [property: JsonPropertyName("site_id")] string? SiteId = null,
    [property: JsonPropertyName("visit_id")] string? VisitId = null,
    [property: JsonPropertyName("value")] double? Value = null,
    [property: JsonPropertyName("value_string")] string? ValueString = null,
    [property: JsonPropertyName("unit")] string? Unit = null,
    [property: JsonPropertyName("normalized_value")] double? NormalizedValue = null,
    [property: JsonPropertyName("normalized_unit")] string? NormalizedUnit = null,
    [property: JsonPropertyName("is_outlier")] bool IsOutlier = false,
    [property: JsonPropertyName("is_sdv_verified")] bool IsSdvVerified = false,
    [property: JsonPropertyName("sdv_verified_by")] string? SdvVerifiedBy = null,
    [property: JsonPropertyName("sdv_verified_at")] DateTime? SdvVerifiedAt = null,
    [property: JsonPropertyName("lab_source")] string? LabSource = null,
    [property: JsonPropertyName("lab_site_id")] string? LabSiteId = null,
    [property: JsonPropertyName("lab_indicator")] string? LabIndicator = null,
    [property: JsonPropertyName("lab_out_of_range")] bool? LabOutOfRange = null,
    [property: JsonPropertyName("matched_normal_bounds")] string? MatchedNormalBounds = null,
    [property: JsonPropertyName("range_indicator")] string? RangeIndicator = null,
    [property: JsonPropertyName("is_out_of_range")] bool? IsOutOfRange = null,
    [property: JsonPropertyName("reference_range_low")] double? ReferenceRangeLow = null,
    [property: JsonPropertyName("reference_range_high")] double? ReferenceRangeHigh = null,
    [property: JsonPropertyName("protocol_version_tag")] string? ProtocolVersionTag = null,
    [property: JsonPropertyName("protocol_version_index")] int? ProtocolVersionIndex = null,
    [property: JsonPropertyName("form_submission_id")] string? FormSubmissionId = null,
    [property: JsonPropertyName("is_active")] bool IsActive = true
);
