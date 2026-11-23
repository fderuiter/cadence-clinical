using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Tickets.Domain;

public class Ticket
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "OTHER";
    public string Priority { get; set; } = "LOW";
    public TicketStatus Status { get; private set; } = TicketStatus.OPEN;
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

    public Result<TicketStatus> TransitionTo(
        TicketStatus targetStatus,
        string? resolutionCode = null,
        string? rootCauseSummary = null,
        string? rootCauseCategory = null)
    {
        if (targetStatus == Status)
        {
            return Result<TicketStatus>.Success(Status);
        }

        var guard = TicketTransitionGraph.Guard(Status, targetStatus);
        if (guard.IsFailure)
        {
            return guard;
        }

        var effectiveResolutionCode = resolutionCode ?? ResolutionCode;
        var effectiveRootCauseSummary = rootCauseSummary ?? RootCauseSummary;

        if (targetStatus == TicketStatus.RESOLVED)
        {
            if (string.IsNullOrWhiteSpace(effectiveResolutionCode))
            {
                return DomainError.Validation(
                    "RESOLUTION_CODE_REQUIRED",
                    "Resolution code is required when resolving a ticket.");
            }

            if (string.IsNullOrWhiteSpace(effectiveRootCauseSummary))
            {
                return DomainError.Validation(
                    "ROOT_CAUSE_REQUIRED",
                    "Root cause summary is required when resolving a ticket.");
            }
        }

        if (TicketTransitionGraph.TerminalStates.Contains(targetStatus)
            && string.IsNullOrWhiteSpace(effectiveResolutionCode))
        {
            return DomainError.Validation(
                "RESOLUTION_CODE_REQUIRED",
                "Resolution code is required when transitioning to a terminal state.");
        }

        if (resolutionCode != null)
        {
            ResolutionCode = resolutionCode;
        }

        if (rootCauseSummary != null)
        {
            RootCauseSummary = rootCauseSummary;
        }

        if (rootCauseCategory != null)
        {
            RootCauseCategory = rootCauseCategory;
        }

        Status = targetStatus;
        return Result<TicketStatus>.Success(Status);
    }

    public Result<TicketStatus> TransitionTo(
        string targetStatus,
        string? resolutionCode = null,
        string? rootCauseSummary = null,
        string? rootCauseCategory = null)
    {
        if (!Enum.TryParse<TicketStatus>(targetStatus, ignoreCase: true, out var parsed))
        {
            return DomainError.Validation(
                "INVALID_STATUS",
                $"'{targetStatus}' is not a valid ticket status.");
        }

        return TransitionTo(parsed, resolutionCode, rootCauseSummary, rootCauseCategory);
    }

    public Result<TicketStatus> Resolve(
        string resolutionCode,
        string rootCauseSummary,
        string? rootCauseCategory = null)
    {
        return TransitionTo(TicketStatus.RESOLVED, resolutionCode, rootCauseSummary, rootCauseCategory);
    }

    public static Ticket ReconstructFromRevision(Ticket head, TicketRevision revision) =>
        new()
        {
            Id = head.Id,
            Reference = revision.Reference,
            Title = revision.Title,
            Description = revision.Description,
            Category = revision.Category,
            Priority = revision.Priority,
            Status = revision.Status,
            Reporter = head.Reporter,
            AssigneeUser = revision.AssigneeUser,
            AssigneeRole = revision.AssigneeRole,
            OrgId = head.OrgId,
            SiteId = revision.SiteCode,
            StudyId = revision.StudyCode,
            RelatedEntityType = head.RelatedEntityType,
            RelatedEntityId = head.RelatedEntityId,
            ContextPayload = revision.ContextPayload,
            GxpSeverity = revision.GxpSeverity,
            RootCauseCategory = revision.RootCauseCategory,
            RootCauseSummary = revision.RootCauseSummary,
            ResolutionCode = revision.ResolutionCode,
            DueDate = head.DueDate,
            SlaTargetAt = revision.SlaTargetAt,
            SlaBreached = head.SlaBreached,
            SlaAmberWarned = head.SlaAmberWarned,
            EscalationCount = head.EscalationCount,
            IsDeleted = head.IsDeleted,
            CreatedAt = head.CreatedAt,
            CreatedBy = head.CreatedBy,
            ReasonForChange = revision.ReasonForChange,
            VersionIndex = revision.RevisionNumber,
        };
}

public class TicketRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TicketId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "OTHER";
    public string Priority { get; set; } = "LOW";
    public TicketStatus Status { get; set; } = TicketStatus.OPEN;
    public string GxpSeverity { get; set; } = "NOT_APPLICABLE";
    public string? AssigneeUser { get; set; }
    public string? AssigneeRole { get; set; }
    public string? StudyCode { get; set; }
    public string? SiteCode { get; set; }
    public string? RootCauseCategory { get; set; }
    public string? RootCauseSummary { get; set; }
    public string? ResolutionCode { get; set; }
    public DateTimeOffset? SlaTargetAt { get; set; }
    public string? ContextPayload { get; set; }
    public required DateTimeOffset RecordedAt { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;

    public static TicketRevision FromTicket(Ticket ticket, DateTimeOffset recordedAt, string recordedBy) =>
        new()
        {
            TicketId = ticket.Id,
            RevisionNumber = ticket.VersionIndex,
            Reference = ticket.Reference,
            Title = ticket.Title,
            Description = ticket.Description,
            Category = ticket.Category,
            Priority = ticket.Priority,
            Status = ticket.Status,
            GxpSeverity = ticket.GxpSeverity,
            AssigneeUser = ticket.AssigneeUser,
            AssigneeRole = ticket.AssigneeRole,
            StudyCode = ticket.StudyId,
            SiteCode = ticket.SiteId,
            RootCauseCategory = ticket.RootCauseCategory,
            RootCauseSummary = ticket.RootCauseSummary,
            ResolutionCode = ticket.ResolutionCode,
            SlaTargetAt = ticket.SlaTargetAt,
            ContextPayload = ticket.ContextPayload,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            ReasonForChange = ticket.ReasonForChange,
        };
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
