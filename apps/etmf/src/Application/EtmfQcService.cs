using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;
using Cadence.Storage;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Etmf.Application;

public interface IEtmfQcService
{
    Task<IResult> StageQcDocumentAsync(
        StageDocumentQCRequest request,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> GetQcQueueAsync(
        string? studyId,
        string? siteId,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> ReviewQcDocumentAsync(
        string docId,
        CraqcReviewRequest request,
        HttpContext httpContext,
        CancellationToken ct);
}

public sealed class EtmfQcService(
    EtmfDbContext dbContext,
    IEtmfUnitOfWork unitOfWork,
    IClock clock,
    IEtmfCompletenessWebhookPublisher? completenessPublisher = null,
    IStoragePort<byte[]>? storage = null) : IEtmfQcService
{
    public async Task<IResult> StageQcDocumentAsync(
        StageDocumentQCRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        string? reason = EtmfClassificationEngine.ResolveChangeReason(request.ReasonForChange, httpContext);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
        {
            return TypedResults.BadRequest(new { error = "21 CFR Part 11 requires a reason_for_change of at least 10 characters." });
        }

        if (EtmfClassificationEngine.CheckStudyAndOptionalSiteScope(httpContext, request.StudyId, request.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!TmfDocument.IsValidPathSegment(request.Filename))
        {
            return TypedResults.BadRequest(new
            {
                error = "filename must be a single valid file name without path separators, control characters, or '.' / '..' segments."
            });
        }

        if (!EtmfClassificationEngine.TryDecodeDocumentPayload(
            request.ContentBase64,
            request.Content,
            out var payloadBytes,
            out var textExcerpt,
            out var payloadError))
        {
            return TypedResults.BadRequest(new { error = payloadError });
        }

        var now = clock.UtcNow;
        var docId = "DOC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var mimeType = request.MimeType ?? "application/pdf";
        var (objectKey, contentChecksum) = await EtmfClassificationEngine.PersistBinaryPayloadAsync(
            storage,
            request.StudyId,
            docId,
            request.Filename,
            mimeType,
            payloadBytes,
            ct);

        var report = EtmfClassificationEngine.PerformClassification(
            docId: docId,
            filename: request.Filename,
            mimeType: mimeType,
            content: textExcerpt,
            hint: request.ArtifactHint,
            now: now,
            sha256Override: contentChecksum);

        var user = CallerScope.RequiredUserId(httpContext, "An admitted eTMF request has no signed caller.");
        var roles = CallerScope.Roles(httpContext);

        var doc = TmfDocument.CreateForTechnicalQc(
            id: docId,
            studyId: request.StudyId,
            filename: request.Filename,
            createdBy: user,
            actorRole: roles,
            timestamp: now,
            reasonForChange: reason,
            siteId: request.SiteId,
            zone: report.PrimaryClassification.Zone,
            section: report.PrimaryClassification.Section,
            artifactCode: report.PrimaryClassification.ArtifactCode,
            artifactType: report.PrimaryClassification.ArtifactType,
            content: textExcerpt,
            mimeType: mimeType,
            taxonomyVersion: request.TaxonomyVersion ?? "v3.2.0-complete",
            metadataJson: JsonSerializer.Serialize(new
            {
                document_intelligence = report,
                assigned_cra = request.AssignedCra
            }),
            objectKey: objectKey,
            contentChecksum: contentChecksum
        );

        dbContext.TmfDocuments.Add(doc);
        dbContext.TmfDocumentRevisions.Add(TmfDocumentRevision.FromDocument(doc, now, user));
        foreach (var tr in doc.Transitions)
        {
            dbContext.TmfDocumentQcTransitions.Add(tr);
        }

        var audit = new TmfAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = roles,
            Action = "STAGE_QC",
            DocumentId = docId,
            Details = $"Staged document {request.Filename} for CRA QC in study {request.StudyId}.",
            ReasonForChange = reason
        };
        dbContext.TmfAuditLogs.Add(audit);

        await unitOfWork.CommitAsync(ct);

        if (completenessPublisher is not null && !string.IsNullOrWhiteSpace(request.SiteId))
        {
            await completenessPublisher.PublishSiteActivationCompletenessAsync(
                dbContext: dbContext,
                studyId: request.StudyId,
                siteId: request.SiteId,
                tenantId: CallerScope.TenantId(httpContext),
                reasonForChange: reason,
                ct: ct);
        }

        var stagingItem = new CraqcStagingItem(
            DocumentId: docId,
            StudyId: request.StudyId,
            SiteId: request.SiteId,
            Filename: request.Filename,
            Status: "TECHNICAL_QC",
            IntelligenceReport: report,
            StagedAt: now,
            StagedBy: user,
            QcAssignedTo: request.AssignedCra,
            DiscrepancyNotes: report.ConfidenceTier == "LOW" ? ["Low classification confidence score"] : []
        );

        return TypedResults.Created($"/api/v1/etmf/documents/{docId}", stagingItem);
    }

    public async Task<IResult> GetQcQueueAsync(
        string? studyId,
        string? siteId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(studyId) && !ClinicalScopeGuard.CanAccessStudy(httpContext, studyId))
        {
            return ClinicalScopeGuard.Forbidden(ClinicalScopeGuard.StudyAccessDeniedDetail, code: ClinicalScopeGuard.ForbiddenCode);
        }

        var principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (!ClinicalScopeGuard.HasValidSiteScopeIfRequired(principal))
        {
            return ClinicalScopeGuard.Forbidden(ClinicalScopeGuard.SiteAccessDeniedDetail, code: ClinicalScopeGuard.ForbiddenCode);
        }

        string? requestedSiteId = !string.IsNullOrWhiteSpace(siteId)
            ? siteId
            : httpContext.Request.Query["site_id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(requestedSiteId) && !ClinicalScopeGuard.CanAccessSite(httpContext, requestedSiteId))
        {
            return ClinicalScopeGuard.Forbidden(ClinicalScopeGuard.SiteAccessDeniedDetail, code: ClinicalScopeGuard.ForbiddenCode);
        }

        var query = ClinicalScopeGuard.NarrowToStudyScope(dbContext.TmfDocuments.AsNoTracking(), httpContext, d => d.StudyId)
            .Where(d => d.Status == "TECHNICAL_QC");
        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(d => d.StudyId == studyId);
        }

        if (!string.IsNullOrWhiteSpace(requestedSiteId))
        {
            query = query.Where(d => d.SiteId == requestedSiteId);
        }

        var docs = (await query.ToListAsync(ct))
            .Where(d => string.IsNullOrWhiteSpace(d.SiteId) || ClinicalScopeGuard.CanAccessSite(httpContext, d.SiteId))
            .ToList();
        var stagingItems = docs.Select(doc =>
        {
            DocumentIntelligenceReport report;
            try
            {
                if (!string.IsNullOrEmpty(doc.MetadataJson))
                {
                    using var jsonDoc = JsonDocument.Parse(doc.MetadataJson);
                    if (jsonDoc.RootElement.TryGetProperty("document_intelligence", out var intelElem))
                    {
                        report = JsonSerializer.Deserialize<DocumentIntelligenceReport>(intelElem.GetRawText())!;
                    }
                    else
                    {
                        report = EtmfClassificationEngine.PerformClassification(
                            doc.Id, doc.Filename, doc.MimeType, doc.Content, doc.ArtifactType, clock.UtcNow, doc.ContentChecksum);
                    }
                }
                else
                {
                    report = EtmfClassificationEngine.PerformClassification(
                        doc.Id, doc.Filename, doc.MimeType, doc.Content, doc.ArtifactType, clock.UtcNow, doc.ContentChecksum);
                }
            }
            catch
            {
                report = EtmfClassificationEngine.PerformClassification(
                    doc.Id, doc.Filename, doc.MimeType, doc.Content, doc.ArtifactType, clock.UtcNow, doc.ContentChecksum);
            }

            return new CraqcStagingItem(
                DocumentId: doc.Id,
                StudyId: doc.StudyId,
                SiteId: doc.SiteId,
                Filename: doc.Filename,
                Status: doc.Status,
                IntelligenceReport: report,
                StagedAt: new DateTimeOffset(doc.CreatedAt, TimeSpan.Zero),
                StagedBy: doc.CreatedBy,
                QcAssignedTo: null,
                DiscrepancyNotes: report.ConfidenceTier == "LOW" ? ["Low classification confidence score"] : []
            );
        }).ToList();

        return TypedResults.Ok(stagingItems);
    }

    public async Task<IResult> ReviewQcDocumentAsync(
        string docId,
        CraqcReviewRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        string? reason = EtmfClassificationEngine.ResolveChangeReason(request.ReasonForChange, httpContext);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
        {
            return TypedResults.BadRequest(new { error = "21 CFR Part 11 requires a reason_for_change of at least 10 characters." });
        }

        var doc = await dbContext.TmfDocuments.FirstOrDefaultAsync(d => d.Id == docId, ct);
        if (doc == null)
        {
            return TypedResults.NotFound(new { error = $"Document with ID '{docId}' not found." });
        }

        if (EtmfClassificationEngine.CheckStudyAndOptionalSiteScope(httpContext, doc.StudyId, doc.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        var decision = request.Decision.ToUpperInvariant();
        if (decision != "ACCEPT" && decision != "OVERRIDE" && decision != "REJECT")
        {
            return TypedResults.BadRequest(new { error = $"Invalid decision '{request.Decision}'. Allowed values: ACCEPT, OVERRIDE, REJECT." });
        }

        if (!string.Equals(doc.Status, TmfDocumentLifecycleState.TechnicalQc, StringComparison.OrdinalIgnoreCase))
        {
            var allowed = TmfDocument.GetAllowedTransitions(doc.Status);
            var allowedStr = allowed.Count > 0 ? string.Join(", ", allowed) : "None";
            return TypedResults.BadRequest(new
            {
                error = $"Cannot review document with status '{doc.Status}'. Only documents in 'TECHNICAL_QC' status can be reviewed. Allowed transitions from '{doc.Status}': {allowedStr}."
            });
        }

        var now = clock.UtcNow;
        var user = CallerScope.RequiredUserId(httpContext, "An admitted eTMF request has no signed caller.");
        var roles = CallerScope.Roles(httpContext);
        var previousTaxonomy = new EtmfArtifactTaxonomy(doc.ArtifactType, doc.ArtifactCode);

        if (decision == "OVERRIDE" && !string.IsNullOrWhiteSpace(request.OverrideArtifactCode))
        {
            var tax = EtmfClassificationEngine.ResolveTaxonomyByCode(request.OverrideArtifactCode);
            doc.OverrideTaxonomy(tax.Zone, tax.Section, tax.ArtifactType, request.OverrideArtifactCode);
        }

        var currentSeq = await dbContext.TmfDocumentQcTransitions
            .Where(t => t.DocumentId == docId)
            .MaxAsync(t => (int?)t.TransitionSequence, ct) ?? 0;
        var nextSeq = currentSeq + 1;

        TmfDocumentQcTransition transition;
        try
        {
            transition = decision == "REJECT"
                ? doc.Reject(user, roles, reason, now, nextSeq)
                : doc.Approve(user, roles, reason, now, nextSeq);
        }
        catch (InvalidStateTransitionException ex)
        {
            return TypedResults.BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(new { error = ex.Message });
        }

        long baselineVersion = dbContext.Entry(doc).Property(x => x.VersionIndex).OriginalValue;
        doc.VersionIndex = baselineVersion + 1;
        dbContext.TmfDocumentQcTransitions.Add(transition);
        dbContext.TmfDocumentRevisions.Add(TmfDocumentRevision.FromDocument(doc, now, user));

        var audit = new TmfAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = roles,
            Action = $"QC_REVIEW_{decision}",
            DocumentId = docId,
            Details = $"CRA QC Review decision: {decision}. Reason: {reason}",
            ReasonForChange = reason
        };
        dbContext.TmfAuditLogs.Add(audit);

        await unitOfWork.CommitAsync(ct);

        if (completenessPublisher is not null)
        {
            var reviewContext = new EtmfDocumentReviewContext(
                Current: new EtmfArtifactTaxonomy(doc.ArtifactType, doc.ArtifactCode),
                Previous: previousTaxonomy);

            await completenessPublisher.PublishSiteActivationCompletenessAsync(
                dbContext: dbContext,
                studyId: doc.StudyId,
                siteId: doc.SiteId,
                tenantId: CallerScope.TenantId(httpContext),
                reasonForChange: reason,
                reviewContext: reviewContext,
                ct: ct);
        }

        return TypedResults.Ok(doc);
    }
}
