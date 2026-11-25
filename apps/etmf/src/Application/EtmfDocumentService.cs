using System;
using System.Linq;
using System.Text;
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

public interface IEtmfDocumentService
{
    Task<IResult> GetDocumentsAsync(
        string? studyId,
        int? zone,
        string? siteId,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> GetDocumentByIdAsync(
        string documentId,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> DownloadDocumentContentAsync(
        string documentId,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> SubmitDocumentForQcAsync(
        string documentId,
        SubmitDocumentForQcRequest request,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> UpdateDocumentTaxonomyAsync(
        string documentId,
        UpdateDocumentTaxonomyRequest request,
        HttpContext httpContext,
        CancellationToken ct);

    Task<IResult> IngestDocumentAsync(
        IngestDocumentRequest request,
        HttpContext httpContext,
        CancellationToken ct);
}

public sealed class EtmfDocumentService(
    EtmfDbContext dbContext,
    IEtmfUnitOfWork unitOfWork,
    IClock clock,
    IStoragePort<byte[]>? storage = null,
    IEtmfCompletenessWebhookPublisher? completenessPublisher = null) : IEtmfDocumentService
{
    public async Task<IResult> GetDocumentsAsync(
        string? studyId,
        int? zone,
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

        var query = ClinicalScopeGuard.NarrowToStudyScope(dbContext.TmfDocuments.AsNoTracking(), httpContext, d => d.StudyId);
        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(d => d.StudyId == studyId);
        }

        if (!string.IsNullOrWhiteSpace(requestedSiteId))
        {
            query = query.Where(d => d.SiteId == requestedSiteId);
        }

        if (zone.HasValue)
        {
            query = query.Where(d => d.Zone == zone.Value);
        }

        var docs = (await query.ToListAsync(ct))
            .Where(d => string.IsNullOrWhiteSpace(d.SiteId) || ClinicalScopeGuard.CanAccessSite(httpContext, d.SiteId))
            .ToList();
        return TypedResults.Ok(docs);
    }

    public async Task<IResult> GetDocumentByIdAsync(
        string documentId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var doc = await dbContext.TmfDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, doc.StudyId, doc.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return TypedResults.NotFound(new { error = $"Document '{documentId}' not found." });
        }

        return TypedResults.Ok(doc);
    }

    public async Task<IResult> DownloadDocumentContentAsync(
        string documentId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var doc = await dbContext.TmfDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null)
        {
            return TypedResults.NotFound(new { error = $"Document '{documentId}' not found." });
        }

        if (EtmfClassificationEngine.CheckStudyAndOptionalSiteScope(httpContext, doc.StudyId, doc.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        byte[]? bytes = null;
        string? verifiedSha256 = null;

        if (storage is not null && !string.IsNullOrWhiteSpace(doc.ObjectKey))
        {
            try
            {
                (bytes, verifiedSha256) = await storage.GetObjectAsync(doc.ObjectKey, ct);
            }
            catch (StorageIntegrityException ex)
            {
                return TypedResults.Problem(
                    title: "Document integrity check failed",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (StorageObjectNotFoundException)
            {
                bytes = null;
            }
        }

        if (bytes is null)
        {
            if (string.IsNullOrEmpty(doc.Content))
            {
                return TypedResults.NotFound(new { error = $"Stored binary content for document '{documentId}' was not found." });
            }

            byte[] fallbackBytes = Encoding.UTF8.GetBytes(doc.Content);
            string fallbackSha256 = EtmfClassificationEngine.ComputeSha256(fallbackBytes);
            if (!string.IsNullOrWhiteSpace(doc.ContentChecksum)
                && !string.Equals(doc.ContentChecksum, fallbackSha256, StringComparison.OrdinalIgnoreCase))
            {
                return TypedResults.NotFound(new { error = $"Stored binary content for document '{documentId}' is unavailable." });
            }

            bytes = fallbackBytes;
            verifiedSha256 = fallbackSha256;
        }
        else if (!string.IsNullOrWhiteSpace(doc.ContentChecksum)
            && !string.Equals(doc.ContentChecksum, verifiedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(
                title: "Document integrity check failed",
                detail: $"Stored SHA-256 digest '{verifiedSha256}' does not match recorded ContentChecksum '{doc.ContentChecksum}'.",
                statusCode: StatusCodes.Status409Conflict);
        }

        verifiedSha256 ??= EtmfClassificationEngine.ComputeSha256(bytes);
        httpContext.Response.Headers["X-Content-SHA256"] = verifiedSha256;
        httpContext.Response.Headers.CacheControl = "no-store";

        var mimeType = string.IsNullOrWhiteSpace(doc.MimeType) ? "application/pdf" : doc.MimeType;
        return TypedResults.File(bytes, mimeType);
    }

    public async Task<IResult> SubmitDocumentForQcAsync(
        string documentId,
        SubmitDocumentForQcRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        string? reason = EtmfClassificationEngine.ResolveChangeReason(request.ReasonForChange, httpContext);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
        {
            return TypedResults.BadRequest(new { error = "21 CFR Part 11 requires a reason_for_change of at least 10 characters." });
        }

        var doc = await dbContext.TmfDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null)
        {
            return TypedResults.NotFound(new { error = $"Document with ID '{documentId}' not found." });
        }

        if (EtmfClassificationEngine.CheckStudyAndOptionalSiteScope(httpContext, doc.StudyId, doc.SiteId) is { } scopeDenied)
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

        var currentSeq = await dbContext.TmfDocumentQcTransitions
            .Where(t => t.DocumentId == documentId)
            .MaxAsync(t => (int?)t.TransitionSequence, ct) ?? 0;

        long baselineVersion = dbContext.Entry(doc).Property(x => x.VersionIndex).OriginalValue;
        var transition = doc.StageForQc(user, roles, reason, now, currentSeq + 1);
        dbContext.TmfDocumentQcTransitions.Add(transition);

        var report = EtmfClassificationEngine.PerformClassification(
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
        dbContext.TmfDocumentRevisions.Add(TmfDocumentRevision.FromDocument(doc, now, user));

        dbContext.TmfAuditLogs.Add(new TmfAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = roles,
            Action = "SUBMIT_TECHNICAL_QC",
            DocumentId = documentId,
            Details = $"Submitted draft document {doc.Filename} to TECHNICAL_QC.",
            ReasonForChange = reason
        });

        await unitOfWork.CommitAsync(ct);

        if (completenessPublisher is not null && !string.IsNullOrWhiteSpace(doc.SiteId))
        {
            await completenessPublisher.PublishSiteActivationCompletenessAsync(
                dbContext: dbContext,
                studyId: doc.StudyId,
                siteId: doc.SiteId,
                tenantId: CallerScope.TenantId(httpContext),
                reasonForChange: reason,
                ct: ct);
        }

        return TypedResults.Ok(doc);
    }

    public async Task<IResult> UpdateDocumentTaxonomyAsync(
        string documentId,
        UpdateDocumentTaxonomyRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        string? reason = EtmfClassificationEngine.ResolveChangeReason(request.ReasonForChange, httpContext);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
        {
            return TypedResults.BadRequest(new { error = "21 CFR Part 11 requires a reason_for_change of at least 10 characters." });
        }

        var doc = await dbContext.TmfDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null)
        {
            return TypedResults.NotFound(new { error = $"Document with ID '{documentId}' not found." });
        }

        if (EtmfClassificationEngine.CheckStudyAndOptionalSiteScope(httpContext, doc.StudyId, doc.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        int targetZone = request.Zone ?? doc.Zone;
        string targetSection = !string.IsNullOrWhiteSpace(request.Section) ? request.Section.Trim() : doc.Section;
        string targetArtifactCode = !string.IsNullOrWhiteSpace(request.ArtifactCode) ? request.ArtifactCode.Trim() : doc.ArtifactCode;
        string targetArtifactType = !string.IsNullOrWhiteSpace(request.ArtifactType) ? request.ArtifactType.Trim() : doc.ArtifactType;

        if (!string.IsNullOrWhiteSpace(request.ArtifactCode))
        {
            var resolved = EtmfClassificationEngine.ResolveTaxonomyByCode(targetArtifactCode);
            if (request.Zone is null && string.IsNullOrWhiteSpace(request.Section))
            {
                targetZone = resolved.Zone;
                targetSection = resolved.Section;
            }

            if (string.IsNullOrWhiteSpace(request.ArtifactType)
                && (!string.Equals(resolved.ArtifactType, "Custom Artifact", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(targetArtifactType)))
            {
                targetArtifactType = resolved.ArtifactType;
            }
        }

        if (targetZone <= 0 || !TmfDocument.IsValidPathSegment(targetSection) || string.IsNullOrWhiteSpace(targetArtifactType))
        {
            return TypedResults.BadRequest(new { error = "Valid zone, section, and artifact_type are required for taxonomy updates." });
        }

        long baselineVersion = dbContext.Entry(doc).Property(x => x.VersionIndex).OriginalValue;
        var previous = $"Z{doc.Zone}/{doc.Section}/{doc.ArtifactCode} ({doc.ArtifactType})";
        doc.OverrideTaxonomy(targetZone, targetSection, targetArtifactType, targetArtifactCode);
        doc.ReasonForChange = reason;
        doc.VersionIndex = baselineVersion + 1;

        var now = clock.UtcNow;
        var user = CallerScope.RequiredUserId(httpContext, "An admitted eTMF request has no signed caller.");
        var roles = CallerScope.Roles(httpContext);
        dbContext.TmfDocumentRevisions.Add(TmfDocumentRevision.FromDocument(doc, now, user));

        dbContext.TmfAuditLogs.Add(new TmfAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = roles,
            Action = "TAXONOMY_UPDATE",
            DocumentId = documentId,
            Details = $"Updated taxonomy from {previous} to Z{doc.Zone}/{doc.Section}/{doc.ArtifactCode} ({doc.ArtifactType}).",
            ReasonForChange = reason
        });

        await unitOfWork.CommitAsync(ct);

        if (completenessPublisher is not null && !string.IsNullOrWhiteSpace(doc.SiteId))
        {
            await completenessPublisher.PublishSiteActivationCompletenessAsync(
                dbContext: dbContext,
                studyId: doc.StudyId,
                siteId: doc.SiteId,
                tenantId: CallerScope.TenantId(httpContext),
                reasonForChange: reason,
                ct: ct);
        }

        return TypedResults.Ok(doc);
    }

    public async Task<IResult> IngestDocumentAsync(
        IngestDocumentRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StudyId))
        {
            return TypedResults.BadRequest(new { error = "study_id is required: every document is filed in a study." });
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

        if (request.Section is not null && !TmfDocument.IsValidPathSegment(request.Section))
        {
            return TypedResults.BadRequest(new
            {
                error = "section must be a single valid taxonomy section segment without path separators, control characters, or '.' / '..' segments."
            });
        }

        string effectiveReason = EtmfClassificationEngine.ResolveChangeReason(request.ReasonForChange, httpContext) ?? "Initial filing";
        if (effectiveReason.Length < 10)
        {
            return TypedResults.BadRequest(new { error = "21 CFR Part 11 requires a reason_for_change of at least 10 characters." });
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
        var user = CallerScope.RequiredUserId(httpContext, "An admitted eTMF request has no signed caller.");
        var roles = CallerScope.Roles(httpContext);
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

        var doc = TmfDocument.CreateDraft(
            id: docId,
            studyId: request.StudyId,
            filename: request.Filename,
            createdBy: user,
            createdAt: now.UtcDateTime,
            reasonForChange: effectiveReason,
            siteId: request.SiteId,
            zone: request.Zone > 0 ? request.Zone : 1,
            section: request.Section ?? "01.01",
            artifactCode: request.ArtifactCode ?? "01.01.01",
            artifactType: request.ArtifactType ?? "Clinical Trial Protocol",
            content: textExcerpt,
            mimeType: mimeType,
            taxonomyVersion: request.TaxonomyVersion ?? "v3.2.0-complete",
            objectKey: objectKey,
            contentChecksum: contentChecksum
        );

        dbContext.TmfDocuments.Add(doc);
        dbContext.TmfDocumentRevisions.Add(TmfDocumentRevision.FromDocument(doc, now, user));
        dbContext.TmfAuditLogs.Add(new TmfAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = roles,
            Action = "INGEST_DRAFT",
            DocumentId = docId,
            Details = $"Ingested draft document {request.Filename} in study {request.StudyId} (SHA-256: {contentChecksum}).",
            ReasonForChange = effectiveReason
        });
        await unitOfWork.CommitAsync(ct);

        if (completenessPublisher is not null && !string.IsNullOrWhiteSpace(request.SiteId))
        {
            await completenessPublisher.PublishSiteActivationCompletenessAsync(
                dbContext: dbContext,
                studyId: request.StudyId,
                siteId: request.SiteId,
                tenantId: CallerScope.TenantId(httpContext),
                reasonForChange: effectiveReason,
                ct: ct);
        }

        return TypedResults.Created($"/api/v1/etmf/documents/{docId}", doc);
    }
}
