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

/// <summary>
/// Exception thrown when DICOM pixel masking or frame decoding fails.
/// </summary>
public class DicomPixelDeidException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DicomPixelDeidException"/> class.
    /// </summary>
    public DicomPixelDeidException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomPixelDeidException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public DicomPixelDeidException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomPixelDeidException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public DicomPixelDeidException(string message, Exception innerException) : base(message, innerException) { }
}

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

    /// <summary>When the image was de-identified (ISO 8601, UTC). Set by <c>DicomDeidentifier.Deidentify</c> from its caller's time.</summary>
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }
}

public class BatchDeidentificationManifest
{
    [JsonPropertyName("manifest_id")]
    public string ManifestId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("study_envelope_id")]
    public string StudyEnvelopeId { get; set; } = string.Empty;

    [JsonPropertyName("study_id")]
    public string StudyId { get; set; } = string.Empty;

    [JsonPropertyName("site_id")]
    public string SiteId { get; set; } = string.Empty;

    [JsonPropertyName("subject_id")]
    public string SubjectId { get; set; } = string.Empty;

    [JsonPropertyName("total_files_processed")]
    public int TotalFilesProcessed { get; set; }

    [JsonPropertyName("ingested_slice_count")]
    public int IngestedSliceCount { get; set; }

    [JsonPropertyName("skipped_non_dicom_count")]
    public int SkippedNonDicomCount { get; set; }

    [JsonPropertyName("skipped_non_dicom_files")]
    public List<string> SkippedNonDicomFiles { get; set; } = [];

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
    public string Timestamp { get; set; } = string.Empty;

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

/// <summary>
/// Compact slice de-identification result produced by streaming ingestion without retaining raw or scrubbed DICOM byte buffers.
/// </summary>
public sealed record DicomSliceDeidResult(
    string SourceSha256,
    string TargetSha256,
    long DeidentifiedSizeBytes,
    string OriginalStudyInstanceUidHash,
    string OriginalSopInstanceUidHash,
    string AnonymizedStudyUid,
    string AnonymizedSeriesUid,
    string AnonymizedSopUid,
    string Modality,
    int Rows,
    int Columns,
    int BitsAllocated,
    IReadOnlyList<double> PixelSpacing,
    double? SliceThickness,
    double? SliceLocation,
    double? WindowCenter,
    double? WindowWidth,
    int ScrubbedTagsCount,
    IReadOnlyList<string> ScrubbedTags,
    bool PixelMaskApplied,
    IReadOnlyList<PixelMaskBoundingBox> PixelMasks
);
