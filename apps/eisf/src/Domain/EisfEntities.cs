using System;

namespace Cadence.Apps.Eisf.Domain;

public class IsfDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string BinderClassification { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string? ObjectKey { get; set; }
    public string? Content { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
    public DateTimeOffset? IssueDate { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? DocumentOwnerId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public string? CorrelationKey { get; set; }
    public string? ContentChecksum { get; set; }
    public string SyncStatus { get; set; } = "PENDING";
    public string SourceSystem { get; set; } = "eISF";
}

public class IsfAuditLog
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

public class EisfSectionTaxonomy
{
    public string SectionCode { get; set; } = string.Empty;
    public string SectionNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsMandatory { get; set; } = true;
}

public class EisfDocumentRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SiteId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string SectionCode { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Sha256Checksum { get; set; } = string.Empty;
    public int VersionMajor { get; set; } = 1;
    public int VersionMinor { get; set; } = 0;
    public string Status { get; set; } = "DRAFT";
    public DateTimeOffset? ExpirationDate { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = "Initial Document Ingestion";
    public int VersionIndex { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
}
