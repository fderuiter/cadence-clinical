using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Endpoints;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Templating;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Notifications.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="ParticipantStreakMilestoneAchieved"/> and <see cref="EngagementNudgeTriggered"/> events.
/// Dispatches engagement nudges and records idempotent <see cref="NotificationRecord"/> entries.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public sealed class ParticipantEngagementConsumer(
    NotificationsDbContext dbContext,
    ITemplateRenderer templateRenderer,
    IOptions<NotificationBrandingOptions> branding,
    IClock? clock = null,
    ILogger<ParticipantEngagementConsumer>? logger = null) :
    IConsumer<ParticipantStreakMilestoneAchieved>,
    IConsumer<EngagementNudgeTriggered>
{
    public async Task Consume(ConsumeContext<ParticipantStreakMilestoneAchieved> context)
    {
        var msg = context.Message;

        if (string.IsNullOrWhiteSpace(msg.SubjectId) || string.IsNullOrWhiteSpace(msg.StudyId))
        {
            logger?.LogWarning("Received malformed ParticipantStreakMilestoneAchieved event with empty SubjectId or StudyId.");
            return;
        }

        string relatedEntityId = $"{msg.StudyId}:{msg.SubjectId}:{msg.MilestoneType}:{msg.StreakCount}";

        // Guardrail 2: Maintain strict idempotency by checking RelatedEntityId in NotificationRecords
        bool existing = await dbContext.NotificationRecords
            .AnyAsync(r => r.RelatedEntityId == relatedEntityId && r.RelatedEntityType == "ParticipantEngagement", context.CancellationToken);

        if (existing)
        {
            logger?.LogInformation("Notification record for ParticipantEngagement event {RelatedEntityId} already exists. Skipping duplicate dispatch.", relatedEntityId);
            return;
        }

        DateTimeOffset now = msg.AchievedAt != default ? msg.AchievedAt : (clock ?? SystemClock.Instance).UtcNow;
        var options = branding.Value;

        string celebrationMessage = msg.Message ?? $"Congratulations! You've achieved a {msg.StreakCount}-day adherence streak milestone for study {msg.StudyId}.";

        var renderModel = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["study_id"] = msg.StudyId,
            ["event_id"] = $"evt-gamification-{msg.SubjectId}-{msg.MilestoneType}",
            ["timestamp_utc"] = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["payload"] = new EngagementNudgePayload(
                Title: "Adherence Milestone Achieved!",
                SubjectId: msg.SubjectId,
                CurrentStreak: msg.StreakCount,
                MilestoneType: msg.MilestoneType,
                BonusAmount: msg.BonusAmount > 0 ? msg.BonusAmount.ToString("F2") : null,
                Message: celebrationMessage
            ),
            ["brand_name"] = options.BrandName,
            ["portal_url"] = string.IsNullOrWhiteSpace(options.PortalUrl) ? null : options.PortalUrl,
        };

        string renderedContent = await templateRenderer.RenderAsync(NotificationTemplateEndpoints.Catalog, "engagement_nudge", renderModel);

        var notification = new NotificationRecord
        {
            RecipientUserId = msg.SubjectId,
            RecipientRoleId = "Participant",
            Category = "ParticipantEngagement",
            Priority = "NORMAL",
            Channels = "InApp,SMS,MobilePush",
            MessageContent = renderedContent,
            RelatedEntityId = relatedEntityId,
            RelatedEntityType = "ParticipantEngagement",
            Status = "OPEN",
            DeliveryState = "DELIVERED",
            CreatedAt = now,
            CreatedBy = "svc:gamification-outbox",
            VersionIndex = 1,
            ReasonForChange = $"Milestone nudge for subject {msg.SubjectId}, milestone {msg.MilestoneType} ({msg.StreakCount} days)"
        };

        dbContext.NotificationRecords.Add(notification);

        foreach (var ch in new[] { "InApp", "SMS", "MobilePush" })
        {
            dbContext.NotificationDeliveries.Add(new NotificationDelivery
            {
                NotificationId = notification.Id,
                Channel = ch,
                Status = "DELIVERED",
                Attempts = 1,
                CompletedAt = now,
                RetryEligible = false
            });
        }

        dbContext.NotificationAuditLogs.Add(new NotificationAuditLog
        {
            Timestamp = now,
            UserId = "svc:gamification-outbox",
            UserRole = "System",
            Action = "PARTICIPANT_ENGAGEMENT_NUDGE_DISPATCH",
            Details = $"Dispatched engagement nudge for subject {msg.SubjectId}, milestone {msg.MilestoneType}, streak {msg.StreakCount}"
        });

        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger?.LogInformation("Successfully processed ParticipantStreakMilestoneAchieved event for subject {SubjectId}, milestone {MilestoneType}.", msg.SubjectId, msg.MilestoneType);
    }

    public async Task Consume(ConsumeContext<EngagementNudgeTriggered> context)
    {
        var msg = context.Message;

        if (string.IsNullOrWhiteSpace(msg.SubjectId) || string.IsNullOrWhiteSpace(msg.StudyId))
        {
            logger?.LogWarning("Received malformed EngagementNudgeTriggered event with empty SubjectId or StudyId.");
            return;
        }

        string relatedEntityId = $"{msg.StudyId}:{msg.SubjectId}:{msg.NudgeType}:{msg.CurrentStreak}:{msg.TriggeredAt.Ticks}";

        // Guardrail 2: Maintain strict idempotency by checking RelatedEntityId in NotificationRecords
        bool existing = await dbContext.NotificationRecords
            .AnyAsync(r => r.RelatedEntityId == relatedEntityId && r.RelatedEntityType == "EngagementNudge", context.CancellationToken);

        if (existing)
        {
            logger?.LogInformation("Notification record for EngagementNudge event {RelatedEntityId} already exists. Skipping duplicate dispatch.", relatedEntityId);
            return;
        }

        DateTimeOffset now = msg.TriggeredAt != default ? msg.TriggeredAt : (clock ?? SystemClock.Instance).UtcNow;
        var options = branding.Value;

        var renderModel = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["study_id"] = msg.StudyId,
            ["event_id"] = $"evt-nudge-{msg.SubjectId}-{msg.NudgeType}",
            ["timestamp_utc"] = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["payload"] = new EngagementNudgePayload(
                Title: "Daily Engagement Reminder",
                SubjectId: msg.SubjectId,
                CurrentStreak: msg.CurrentStreak,
                MilestoneType: null,
                BonusAmount: null,
                Message: msg.Message
            ),
            ["brand_name"] = options.BrandName,
            ["portal_url"] = string.IsNullOrWhiteSpace(options.PortalUrl) ? null : options.PortalUrl,
        };

        string renderedContent = await templateRenderer.RenderAsync(NotificationTemplateEndpoints.Catalog, "engagement_nudge", renderModel);

        var notification = new NotificationRecord
        {
            RecipientUserId = msg.SubjectId,
            RecipientRoleId = "Participant",
            Category = "ParticipantEngagement",
            Priority = "NORMAL",
            Channels = "InApp,MobilePush",
            MessageContent = renderedContent,
            RelatedEntityId = relatedEntityId,
            RelatedEntityType = "EngagementNudge",
            Status = "OPEN",
            DeliveryState = "DELIVERED",
            CreatedAt = now,
            CreatedBy = "svc:gamification-outbox",
            VersionIndex = 1,
            ReasonForChange = $"Engagement nudge for subject {msg.SubjectId}, nudge {msg.NudgeType}"
        };

        dbContext.NotificationRecords.Add(notification);

        dbContext.NotificationAuditLogs.Add(new NotificationAuditLog
        {
            Timestamp = now,
            UserId = "svc:gamification-outbox",
            UserRole = "System",
            Action = "PARTICIPANT_ENGAGEMENT_NUDGE_DISPATCH",
            Details = $"Dispatched engagement nudge for subject {msg.SubjectId}, type {msg.NudgeType}"
        });

        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger?.LogInformation("Successfully processed EngagementNudgeTriggered event for subject {SubjectId}, nudge {NudgeType}.", msg.SubjectId, msg.NudgeType);
    }
}

/// <summary>
/// Payload model for Liquid rendering of engagement nudges.
/// </summary>
public sealed record EngagementNudgePayload(
    string Title,
    string SubjectId,
    int CurrentStreak,
    string? MilestoneType,
    string? BonusAmount,
    string Message
);
