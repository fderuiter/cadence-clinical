using System;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Contracts;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Etmf.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="EtmfDocumentFilingRequested"/> events.
/// Processes eTMF document filing requests idempotently.
/// @req:PRD-SYS-001
/// </summary>
public sealed class EtmfDocumentFilingRequestedConsumer(
    EtmfDbContext dbContext,
    ILogger<EtmfDocumentFilingRequestedConsumer>? logger = null) : IConsumer<EtmfDocumentFilingRequested>
{
    public async Task Consume(ConsumeContext<EtmfDocumentFilingRequested> context)
    {
        var msg = context.Message;

        if (string.IsNullOrWhiteSpace(msg.DocumentId) || string.IsNullOrWhiteSpace(msg.StudyId))
        {
            logger?.LogWarning("Received malformed EtmfDocumentFilingRequested event with empty DocumentId or StudyId.");
            return;
        }

        // Idempotency check: verify whether TmfDocument already exists for this DocumentId
        var existing = await dbContext.TmfDocuments
            .AnyAsync(d => d.Id == msg.DocumentId || d.IdempotencyKey == msg.DocumentId, context.CancellationToken);

        if (existing)
        {
            logger?.LogInformation("TmfDocument {DocumentId} already exists in eTMF. Skipping duplicate filing.", msg.DocumentId);
            return;
        }

        int zone = int.TryParse(msg.ZoneCode, out var parsedZone) ? parsedZone : 1;

        var doc = new TmfDocument(msg.DocumentId)
        {
            StudyId = msg.StudyId,
            SiteId = msg.SiteId,
            Zone = zone,
            Section = msg.SectionCode,
            ArtifactType = msg.ArtifactCode,
            ArtifactCode = msg.ArtifactCode,
            Filename = msg.DocumentTitle,
            Content = msg.StoragePath,
            ObjectKey = msg.StoragePath,
            Status = "FILED",
            ApprovalStatus = "APPROVED",
            IdempotencyKey = msg.DocumentId,
            SourceSystem = "EXECUTION",
            SyncStatus = "SYNCHRONIZED",
            CreatedAt = msg.RequestedAt.UtcDateTime,
            CreatedBy = string.IsNullOrWhiteSpace(msg.RequestedBy) ? "svc:etmf-outbox" : msg.RequestedBy,
            ReasonForChange = "eTMF document filing request outbox sync"
        };

        dbContext.TmfDocuments.Add(doc);
        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger?.LogInformation("Successfully filed TmfDocument {DocumentId} in eTMF.", msg.DocumentId);
    }
}
