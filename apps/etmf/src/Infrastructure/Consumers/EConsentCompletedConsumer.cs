using System;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Kernel.Time;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Etmf.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer in eTMF service for <see cref="EConsentCompleted"/> events.
/// Synchronizes consent completion records into the eTMF filing ledger idempotently.
/// @req:PRD-SYS-001
/// </summary>
public sealed class EConsentCompletedConsumer(
    EtmfDbContext dbContext,
    IClock? clock = null,
    ILogger<EConsentCompletedConsumer>? logger = null) : IConsumer<EConsentCompleted>
{
    private readonly IClock _clock = clock ?? SystemClock.Instance;

    public async Task Consume(ConsumeContext<EConsentCompleted> context)
    {
        var msg = context.Message;

        if (string.IsNullOrWhiteSpace(msg.SubjectId) || string.IsNullOrWhiteSpace(msg.StudyId))
        {
            logger?.LogWarning("Received malformed EConsentCompleted event in eTMF with empty SubjectId or StudyId.");
            return;
        }

        var key = $"{msg.SubjectId}:{msg.StudyId}:{msg.VersionIndex}";

        // Idempotency check: verify whether document for this consent completion already exists
        var existing = await dbContext.TmfDocuments
            .AnyAsync(d => d.IdempotencyKey == key || d.CorrelationKey == key, context.CancellationToken);

        if (existing)
        {
            logger?.LogInformation("eTMF document for eConsent completion key {Key} already exists. Skipping duplicate filing.", key);
            return;
        }

        var effectiveTimestamp = msg.SignedAt ?? _clock.UtcNow;

        var docId = Guid.NewGuid().ToString();
        var doc = new TmfDocument(docId)
        {
            StudyId = msg.StudyId,
            SiteId = msg.SiteId,
            Zone = 5, // Zone 05: Informed Consent in TMF Reference Model
            Section = "05.01",
            ArtifactType = "05.01.01",
            ArtifactCode = "ICF",
            Filename = $"eConsent_{msg.SubjectId}_{msg.VersionTag}.pdf",
            Content = $"s3://etmf/{msg.StudyId}/{msg.SubjectId}/icf_{msg.VersionTag}.pdf",
            ObjectKey = $"etmf/{msg.StudyId}/{msg.SubjectId}/icf_{msg.VersionTag}.pdf",
            Status = "FILED",
            ApprovalStatus = "APPROVED",
            IdempotencyKey = key,
            CorrelationKey = key,
            SourceSystem = "ECONSENT",
            SyncStatus = "SYNCHRONIZED",
            SigningTimestamp = msg.SignedAt,
            ProtocolVersionTag = msg.VersionTag,
            ProtocolVersionIndex = msg.VersionIndex,
            CreatedAt = effectiveTimestamp.UtcDateTime,
            CreatedBy = "svc:etmf-econsent-consumer",
            ReasonForChange = "EConsent completed filing sync"
        };

        dbContext.TmfDocuments.Add(doc);
        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger?.LogInformation("Successfully created eTMF document {DocumentId} for completed eConsent key {Key}.", docId, key);
    }
}
