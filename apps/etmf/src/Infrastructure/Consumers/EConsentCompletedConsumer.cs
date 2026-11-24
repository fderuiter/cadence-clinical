using System;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Database.Audit;
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
        using var _ = AmbientAuditActorScope.BeginScope(
            "svc:etmf-econsent-consumer",
            "EConsent completed filing sync");

        var docId = Guid.NewGuid().ToString();
        var safeStudyId = TmfDocument.SanitizePathSegment(msg.StudyId, "study");
        var safeSubjectId = TmfDocument.SanitizePathSegment(msg.SubjectId, "subject");
        var safeVersionTag = TmfDocument.SanitizePathSegment(msg.VersionTag, "v1");
        var safeFilename = TmfDocument.SanitizePathSegment($"eConsent_{safeSubjectId}_{safeVersionTag}.pdf", "eConsent.pdf");

        var doc = TmfDocument.CreateFiled(
            id: docId,
            studyId: msg.StudyId,
            filename: safeFilename,
            createdBy: "svc:etmf-econsent-consumer",
            createdAt: effectiveTimestamp.UtcDateTime,
            reasonForChange: "EConsent completed filing sync",
            siteId: msg.SiteId,
            zone: 5, // Zone 05: Informed Consent in TMF Reference Model
            section: "05.01",
            artifactCode: "ICF",
            artifactType: "05.01.01",
            content: $"s3://etmf/{safeStudyId}/{safeSubjectId}/icf_{safeVersionTag}.pdf",
            objectKey: $"etmf/{safeStudyId}/{safeSubjectId}/icf_{safeVersionTag}.pdf",
            idempotencyKey: key,
            correlationKey: key,
            sourceSystem: "ECONSENT",
            syncStatus: "SYNCHRONIZED",
            signingTimestamp: msg.SignedAt,
            protocolVersionTag: msg.VersionTag,
            protocolVersionIndex: msg.VersionIndex
        );

        dbContext.TmfDocuments.Add(doc);
        dbContext.TmfDocumentRevisions.Add(
            TmfDocumentRevision.FromDocument(doc, effectiveTimestamp, "svc:etmf-econsent-consumer"));
        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger?.LogInformation("Successfully created eTMF document {DocumentId} for completed eConsent key {Key}.", docId, key);
    }
}
