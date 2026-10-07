using System;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Contracts;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Notifications.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="ClinicalQueryRaised"/> events.
/// Dispatches notifications and creates an idempotent <see cref="NotificationRecord"/>.
/// @req:PRD-SYS-001
/// </summary>
public sealed class ClinicalQueryRaisedConsumer(
    NotificationsDbContext dbContext,
    ILogger<ClinicalQueryRaisedConsumer>? logger = null) : IConsumer<ClinicalQueryRaised>
{
    public async Task Consume(ConsumeContext<ClinicalQueryRaised> context)
    {
        var msg = context.Message;

        if (string.IsNullOrWhiteSpace(msg.QueryId) || string.IsNullOrWhiteSpace(msg.StudyId))
        {
            logger?.LogWarning("Received malformed ClinicalQueryRaised event with empty QueryId or StudyId.");
            return;
        }

        // Idempotency check: verify whether notification record already exists for this query
        var existing = await dbContext.NotificationRecords
            .AnyAsync(r => r.RelatedEntityId == msg.QueryId && r.RelatedEntityType == "ClinicalQuery", context.CancellationToken);

        if (existing)
        {
            logger?.LogInformation("Notification record for ClinicalQuery {QueryId} already exists. Skipping duplicate dispatch.", msg.QueryId);
            return;
        }

        var notification = new NotificationRecord
        {
            Category = "ClinicalQuery",
            Priority = string.IsNullOrWhiteSpace(msg.Priority) ? "HIGH" : msg.Priority,
            Channels = "IN_APP,EMAIL",
            MessageContent = $"Clinical Query {msg.QueryId} raised for subject {msg.SubjectId} on test {msg.TestCode}: {msg.Explanation}",
            RelatedEntityId = msg.QueryId,
            RelatedEntityType = "ClinicalQuery",
            Status = "OPEN",
            DeliveryState = "DELIVERED",
            CreatedAt = msg.RaisedAt,
            CreatedBy = string.IsNullOrWhiteSpace(msg.RaisedBy) ? "svc:notifications-outbox" : msg.RaisedBy,
            ReasonForChange = "Clinical query raised outbox notification"
        };

        dbContext.NotificationRecords.Add(notification);
        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger?.LogInformation("Successfully processed ClinicalQueryRaised notification for QueryId {QueryId}.", msg.QueryId);
    }
}
