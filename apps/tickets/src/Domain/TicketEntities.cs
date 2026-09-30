using System;

namespace Cadence.Apps.Tickets.Domain;

public class Ticket
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "OTHER";
    public string Priority { get; set; } = "LOW";
    public string Status { get; set; } = "OPEN";
    public string Reporter { get; set; } = string.Empty;
    public string? AssigneeUser { get; set; }
    public string? AssigneeRole { get; set; }
    public string? OrgId { get; set; }
    public string? SiteId { get; set; }
    public string? StudyId { get; set; }
    public string? RelatedEntityType { get; set; }
    public string? RelatedEntityId { get; set; }
    public string? ContextPayload { get; set; }
    public string GxpSeverity { get; set; } = "NOT_APPLICABLE";
    public string? RootCauseCategory { get; set; }
    public string? RootCauseSummary { get; set; }
    public string? ResolutionCode { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? SlaTargetAt { get; set; }
    public DateTimeOffset? SlaPausedAt { get; set; }
    public int SlaTotalPausedSeconds { get; set; }
    public bool SlaBreached { get; set; }
    public bool SlaAmberWarned { get; set; }
    public DateTimeOffset? LastEscalatedAt { get; set; }
    public DateTimeOffset? LastEscalationNotifiedAt { get; set; }
    public int EscalationCount { get; set; }
    public string? WorkflowType { get; set; }
    public string? ActionType { get; set; }
    public string? SignatureAction { get; set; }
    public string? SignatureToken { get; set; }
    public string? SignatureMeaning { get; set; }
    public DateTimeOffset? SignatureTimestamp { get; set; }
    public string? SignatureUser { get; set; }
    public bool IsDeleted { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class TicketComment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TicketId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Visibility { get; set; } = "PUBLIC";
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class TicketAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TicketId { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public int FileSizeBytes { get; set; }
    public string MimeType { get; set; } = "application/octet-stream";
    public string StorageUri { get; set; } = string.Empty;
    public string Sha256Hash { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public required DateTimeOffset UploadedAt { get; set; }
    public bool DeidScrubbed { get; set; } = true;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class TicketAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? TicketId { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? ReasonForChange { get; set; }
    public int VersionIndex { get; set; } = 1;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? RecordId { get; set; }
}
