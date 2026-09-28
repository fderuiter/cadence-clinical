using System;

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
    public double Score { get; set; }
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
    public string? SignerEmail { get; set; }
    public string? Meaning { get; set; }
    public string? SignatureData { get; set; }
    public DateTime SignedAt { get; set; } = DateTime.UtcNow;
    public string? DigestSha256 { get; set; }
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
