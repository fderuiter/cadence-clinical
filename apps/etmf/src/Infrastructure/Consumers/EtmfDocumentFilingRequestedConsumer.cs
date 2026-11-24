using System;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Database.Audit;

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

        int zone = int.TryParse(msg.ZoneCode, out var parsedZone) && parsedZone > 0 ? parsedZone : 1;
        var createdBy = string.IsNullOrWhiteSpace(msg.RequestedBy) ? "svc:etmf-outbox" : msg.RequestedBy;
        using var _ = AmbientAuditActorScope.BeginScope(
            "svc:etmf-outbox",
            "eTMF document filing request outbox sync");
        var safeFilename = TmfDocument.SanitizePathSegment(msg.DocumentTitle, "document.pdf");
        var safeSection = TmfDocument.SanitizePathSegment(msg.SectionCode, "01.01");

        var doc = TmfDocument.CreateFiled(
            id: msg.DocumentId,
            studyId: msg.StudyId,
            filename: safeFilename,
            createdBy: createdBy,
            createdAt: msg.RequestedAt.UtcDateTime,
            reasonForChange: "eTMF document filing request outbox sync",
            siteId: msg.SiteId,
            zone: zone,
            section: safeSection,
            artifactCode: msg.ArtifactCode,
            artifactType: msg.ArtifactCode,
            content: msg.StoragePath,
            mimeType: "application/pdf",
            objectKey: msg.StoragePath,
            idempotencyKey: msg.DocumentId,
            sourceSystem: "EXECUTION",
            syncStatus: "SYNCHRONIZED"
        );

        dbContext.TmfDocuments.Add(doc);
        dbContext.TmfDocumentRevisions.Add(
            TmfDocumentRevision.FromDocument(doc, msg.RequestedAt, createdBy));
        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger?.LogInformation("Successfully filed TmfDocument {DocumentId} in eTMF.", msg.DocumentId);
    }
}
