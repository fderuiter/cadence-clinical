using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Econsent.Endpoints;

public sealed record PublishTemplateRequest(
    [property: JsonPropertyName("study_id")] string? StudyId,
    [property: JsonPropertyName("protocol_version")] string? ProtocolVersion,
    [property: JsonPropertyName("change_summary")] string? ChangeSummary,
    [property: JsonPropertyName("change_reason")] string? ChangeReason,
    [property: JsonPropertyName("requires_reconsent")] bool? RequiresReconsent,
    [property: JsonPropertyName("channels")] string[]? Channels);

public sealed record PublishTemplateResponse(
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("is_published")] bool IsPublished,
    [property: JsonPropertyName("requires_reconsent")] bool RequiresReconsent,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt);

public sealed record CaptureConsentRequest(
    [property: JsonPropertyName("subject_pseudonym")] string? SubjectPseudonym,
    [property: JsonPropertyName("study_id")] string? StudyId,
    [property: JsonPropertyName("site_id")] string? SiteId,
    [property: JsonPropertyName("signer_name")] string? SignerName,
    [property: JsonPropertyName("signer_email")] string? SignerEmail,
    [property: JsonPropertyName("signer_role")] string? SignerRole,
    [property: JsonPropertyName("meaning")] string? Meaning,
    [property: JsonPropertyName("signing_reason")] string? SigningReason,
    [property: JsonPropertyName("protocol_version")] string? ProtocolVersion,
    [property: JsonPropertyName("digest_sha256")] string? DigestSha256,
    [property: JsonPropertyName("signature_data")] string? SignatureData,
    [property: JsonPropertyName("liveness_token")] string? LivenessToken,
    [property: JsonPropertyName("device_timestamp")] DateTimeOffset? DeviceTimestamp);

public sealed record CaptureConsentResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("subject_pseudonym")] string SubjectPseudonym,
    [property: JsonPropertyName("signer_name")] string SignerName,
    [property: JsonPropertyName("signer_role")] string SignerRole,
    [property: JsonPropertyName("meaning")] string Meaning,
    [property: JsonPropertyName("server_timestamp")] DateTimeOffset ServerTimestamp,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("sha256_hash")] string Sha256Hash);
