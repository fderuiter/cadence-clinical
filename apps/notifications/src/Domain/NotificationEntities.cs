using System;

namespace Cadence.Apps.Notifications.Domain;

public class NotificationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? RecipientUserId { get; set; }
    public string? RecipientRoleId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Channels { get; set; } = "IN_APP";
    public string MessageContent { get; set; } = string.Empty;
    public string? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
    public string Status { get; set; } = "OPEN";
    public string DeliveryState { get; set; } = "PENDING";
    public int Retries { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class NotificationAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public class NotificationDelivery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NotificationId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool RetryEligible { get; set; } = true;
}

public record NotificationPayload(
    string SubjectId = "",
    string AdverseEventDescription = "",
    string FlagReason = "",
    string DiscrepancySource = "",
    string ProtocolVersion = "",
    string VersionNumber = "",
    string ChangeSummary = "",
    string SummaryOfChanges = "",
    string SiteId = "",
    string FormName = "",
    string FieldName = "",
    string QueryMessage = "",
    string DiaryName = "",
    string DueDate = "",
    string AmendmentTag = "",
    string VersionIndex = "",
    string ChangeReason = "",
    string SubmittedBy = "",
    string DocumentName = "",
    string ArtifactCode = "",
    string ExpirationDate = ""
);

public record NotificationRenderModel(
    string StudyId,
    string EventId,
    string TimestampUtc,
    NotificationPayload Payload,
    string BrandName = "Cadence Clinical Platform",
    string? PortalUrl = null
);
