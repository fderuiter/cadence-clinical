using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Cadence.Deid;

public static class DetectorCategory
{
    public const string Email = "email";
    public const string TelephoneFax = "telephone_fax";
    public const string SsnNationalId = "ssn_national_id";
    public const string Dates = "dates";
    public const string ZipGeographic = "zip_geographic";
    public const string Urls = "urls";
    public const string IpMacAddresses = "ip_mac_addresses";
    public const string MedicalRecordAccount = "medical_record_account";
    public const string Age = "age";
    public const string Custom = "custom";
    public const string HealthPlanBeneficiary = "health_plan_beneficiary";
    public const string CertificateLicense = "certificate_license";
    public const string VehicleIdentifiers = "vehicle_identifiers";
    public const string DeviceSerial = "device_serial";
}

public enum ComplianceProfile
{
    HIPAA,
    GDPR,
    EU_CTR
}

public static class ProfileCategories
{
    public static ImmutableHashSet<string> AllCategories { get; } = ImmutableHashSet.Create(
        DetectorCategory.Email,
        DetectorCategory.TelephoneFax,
        DetectorCategory.SsnNationalId,
        DetectorCategory.Dates,
        DetectorCategory.ZipGeographic,
        DetectorCategory.Urls,
        DetectorCategory.IpMacAddresses,
        DetectorCategory.MedicalRecordAccount,
        DetectorCategory.Age,
        DetectorCategory.Custom,
        DetectorCategory.HealthPlanBeneficiary,
        DetectorCategory.CertificateLicense,
        DetectorCategory.VehicleIdentifiers,
        DetectorCategory.DeviceSerial
    );

    public static ImmutableHashSet<string> EuCtrCategories { get; } = ImmutableHashSet.Create(
        DetectorCategory.Email,
        DetectorCategory.TelephoneFax,
        DetectorCategory.SsnNationalId,
        DetectorCategory.Dates,
        DetectorCategory.MedicalRecordAccount,
        DetectorCategory.Age,
        DetectorCategory.Custom
    );

    public static HashSet<string> GetCategoriesForProfile(ComplianceProfile profile)
    {
        return profile switch
        {
            ComplianceProfile.EU_CTR => new HashSet<string>(EuCtrCategories),
            _ => new HashSet<string>(AllCategories)
        };
    }
}

public class DetectionResult
{
    public string Category { get; set; }
    public int Start { get; set; }
    public int End { get; set; }
    public string Value { get; set; }

    public DetectionResult(string category, int start, int end, string value)
    {
        Category = category;
        Start = start;
        End = end;
        Value = value;
    }
}

public record PixelMaskBoundingBox(
    [property: JsonPropertyName("x_min")] int XMin,
    [property: JsonPropertyName("y_min")] int YMin,
    [property: JsonPropertyName("x_max")] int XMax,
    [property: JsonPropertyName("y_max")] int YMax
);

public class DicomDeidCertificate
{
    [JsonPropertyName("certificate_id")]
    public string CertificateId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("source_sha256")]
    public string SourceSha256 { get; set; } = string.Empty;

    [JsonPropertyName("target_sha256")]
    public string TargetSha256 { get; set; } = string.Empty;

    [JsonPropertyName("study_id")]
    public string StudyId { get; set; } = string.Empty;

    [JsonPropertyName("site_id")]
    public string SiteId { get; set; } = string.Empty;

    [JsonPropertyName("subject_id")]
    public string SubjectId { get; set; } = string.Empty;

    [JsonPropertyName("original_study_instance_uid_hash")]
    public string OriginalStudyInstanceUidHash { get; set; } = string.Empty;

    [JsonPropertyName("original_sop_instance_uid_hash")]
    public string OriginalSopInstanceUidHash { get; set; } = string.Empty;

    [JsonPropertyName("anonymized_study_instance_uid")]
    public string AnonymizedStudyInstanceUid { get; set; } = string.Empty;

    [JsonPropertyName("anonymized_sop_instance_uid")]
    public string AnonymizedSopInstanceUid { get; set; } = string.Empty;

    [JsonPropertyName("scrubbed_tags_count")]
    public int ScrubbedTagsCount { get; set; }

    [JsonPropertyName("scrubbed_tags")]
    public List<string> ScrubbedTags { get; set; } = [];

    [JsonPropertyName("pixel_mask_applied")]
    public bool PixelMaskApplied { get; set; }

    [JsonPropertyName("pixel_masks")]
    public List<PixelMaskBoundingBox> PixelMasks { get; set; } = [];

    [JsonPropertyName("operator_identity")]
    public string OperatorIdentity { get; set; } = "cadence.imaging.pipeline";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }
}

public record DicomDeidResult(
    byte[] DeidentifiedBytes,
    DicomDeidCertificate Certificate,
    string AnonymizedStudyUid,
    string AnonymizedSeriesUid,
    string AnonymizedSopUid,
    string Modality,
    int Rows = 0,
    int Columns = 0,
    int BitsAllocated = 16,
    List<double>? PixelSpacing = null,
    double? SliceThickness = null,
    double? SliceLocation = null,
    double? WindowCenter = null,
    double? WindowWidth = null
);

public record RedactionRecordItem(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("strategy")] string Strategy,
    [property: JsonPropertyName("start")] int Start,
    [property: JsonPropertyName("end")] int End,
    [property: JsonPropertyName("replacement")] string Replacement
);

public class RedactionManifest
{
    [JsonPropertyName("categories_counts")]
    public Dictionary<string, int> CategoriesCounts { get; set; } = [];

    [JsonPropertyName("strategies")]
    public Dictionary<string, string> Strategies { get; set; } = [];

    [JsonPropertyName("operator_identity")]
    public string OperatorIdentity { get; set; } = string.Empty;

    private string _reason = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason
    {
        get => _reason;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Reason for redaction must be a non-empty string.", nameof(value));
            }
            _reason = value;
        }
    }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("source_version")]
    public string SourceVersion { get; set; } = string.Empty;

    [JsonPropertyName("target_version")]
    public string TargetVersion { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }
}
