using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Observations;

public record LabReferenceRangeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("site_id")] string? SiteId,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("normalized_unit")] string NormalizedUnit,
    [property: JsonPropertyName("sex_applicability")] string SexApplicability,
    [property: JsonPropertyName("age_low")] double? AgeLow,
    [property: JsonPropertyName("age_high")] double? AgeHigh,
    [property: JsonPropertyName("low_bound")] double? LowBound,
    [property: JsonPropertyName("high_bound")] double? HighBound,
    [property: JsonPropertyName("critical_low")] double? CriticalLow,
    [property: JsonPropertyName("critical_high")] double? CriticalHigh,
    [property: JsonPropertyName("loinc_code")] string? LoincCode,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy
);

public record LabReferenceRangeCreateRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("source")] string Source = "CENTRAL",
    [property: JsonPropertyName("site_id")] string? SiteId = null,
    [property: JsonPropertyName("unit")] string Unit = "",
    [property: JsonPropertyName("normalized_unit")] string? NormalizedUnit = null,
    [property: JsonPropertyName("sex_applicability")] string SexApplicability = "ALL",
    [property: JsonPropertyName("age_low")] double? AgeLow = null,
    [property: JsonPropertyName("age_high")] double? AgeHigh = null,
    [property: JsonPropertyName("low_bound")] double? LowBound = null,
    [property: JsonPropertyName("high_bound")] double? HighBound = null,
    [property: JsonPropertyName("critical_low")] double? CriticalLow = null,
    [property: JsonPropertyName("critical_high")] double? CriticalHigh = null,
    [property: JsonPropertyName("loinc_code")] string? LoincCode = null,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange = "Creating lab reference range"
);

public record LabReferenceRangeUpdateRequest(
    [property: JsonPropertyName("test_name")] string? TestName = null,
    [property: JsonPropertyName("unit")] string? Unit = null,
    [property: JsonPropertyName("normalized_unit")] string? NormalizedUnit = null,
    [property: JsonPropertyName("sex_applicability")] string? SexApplicability = null,
    [property: JsonPropertyName("age_low")] double? AgeLow = null,
    [property: JsonPropertyName("age_high")] double? AgeHigh = null,
    [property: JsonPropertyName("low_bound")] double? LowBound = null,
    [property: JsonPropertyName("high_bound")] double? HighBound = null,
    [property: JsonPropertyName("critical_low")] double? CriticalLow = null,
    [property: JsonPropertyName("critical_high")] double? CriticalHigh = null,
    [property: JsonPropertyName("loinc_code")] string? LoincCode = null,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange = "Updating lab reference range"
);

public record BulkLabRangeDryRunRequest(
    [property: JsonPropertyName("study_id")] string StudyId = "STUDY-101",
    [property: JsonPropertyName("site_id")] string? SiteId = null,
    [property: JsonPropertyName("lab_source")] string LabSource = "CENTRAL",
    [property: JsonPropertyName("file_name")] string FileName = "lab_ranges.csv",
    [property: JsonPropertyName("file_content_base64")] string? FileContentBase64 = null,
    [property: JsonPropertyName("raw_file_content")] string? RawFileContent = null
);

public record BulkLabRangeRowPreview(
    [property: JsonPropertyName("row_number")] int RowNumber,
    [property: JsonPropertyName("is_valid")] bool IsValid,
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("site_id")] string? SiteId,
    [property: JsonPropertyName("sex_applicability")] string SexApplicability,
    [property: JsonPropertyName("age_low")] double? AgeLow,
    [property: JsonPropertyName("age_high")] double? AgeHigh,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("normalized_unit")] string NormalizedUnit,
    [property: JsonPropertyName("low_bound")] double? LowBound,
    [property: JsonPropertyName("high_bound")] double? HighBound,
    [property: JsonPropertyName("converted_low_bound")] double? ConvertedLowBound,
    [property: JsonPropertyName("converted_high_bound")] double? ConvertedHighBound,
    [property: JsonPropertyName("critical_low")] double? CriticalLow,
    [property: JsonPropertyName("critical_high")] double? CriticalHigh,
    [property: JsonPropertyName("loinc_code")] string? LoincCode,
    [property: JsonPropertyName("conversion_factor")] double? ConversionFactor,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public record BulkLabRangeDryRunResponse(
    [property: JsonPropertyName("is_success")] bool IsSuccess,
    [property: JsonPropertyName("total_rows")] int TotalRows,
    [property: JsonPropertyName("valid_rows")] int ValidRows,
    [property: JsonPropertyName("invalid_rows")] int InvalidRows,
    [property: JsonPropertyName("loinc_mapped_count")] int LoincMappedCount,
    [property: JsonPropertyName("unit_converted_count")] int UnitConvertedCount,
    [property: JsonPropertyName("rows")] IReadOnlyList<BulkLabRangeRowPreview> Rows,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("dry_run_id")] string DryRunId
);

public record BulkLabRangeImportCommitRequest(
    [property: JsonPropertyName("dry_run_id")] string? DryRunId = null,
    [property: JsonPropertyName("study_id")] string StudyId = "STUDY-101",
    [property: JsonPropertyName("site_id")] string? SiteId = null,
    [property: JsonPropertyName("lab_source")] string LabSource = "CENTRAL",
    [property: JsonPropertyName("file_name")] string FileName = "lab_ranges.csv",
    [property: JsonPropertyName("file_content_base64")] string? FileContentBase64 = null,
    [property: JsonPropertyName("raw_file_content")] string? RawFileContent = null,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange = "Bulk laboratory range import"
);

public record BulkLabRangeImportJobResponse(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string? SiteId,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("file_type")] string FileType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("total_rows")] int TotalRows,
    [property: JsonPropertyName("success_rows")] int SuccessRows,
    [property: JsonPropertyName("error_rows")] int ErrorRows,
    [property: JsonPropertyName("error_summary_json")] string? ErrorSummaryJson,
    [property: JsonPropertyName("preview_summary_json")] string? PreviewSummaryJson,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("submitted_by")] string SubmittedBy,
    [property: JsonPropertyName("submitted_at")] DateTimeOffset SubmittedAt,
    [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt
);

public record UnitConversionRequest(
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("from_unit")] string FromUnit,
    [property: JsonPropertyName("to_unit")] string ToUnit
);

public record UnitConversionResponse(
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("input_value")] double InputValue,
    [property: JsonPropertyName("converted_value")] double ConvertedValue,
    [property: JsonPropertyName("from_unit")] string FromUnit,
    [property: JsonPropertyName("to_unit")] string ToUnit,
    [property: JsonPropertyName("factor")] double Factor,
    [property: JsonPropertyName("loinc_code")] string? LoincCode
);

public record LoincCatalogItemResponse(
    [property: JsonPropertyName("test_code")] string TestCode,
    [property: JsonPropertyName("test_name")] string TestName,
    [property: JsonPropertyName("loinc_code")] string LoincCode,
    [property: JsonPropertyName("standard_unit")] string StandardUnit,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("molecular_weight")] double? MolecularWeight
);

public record LoincCatalogResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<LoincCatalogItemResponse> Items,
    [property: JsonPropertyName("total_count")] int TotalCount
);
