using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Ctms.Endpoints;

public record SiteGreenlightGateResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("site_name")] string SiteName,
    [property: JsonPropertyName("country_code")] string CountryCode,
    [property: JsonPropertyName("overall_status")] string OverallStatus,
    [property: JsonPropertyName("readiness_percentage")] int ReadinessPercentage,
    [property: JsonPropertyName("facet_regulatory_irb")] bool FacetRegulatoryIrb,
    [property: JsonPropertyName("facet_cta_budget")] bool FacetCtaBudget,
    [property: JsonPropertyName("facet_eisf_baseline")] bool FacetEisfBaseline,
    [property: JsonPropertyName("facet_doa_log")] bool FacetDoaLog,
    [property: JsonPropertyName("facet_staff_training")] bool FacetStaffTraining,
    [property: JsonPropertyName("facet_ip_release")] bool FacetIpRelease,
    [property: JsonPropertyName("is_compliant")] bool IsCompliant,
    [property: JsonPropertyName("greenlight_token")] string? GreenlightToken,
    [property: JsonPropertyName("greenlight_certified_by")] string? GreenlightCertifiedBy,
    [property: JsonPropertyName("greenlight_certified_at")] string? GreenlightCertifiedAt,
    [property: JsonPropertyName("is_overridden")] bool IsOverridden,
    [property: JsonPropertyName("unfulfilled_reasons")] List<string>? UnfulfilledReasons
);

public record BatchCertifyRequestDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("country_code")] string CountryCode,
    [property: JsonPropertyName("site_ids")] List<string> SiteIds,
    [property: JsonPropertyName("meaning")] string? Meaning
);

public record CertifiedSiteItemDto(
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("site_name")] string SiteName,
    [property: JsonPropertyName("greenlight_token")] string GreenlightToken,
    [property: JsonPropertyName("certified_at")] string CertifiedAt,
    [property: JsonPropertyName("certified_by")] string CertifiedBy,
    [property: JsonPropertyName("status")] string Status
);

public record BatchCertifyResponseDto(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("certified_count")] int CertifiedCount,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("country_code")] string CountryCode,
    [property: JsonPropertyName("certified_sites")] List<CertifiedSiteItemDto> CertifiedSites,
    [property: JsonPropertyName("message")] string Message
);
