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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class EtmfArchivalDelivery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Status { get; set; } = "PENDING";
    public int Attempts { get; set; } = 0;
    public string? LastError { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool RetryEligible { get; set; } = true;
    public string CorrelationId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public int VersionIndex { get; set; }
    public string SubjectPseudonym { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string ArtifactContent { get; set; } = string.Empty;
    public string? EtmfDocumentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime ServerTimestamp { get; set; } = DateTime.UtcNow;
    public DateTime? DeviceTimestamp { get; set; }
    public string SignatureManifestJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime SignedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class ConsentAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ActorId { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public string Details { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public string TenantId { get; set; } = "tenant_default";
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime SelectedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime? DeadlineAt { get; set; }
    public string? CompletedConsentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public DateTime WithdrawalDate { get; set; } = DateTime.UtcNow;
    public string ReasonCategory { get; set; } = string.Empty;
    public string ReasonDetail { get; set; } = string.Empty;
    public string Scope { get; set; } = "STOP_ALL_DATA_COLLECTION";
    public bool AcknowledgedByInvestigator { get; set; }
    public string? InvestigatorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

/// <summary>
/// Binds a captured e-signature to the content it signed (21 CFR 11.70 signature/record linking) without keeping the
/// raw capture. The binding is SHA-256 over the UTF-8 bytes of
/// <c>"cadence.econsent.signature-binding.v1\n" + documentDigestSha256 + "\n" + signatureData</c>, lowercase hex.
/// Migration <c>ProtectSignerEmailAndBindSignatureData</c> computes the same value in SQL for existing rows.
/// @req:PRD-SYS-053
/// </summary>
public static class ConsentSignatureBinding
{
    /// <summary>Domain-separation label; change it (and the version suffix) if the input layout ever changes.</summary>
    public const string Label = "cadence.econsent.signature-binding.v1";

    /// <summary>Returns the lowercase hex SHA-256 binding of <paramref name="signatureData"/> to the document digest.</summary>
    public static string Compute(string? documentDigestSha256, string signatureData)
    {
        ArgumentNullException.ThrowIfNull(signatureData);
        string input = $"{Label}\n{documentDigestSha256 ?? string.Empty}\n{signatureData}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    /// <summary>
    /// True when <paramref name="signatureData"/> and <paramref name="documentDigestSha256"/> reproduce the stored
    /// <paramref name="bindingSha256"/>. Compares in constant time.
    /// </summary>
    public static bool Matches(string? bindingSha256, string? documentDigestSha256, string signatureData)
    {
        if (bindingSha256 is null)
        {
            return false;
        }

        byte[] expected = Encoding.ASCII.GetBytes(Compute(documentDigestSha256, signatureData));
        byte[] actual = Encoding.ASCII.GetBytes(bindingSha256.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
