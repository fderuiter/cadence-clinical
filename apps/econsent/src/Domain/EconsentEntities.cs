using System;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Apps.Econsent.Domain;

public class ConsentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class ConsentDocumentRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DocumentId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string StudyCode { get; set; } = string.Empty;
    public string SiteCode { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public required DateTimeOffset RecordedAt { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;

    public static ConsentDocumentRevision FromDocument(ConsentDocument doc, DateTimeOffset recordedAt, string recordedBy) =>
        new()
        {
            DocumentId = doc.Id,
            RevisionNumber = doc.VersionIndex,
            StudyCode = doc.StudyId,
            SiteCode = doc.SiteId,
            DocumentName = doc.DocumentName,
            Content = doc.Content,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            ReasonForChange = doc.ReasonForChange,
        };
}

public class EtmfArchivalDelivery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Status { get; set; } = "PENDING";
    public int Attempts { get; set; } = 0;
    public string? LastError { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool RetryEligible { get; set; } = true;
    public string CorrelationId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public int VersionIndex { get; set; }
    public string SubjectPseudonym { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string ArtifactContent { get; set; } = string.Empty;
    public string? EtmfDocumentId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class SubjectConsent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubjectPseudonym { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public int VersionIndex { get; set; }
    public string ProtocolVersion { get; set; } = string.Empty;
    public string SourceContentIdentity { get; set; } = string.Empty;
    public string Status { get; set; } = "ACTIVE";
    public required DateTimeOffset ServerTimestamp { get; set; }
    public DateTimeOffset? DeviceTimestamp { get; set; }
    public string SignatureManifestJson { get; set; } = "{}";
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class ComprehensionCheck
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = string.Empty;
    public int VersionIndex { get; set; }
    public string QuestionsJson { get; set; } = "[]";
    public string ExpectedAnswersJson { get; set; } = "{}";
    public string ThresholdPolicyJson { get; set; } = "{}";
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class ComprehensionResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = string.Empty;
    public int VersionIndex { get; set; }
    public string SubjectPseudonym { get; set; } = string.Empty;
    public string QuestionsJson { get; set; } = "[]";
    public string ExpectedAnswersJson { get; set; } = "{}";
    public string ThresholdPolicyJson { get; set; } = "{}";
    public string SubmittedAnswersJson { get; set; } = "{}";
    public bool Passed { get; set; }
    public decimal Score { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class ConsentSignature
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = string.Empty;
    public int VersionIndex { get; set; }
    public string SubjectPseudonym { get; set; } = string.Empty;
    public string Role { get; set; } = "SUBJECT";
    public string? SignerName { get; set; }
    /// <summary>Signer contact address. Encrypted at rest (#4770).</summary>
    public string? SignerEmail { get; set; }
    public string? Meaning { get; set; }
    public required DateTimeOffset SignedAt { get; set; }
    /// <summary>SHA-256 (lowercase hex) of the consent document content that was signed.</summary>
    public string? DigestSha256 { get; set; }
    /// <summary>
    /// SHA-256 binding of the captured signature to <see cref="DigestSha256"/>, computed with
    /// <see cref="ConsentSignatureBinding.Compute"/>. The raw signature capture (drawn image, typed name payload) is
    /// not stored (#4770); a presented capture is verified with <see cref="ConsentSignatureBinding.Matches"/>.
    /// </summary>
    public string? SignatureBindingSha256 { get; set; }
    public string? LarRelationship { get; set; }
    public string? LarAuthorityBasis { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class ConsentClause
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClauseId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class ConsentTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public bool RequiresReconsent { get; set; }
    public int VersionIndex { get; set; } = 1;
    public string ClausesJson { get; set; } = "[]";
    public string WorkflowStepsJson { get; set; } = "[]";
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class ConsentAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required DateTimeOffset Timestamp { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public string Details { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? PreviousDigest { get; set; }
    public string? Sha256Digest { get; set; }
}

public class ConsentTranslation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TranslationId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int SourceVersionIndex { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string TranslatedTitle { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public string Status { get; set; } = "DRAFT";
    public int VersionIndex { get; set; } = 1;
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class GranularConsentOption
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = string.Empty;
    public int VersionIndex { get; set; }
    public string OptionCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "OTHER";
    public bool IsMandatory { get; set; }
    public bool DefaultSelected { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class SubjectConsentOptionSelection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConsentId { get; set; } = string.Empty;
    public string SubjectPseudonym { get; set; } = string.Empty;
    public string OptionCode { get; set; } = string.Empty;
    public bool Selected { get; set; }
    public required DateTimeOffset SelectedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class ReconsentRequirement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public int PriorVersionIndex { get; set; }
    public int NewVersionIndex { get; set; }
    public string SubjectPseudonym { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public string ChangeSummary { get; set; } = string.Empty;
    public string SubstantiveChangesJson { get; set; } = "[]";
    public DateTimeOffset? DeadlineAt { get; set; }
    public string? CompletedConsentId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class ConsentWithdrawal
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string SubjectPseudonym { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public required DateTimeOffset WithdrawalDate { get; set; }
    public string ReasonCategory { get; set; } = string.Empty;
    public string ReasonDetail { get; set; } = string.Empty;
    public string Scope { get; set; } = "STOP_ALL_DATA_COLLECTION";
    public bool AcknowledgedByInvestigator { get; set; }
    public string? InvestigatorId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class BiometricLivenessVerification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConsentId { get; set; } = string.Empty;
    public string SubjectPseudonym { get; set; } = string.Empty;
    public string LivenessToken { get; set; } = string.Empty;
    public string LivenessTokenHash { get; set; } = string.Empty;
    public decimal AntiSpoofScore { get; set; }
    public bool IsLive { get; set; }
    public string Status { get; set; } = "VERIFIED";
    public required DateTimeOffset VerifiedAt { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class ReplicatedScopeLock
{
    public Cadence.Kernel.TenantId TenantId { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string LockId { get; set; } = string.Empty;
    public string StudyRef { get; set; } = string.Empty;
    public string StudyCode { get; set; } = string.Empty;
    public string? SiteCode { get; set; }
    public string? SubjectCode { get; set; }
    public string ScopeKind { get; set; } = "study";
    public string ScopeRef { get; set; } = string.Empty;
    public string LockKind { get; set; } = "Locked";
    public string Milestone { get; set; } = "InterimCut";
    public bool IsActive { get; set; } = true;
    public required DateTimeOffset AppliedAt { get; set; }
    public string AppliedBy { get; set; } = string.Empty;
    public DateTimeOffset? ReleasedAt { get; set; }
    public string? ReleasedBy { get; set; }
    public string? ReleaseReason { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

/// <summary>
/// Binds a captured e-signature to the content it signed (21 CFR 11.70 signature/record linking) without keeping the
/// raw capture. The binding is SHA-256 over the UTF-8 bytes of
/// <c>"cadence.econsent.signature-binding.v1\n" + documentDigestSha256 + "\n" + signatureData[\n + livenessTokenHash]</c>, lowercase hex.
/// Migration <c>ProtectSignerEmailAndBindSignatureData</c> computes the same value in SQL for existing rows.
/// </summary>
public static class ConsentSignatureBinding
{
    /// <summary>Domain-separation label; change it (and the version suffix) if the input layout ever changes.</summary>
    public const string Label = "cadence.econsent.signature-binding.v1";

    /// <summary>Returns the lowercase hex SHA-256 binding of <paramref name="signatureData"/> to the document digest and optional liveness token hash.</summary>
    public static string Compute(string? documentDigestSha256, string signatureData, string? livenessTokenHash = null)
    {
        ArgumentNullException.ThrowIfNull(signatureData);
        string input = string.IsNullOrWhiteSpace(livenessTokenHash)
            ? $"{Label}\n{documentDigestSha256 ?? string.Empty}\n{signatureData}"
            : $"{Label}\n{documentDigestSha256 ?? string.Empty}\n{signatureData}\n{livenessTokenHash}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    /// <summary>
    /// True when <paramref name="signatureData"/> and <paramref name="documentDigestSha256"/> reproduce the stored
    /// <paramref name="bindingSha256"/>. Compares in constant time.
    /// </summary>
    public static bool Matches(string? bindingSha256, string? documentDigestSha256, string signatureData, string? livenessTokenHash = null)
    {
        if (bindingSha256 is null)
        {
            return false;
        }

        byte[] expected = Encoding.ASCII.GetBytes(Compute(documentDigestSha256, signatureData, livenessTokenHash));
        byte[] actual = Encoding.ASCII.GetBytes(bindingSha256.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
