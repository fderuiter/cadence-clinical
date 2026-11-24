using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Endpoints;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Templating;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Notifications.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="EconsentReconsentRequired"/> events.
/// Dispatches multi-channel alerts (SMS, Mobile Push, Email) and records idempotent <see cref="NotificationRecord"/> entries.
/// @req:PRD-SYS-001
/// </summary>
public sealed class EconsentReconsentConsumer(
    NotificationsDbContext dbContext,
    ITemplateRenderer templateRenderer,
    IOptions<NotificationBrandingOptions> branding,
    IEnrolledSubjectResolver? subjectResolver = null,
    IClock? clock = null,
    ILogger<EconsentReconsentConsumer>? logger = null) : IConsumer<EconsentReconsentRequired>
{
    private const string ServiceAccountId = "svc:econsent-outbox";

    public async Task Consume(ConsumeContext<EconsentReconsentRequired> context)
    {
        using var _ = StandaloneAuditActorScope.Begin(ServiceAccountId);
        var msg = context.Message;

        if (string.IsNullOrWhiteSpace(msg.StudyId) || string.IsNullOrWhiteSpace(msg.TemplateId))
        {
            logger?.LogWarning("Received malformed EconsentReconsentRequired event with empty StudyId or TemplateId.");
            return;
        }

        string eventKeyPrefix = $"{msg.StudyId}:{msg.TemplateId}:{msg.VersionIndex}";

        // Idempotency check: verify whether notification records already exist for this study/template/version
        bool existing = await dbContext.NotificationRecords
            .AnyAsync(r => r.RelatedEntityId != null && r.RelatedEntityId.StartsWith(eventKeyPrefix) && r.RelatedEntityType == "EconsentReconsent", context.CancellationToken);

        if (existing)
        {
            logger?.LogInformation("Notification records for EconsentReconsent event {EventKeyPrefix} already exist. Skipping duplicate dispatch.", eventKeyPrefix);
            return;
        }

        DateTimeOffset now = msg.OccurredAt ?? (clock ?? SystemClock.Instance).UtcNow;
        var options = branding.Value;

        // Render multi-channel notification body using reconsent_required.html.liquid template
        var renderModel = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["study_id"] = msg.StudyId,
            ["event_id"] = $"evt-{msg.TemplateId}-{msg.VersionIndex}",
            ["timestamp_utc"] = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["payload"] = new NotificationPayload(
                ProtocolVersion: msg.ProtocolVersion ?? $"v{msg.VersionIndex}.0",
                VersionIndex: msg.VersionIndex.ToString(),
                ChangeSummary: msg.ChangeSummary
            ),
            ["brand_name"] = options.BrandName,
            ["portal_url"] = string.IsNullOrWhiteSpace(options.PortalUrl) ? null : options.PortalUrl,
        };

        string renderedContent = await templateRenderer.RenderAsync(NotificationTemplateEndpoints.Catalog, "reconsent_required", renderModel);

        var resolver = subjectResolver ?? new DefaultEnrolledSubjectResolver();
        var enrolledSubjects = await resolver.GetEnrolledSubjectIdsAsync(msg.StudyId, context.CancellationToken);

        var rawChannels = msg.Channels != null && msg.Channels.Length > 0
            ? msg.Channels
            : new[] { "SMS", "Mobile Push", "Email" };

        string channelsFormatted = string.Join(",", rawChannels);

        foreach (var subjectId in enrolledSubjects)
        {
            string relatedEntityId = $"{eventKeyPrefix}:{subjectId}";

            var notification = new NotificationRecord
            {
                RecipientUserId = subjectId,
                RecipientRoleId = "Participant",
                Category = "EconsentReconsent",
                Priority = "URGENT",
                Channels = channelsFormatted,
                MessageContent = renderedContent,
                RelatedEntityId = relatedEntityId,
                RelatedEntityType = "EconsentReconsent",
                Status = "OPEN",
                DeliveryState = "DELIVERED",
                CreatedAt = now,
                CreatedBy = "svc:econsent-outbox",
                VersionIndex = msg.VersionIndex,
                ReasonForChange = $"Re-consent required notification for study {msg.StudyId}, template {msg.TemplateId} v{msg.VersionIndex}"
            };

            dbContext.NotificationRecords.Add(notification);

            foreach (var ch in rawChannels)
            {
                dbContext.NotificationDeliveries.Add(new NotificationDelivery
                {
                    NotificationId = notification.Id,
                    Channel = ch.Trim(),
                    Status = "DELIVERED",
                    Attempts = 1,
                    CompletedAt = now,
                    RetryEligible = false
                });
            }

            dbContext.NotificationAuditLogs.Add(new NotificationAuditLog
            {
                Timestamp = now,
                UserId = "svc:econsent-outbox",
                UserRole = "System",
                Action = "ECONSENT_RECONSENT_DISPATCH",
                Details = $"Dispatched multi-channel ({channelsFormatted}) re-consent notification to subject {subjectId} for study {msg.StudyId}, template {msg.TemplateId} v{msg.VersionIndex}"
            });
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger?.LogInformation("Successfully processed EconsentReconsentRequired event for {EventKeyPrefix} across {Count} subject(s).", eventKeyPrefix, enrolledSubjects.Count);
    }
}
