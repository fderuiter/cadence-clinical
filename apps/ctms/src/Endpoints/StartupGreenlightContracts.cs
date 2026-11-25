using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Ctms.Endpoints;

public static class GreenlightTokenGenerator
{
    public static string GenerateToken(
        string siteId,
        string studyId,
        string countryCode,
        string certifiedBy,
        string secret,
        long issuedAt,
        long expiresAt,
        bool isOverridden = false)
    {
        var payloadObj = new Dictionary<string, object?>
        {
            ["certified_by"] = certifiedBy,
            ["country_code"] = countryCode.ToUpperInvariant(),
            ["expires_at"] = expiresAt,
            ["is_overridden"] = isOverridden,
            ["issued_at"] = issuedAt,
            ["site_id"] = siteId,
            ["study_id"] = studyId,
            ["token_type"] = "SITE_GREENLIGHT_ACTIVATION",
            ["token_version"] = "1"
        };

        string json = JsonSerializer.Serialize(payloadObj);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        string b64Payload = Convert.ToBase64String(jsonBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(b64Payload));
        string hexSig = Convert.ToHexStringLower(hashBytes);

        return $"{b64Payload}.{hexSig}";
    }
}

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
