using System;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
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
    ILogger<EtmfDocumentFilingRequestedConsumer>? logger = null,
    IEtmfCompletenessWebhookPublisher? completenessPublisher = null) : IConsumer<EtmfDocumentFilingRequested>
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
        var createdBy = string.IsNullOrWhiteSpace(msg.RequestedBy) ? "svc:etmf-outbox" : msg.RequestedBy;
        string artifactType = msg.ArtifactCode switch
        {
            "05.02.06" => "Monitoring Visit Correspondence",
            "05.02.04" => "Delegation of Authority Log",
            "05.02.05" => "Informed Consent Form",
            "01.01.01" => "Clinical Trial Protocol",
            _ => msg.ArtifactCode,
        };
        string sourceSystem = msg.DocumentId.StartsWith("DOC-CTMS-", StringComparison.OrdinalIgnoreCase)
            ? "CTMS"
            : "EXECUTION";

        var doc = TmfDocument.CreateFiled(
            id: msg.DocumentId,
            studyId: msg.StudyId,
            filename: msg.DocumentTitle,
            createdBy: createdBy,
            createdAt: msg.RequestedAt.UtcDateTime,
            reasonForChange: "eTMF document filing request outbox sync",
            siteId: msg.SiteId,
            zone: zone,
            section: msg.SectionCode,
            artifactCode: msg.ArtifactCode,
            artifactType: artifactType,
            content: msg.StoragePath,
            mimeType: "application/pdf",
            objectKey: msg.StoragePath,
            idempotencyKey: msg.DocumentId,
            sourceSystem: sourceSystem,
            syncStatus: "SYNCHRONIZED"
        );

        dbContext.TmfDocuments.Add(doc);
        dbContext.TmfAuditLogs.Add(new TmfAuditLog
        {
            Timestamp = msg.RequestedAt,
            UserId = createdBy,
            UserRole = "SYSTEM",
            Action = "FILE_DOCUMENT_EVENT",
            DocumentId = msg.DocumentId,
            Details = $"Filed {msg.DocumentTitle} ({artifactType}) in study {msg.StudyId} Zone {zone}.",
            ReasonForChange = "eTMF document filing request outbox sync",
        });
        await dbContext.SaveChangesAsync(context.CancellationToken);

        if (completenessPublisher is not null && !string.IsNullOrWhiteSpace(msg.SiteId))
        {
            await completenessPublisher.PublishSiteActivationCompletenessAsync(
                dbContext: dbContext,
                studyId: msg.StudyId,
                siteId: msg.SiteId,
                tenantId: null,
                reasonForChange: "eTMF document filing request outbox sync",
                ct: context.CancellationToken);
        }

        logger?.LogInformation("Successfully filed TmfDocument {DocumentId} in eTMF.", msg.DocumentId);
    }
}
