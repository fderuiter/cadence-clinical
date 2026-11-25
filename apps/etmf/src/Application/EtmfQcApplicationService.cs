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

using static Cadence.Apps.Etmf.Application.EtmfClassificationService;

namespace Cadence.Apps.Etmf.Application;

/// <summary>
/// Application service owning eTMF AI-classified QC staging, CRA QC queue queries,
/// QC review state transitions, and draft-to-QC submissions.
/// </summary>
public sealed class EtmfQcApplicationService(
    EtmfUnitOfWork uow,
    IClock clock,
    IEtmfCompletenessWebhookPublisher? completenessPublisher = null,
    IStoragePort<byte[]>? storage = null)
{
    public async Task<IResult> StageQcDocumentAsync(
        StageDocumentQCRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        string? reason = ResolveChangeReason(request.ReasonForChange, httpContext);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
        {
            return TypedResults.BadRequest(new { error = "21 CFR Part 11 requires a reason_for_change of at least 10 characters." });
        }

        if (CheckStudyAndOptionalSiteScope(httpContext, request.StudyId, request.SiteId) is { } scopeDenied)
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

        if (!TryDecodeDocumentPayload(request.ContentBase64, request.Content, out var payloadBytes, out var textExcerpt, out var payloadError))
        {
            return TypedResults.BadRequest(new { error = payloadError });
        }

        var now = clock.UtcNow;
        var docId = "DOC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var mimeType = request.MimeType ?? "application/pdf";
        var (objectKey, contentChecksum) = await PersistBinaryPayloadAsync(
            storage,
            request.StudyId,
            docId,
            request.Filename,
            mimeType,
            payloadBytes,
            ct);

        var report = PerformClassification(
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

        uow.Context.TmfDocuments.Add(doc);
        uow.Context.TmfDocumentRevisions.Add(TmfDocumentRevision.FromDocument(doc, now, user));
        foreach (var tr in doc.Transitions)
        {
            uow.Context.TmfDocumentQcTransitions.Add(tr);
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
        uow.Context.TmfAuditLogs.Add(audit);

        await uow.CommitAsync(ct);

        if (completenessPublisher is not null && !string.IsNullOrWhiteSpace(request.SiteId))
        {
            await completenessPublisher.PublishSiteActivationCompletenessAsync(
                dbContext: uow.Context,
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
        string? studyCode,
        HttpContext httpContext,
        CancellationToken ct,
        string? siteCode = null)
    {
        if (!string.IsNullOrWhiteSpace(studyCode) && !ClinicalScopeGuard.CanAccessStudy(httpContext, studyCode))
        {
            return ClinicalScopeGuard.Forbidden(ClinicalScopeGuard.StudyAccessDeniedDetail, code: ClinicalScopeGuard.ForbiddenCode);
        }

        var principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (!ClinicalScopeGuard.HasValidSiteScopeIfRequired(principal))
        {
            return ClinicalScopeGuard.Forbidden(ClinicalScopeGuard.SiteAccessDeniedDetail, code: ClinicalScopeGuard.ForbiddenCode);
        }

        string? requestedSiteId = !string.IsNullOrWhiteSpace(siteCode)
            ? siteCode
            : httpContext.Request.Query["site_id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(requestedSiteId) && !ClinicalScopeGuard.CanAccessSite(httpContext, requestedSiteId))
        {
            return ClinicalScopeGuard.Forbidden(ClinicalScopeGuard.SiteAccessDeniedDetail, code: ClinicalScopeGuard.ForbiddenCode);
        }

        var query = ClinicalScopeGuard.NarrowToStudyScope(uow.Context.TmfDocuments.AsNoTracking(), httpContext, d => d.StudyId)
            .Where(d => d.Status == "TECHNICAL_QC");
        if (!string.IsNullOrWhiteSpace(studyCode))
        {
            query = query.Where(d => d.StudyId == studyCode);
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
                        report = PerformClassification(doc.Id, doc.Filename, doc.MimeType, doc.Content, doc.ArtifactType, clock.UtcNow, doc.ContentChecksum);
                    }
                }
                else
                {
                    report = PerformClassification(doc.Id, doc.Filename, doc.MimeType, doc.Content, doc.ArtifactType, clock.UtcNow, doc.ContentChecksum);
                }
            }
            catch
            {
                report = PerformClassification(doc.Id, doc.Filename, doc.MimeType, doc.Content, doc.ArtifactType, clock.UtcNow, doc.ContentChecksum);
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
        string? reason = ResolveChangeReason(request.ReasonForChange, httpContext);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
        {
            return TypedResults.BadRequest(new { error = "21 CFR Part 11 requires a reason_for_change of at least 10 characters." });
        }

        var doc = await uow.Context.TmfDocuments.FirstOrDefaultAsync(d => d.Id == docId, ct);
        if (doc == null)
        {
            return TypedResults.NotFound(new { error = $"Document with ID '{docId}' not found." });
        }

        if (CheckStudyAndOptionalSiteScope(httpContext, doc.StudyId, doc.SiteId) is { } scopeDenied)
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

        if (decision == "OVERRIDE" && !string.IsNullOrWhiteSpace(request.OverrideArtifactCode))
        {
            var tax = ResolveTaxonomyByCode(request.OverrideArtifactCode);
            doc.OverrideTaxonomy(tax.Zone, tax.Section, tax.ArtifactType, request.OverrideArtifactCode);
        }

        var currentSeq = await uow.Context.TmfDocumentQcTransitions
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

        long baselineVersion = uow.Context.Entry(doc).Property(x => x.VersionIndex).OriginalValue;
        doc.VersionIndex = baselineVersion + 1;
        uow.Context.TmfDocumentQcTransitions.Add(transition);
        uow.Context.TmfDocumentRevisions.Add(TmfDocumentRevision.FromDocument(doc, now, user));

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
        uow.Context.TmfAuditLogs.Add(audit);

        await uow.CommitAsync(ct);

        if (completenessPublisher is not null)
        {
            await completenessPublisher.PublishSiteActivationCompletenessAsync(
                dbContext: uow.Context,
                studyId: doc.StudyId,
                siteId: doc.SiteId,
                tenantId: CallerScope.TenantId(httpContext),
                reasonForChange: reason,
                ct: ct);
        }

        return TypedResults.Ok(doc);
    }

    public async Task<IResult> SubmitDocumentForQcAsync(
        string documentId,
        SubmitDocumentForQcRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        string? reason = ResolveChangeReason(request.ReasonForChange, httpContext);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
        {
            return TypedResults.BadRequest(new { error = "21 CFR Part 11 requires a reason_for_change of at least 10 characters." });
        }

        var doc = await uow.Context.TmfDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null)
        {
            return TypedResults.NotFound(new { error = $"Document with ID '{documentId}' not found." });
        }

        if (CheckStudyAndOptionalSiteScope(httpContext, doc.StudyId, doc.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!string.Equals(doc.Status, TmfDocumentLifecycleState.Draft, StringComparison.OrdinalIgnoreCase))
        {
            var allowed = TmfDocument.GetAllowedTransitions(doc.Status);
            var allowedStr = allowed.Count > 0 ? string.Join(", ", allowed) : "None";
            return TypedResults.BadRequest(new
            {
                error = $"Cannot submit document with status '{doc.Status}' for Technical QC. Only 'DRAFT' documents can be submitted. Allowed transitions from '{doc.Status}': {allowedStr}."
            });
        }

        var now = clock.UtcNow;
        var user = CallerScope.RequiredUserId(httpContext, "An admitted eTMF request has no signed caller.");
        var roles = CallerScope.Roles(httpContext);

        var currentSeq = await uow.Context.TmfDocumentQcTransitions
            .Where(t => t.DocumentId == documentId)
            .MaxAsync(t => (int?)t.TransitionSequence, ct) ?? 0;

        long baselineVersion = uow.Context.Entry(doc).Property(x => x.VersionIndex).OriginalValue;
        var transition = doc.StageForQc(user, roles, reason, now, currentSeq + 1);
        uow.Context.TmfDocumentQcTransitions.Add(transition);

        var report = PerformClassification(
            docId: doc.Id,
            filename: doc.Filename,
            mimeType: doc.MimeType,
            content: doc.Content,
            hint: doc.ArtifactType,
            now: now,
            sha256Override: doc.ContentChecksum);
        doc.MetadataJson = JsonSerializer.Serialize(new
        {
            document_intelligence = report,
            assigned_cra = request.AssignedCra
        });
        doc.VersionIndex = baselineVersion + 1;
        uow.Context.TmfDocumentRevisions.Add(TmfDocumentRevision.FromDocument(doc, now, user));

        uow.Context.TmfAuditLogs.Add(new TmfAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = roles,
            Action = "SUBMIT_TECHNICAL_QC",
            DocumentId = documentId,
            Details = $"Submitted draft document {doc.Filename} to TECHNICAL_QC.",
            ReasonForChange = reason
        });

        await uow.CommitAsync(ct);

        if (completenessPublisher is not null && !string.IsNullOrWhiteSpace(doc.SiteId))
        {
            await completenessPublisher.PublishSiteActivationCompletenessAsync(
                dbContext: uow.Context,
                studyId: doc.StudyId,
                siteId: doc.SiteId,
                tenantId: CallerScope.TenantId(httpContext),
                reasonForChange: reason,
                ct: ct);
        }

        return TypedResults.Ok(doc);
    }
}
