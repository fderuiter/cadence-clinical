using System;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Contracts;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cadence.Apps.Etmf.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="EtmfDocumentFilingRequested"/> events.
/// Processes eTMF document filing requests idempotently.
/// @req:PRD-SYS-001
/// </summary>
public sealed partial class EtmfDocumentFilingRequestedConsumer(
    EtmfDbContext dbContext,
    ILogger<EtmfDocumentFilingRequestedConsumer>? logger = null) : IConsumer<EtmfDocumentFilingRequested>
{
    private readonly ILogger _log = (ILogger?)logger ?? NullLogger.Instance;

    public async Task Consume(ConsumeContext<EtmfDocumentFilingRequested> context)
    {
        var msg = context.Message;

        if (string.IsNullOrWhiteSpace(msg.DocumentId) || string.IsNullOrWhiteSpace(msg.StudyId))
        {
            LogMalformedEvent(_log);
            return;
        }

        // Idempotency check: verify whether TmfDocument already exists for this DocumentId
        var existing = await dbContext.TmfDocuments
            .AnyAsync(d => d.Id == msg.DocumentId || d.IdempotencyKey == msg.DocumentId, context.CancellationToken);

        if (existing)
        {
            LogDuplicateFilingSkipped(_log, msg.DocumentId);
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

        LogFiled(_log, msg.DocumentId);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Received malformed EtmfDocumentFilingRequested event with empty DocumentId or StudyId.")]
    private static partial void LogMalformedEvent(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "TmfDocument {DocumentId} already exists in eTMF. Skipping duplicate filing.")]
    private static partial void LogDuplicateFilingSkipped(ILogger logger, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully filed TmfDocument {DocumentId} in eTMF.")]
    private static partial void LogFiled(ILogger logger, string documentId);
}
