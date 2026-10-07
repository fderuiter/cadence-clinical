using System;

namespace Cadence.Apps.Knowledge.Domain;

public class KnowledgeCategory
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ParentId { get; set; }
    public string? PersonaVisibility { get; set; }
    public bool IsDeleted { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class KnowledgeArticle
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string Status { get; set; } = "DRAFT";
    public int VersionIndex { get; set; } = 1;
    public string VersionLabel { get; set; } = "1.0";
    public string? CurrentPublishedVersionId { get; set; }
    public string? Tags { get; set; }
    public string AuthorUserId { get; set; } = string.Empty;
    public string? LastEditedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public bool IsDeleted { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class KnowledgeArticleVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ArticleId { get; set; } = string.Empty;
    public int VersionIndex { get; set; }
    public string VersionLabel { get; set; } = string.Empty;
    public string StatusAtSnapshot { get; set; } = string.Empty;
    public string BodyMarkdown { get; set; } = string.Empty;
    public string? BodyHtml { get; set; }
    public bool IsLocked { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class KnowledgeArticleAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? ArticleId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? PreviousStatus { get; set; }
    public string? NewStatus { get; set; }
    public string? Details { get; set; }
    public string? PreviousDigest { get; set; }
    public string? Sha256Digest { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
}

public class ContextualHelpMapping
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RoutePattern { get; set; } = string.Empty;
    public string? Persona { get; set; }
    public string ArticleId { get; set; } = string.Empty;
    public string? SectionAnchor { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class ProtocolKnowledgeChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DocumentId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = string.Empty;
    public string DocumentType { get; set; } = "PROTOCOL";
    public string? SectionNumber { get; set; }
    public string SectionTitle { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; }
    public string ChunkText { get; set; } = string.Empty;
    public string? EmbeddingJson { get; set; }
    public int TokenCount { get; set; }
    public bool IsApproved { get; set; } = true;
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class TrainingCurriculum
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = "1.0";
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class TrainingAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CurriculumId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public string? BatchTransactionId { get; set; }
    public double? QuizScore { get; set; }
    public int TimeSpentSeconds { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class DelegationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string StaffUserId { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string TaskCode { get; set; } = string.Empty;
    public string Duties { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public bool SignedOff { get; set; }
    public string? PiSignatureHash { get; set; }
    public DateTimeOffset? PiApprovedAt { get; set; }
    public string? BatchTransactionId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

public class OrgAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required DateTimeOffset Timestamp { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? RecordId { get; set; }
    public string? BatchTransactionId { get; set; }
    public string Details { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class TrainingCertificate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AssignmentId { get; set; } = string.Empty;
    public string? BatchTransactionId { get; set; }
    public string SignerName { get; set; } = string.Empty;
    public string SignerRole { get; set; } = string.Empty;
    public string Meaning { get; set; } = string.Empty;
    public string Sha256Seal { get; set; } = string.Empty;
    public string EtmfArtifactId { get; set; } = "05.02.04";
    public required DateTimeOffset CreatedAt { get; set; }
}
