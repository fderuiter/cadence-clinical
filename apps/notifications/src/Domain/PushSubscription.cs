using System;

namespace Cadence.Apps.Notifications.Domain;

/// <summary>
/// Web Push subscription details for a participant device.
/// </summary>
public class PushSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string SubjectId { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string P256dh { get; set; } = string.Empty;

    public string Auth { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string? UserAgent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? UpdatedAt { get; set; }

    public int VersionIndex { get; set; } = 1;

    public string ReasonForChange { get; set; } = string.Empty;
}

/// <summary>
/// DTO for push subscription registration request from portal.
/// </summary>
public record PushSubscriptionRequest(
    string SubjectId,
    string Endpoint,
    string P256dh,
    string Auth,
    string? UserAgent = null);

/// <summary>
/// Web push notification payload sent to device service workers.
/// </summary>
public record WebPushNotificationPayload(
    string Title,
    string Body,
    string? Icon = null,
    string? Badge = null,
    string? TargetUrl = null,
    string? AssignmentId = null,
    string? QuestionnaireId = null,
    string? Tag = null);
