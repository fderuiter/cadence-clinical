using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;
using Cadence.Templating;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Application;

public sealed class LetterTemplateApplicationService(
    CtmsDbContext dbContext,
    CtmsUnitOfWork unitOfWork,
    ITemplateRenderer renderer,
    IClock clock,
    IServiceProvider serviceProvider)
{
    public async Task<IResult> CompileAndDispatchLetterAsync(
        string templateName,
        SiteVisitLetterModel request,
        TemplateCatalog catalog,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!catalog.Contains(templateName))
        {
            return UnknownTemplate(catalog);
        }

        bool hasAuthenticatedCaller = ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null;
        if (hasAuthenticatedCaller
            && CtmsScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (string.IsNullOrWhiteSpace(request.StudyId) || string.IsNullOrWhiteSpace(request.SiteId))
        {
            return TypedResults.Problem(
                title: "Study and site required",
                detail: "Both studyId and siteId are required to compile a visit letter.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string trimmedStudyId = request.StudyId.Trim();
        string trimmedSiteId = request.SiteId.Trim();
        string visitId;

        if (!string.IsNullOrWhiteSpace(request.VisitId))
        {
            string trimmedVisitId = request.VisitId.Trim();
            var visit = await dbContext.CtmsMonitoringVisits.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == trimmedVisitId, ct);
            if (visit is null)
            {
                return TypedResults.Problem(
                    title: "Monitoring visit not found",
                    detail: $"Monitoring visit '{trimmedVisitId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["code"] = "VISIT_NOT_FOUND" });
            }

            if (hasAuthenticatedCaller
                && CtmsScopeGuard.RequireStudyAndSite(httpContext, visit.StudyId, visit.SiteId) is { } visitScopeDenied)
            {
                return visitScopeDenied;
            }

            if (!string.Equals(visit.StudyId, trimmedStudyId, StringComparison.Ordinal)
                || !string.Equals(visit.SiteId, trimmedSiteId, StringComparison.OrdinalIgnoreCase))
            {
                return TypedResults.Problem(
                    title: "Monitoring visit scope mismatch",
                    detail: $"Monitoring visit '{trimmedVisitId}' does not belong to study '{trimmedStudyId}' and site '{trimmedSiteId}'.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["code"] = "VISIT_SCOPE_MISMATCH" });
            }

            visitId = visit.Id;
        }
        else
        {
            visitId = "VISIT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        }

        var user = httpContext.User?.Identity?.Name ?? request.CraId;
        var now = clock.UtcNow;

        var normalizedRequest = request with { StudyId = trimmedStudyId, SiteId = trimmedSiteId, VisitId = visitId };
        var pdfResult = await GxpPdfCompiler.CompileLetterPdfAsync(
            templateName,
            normalizedRequest,
            renderer,
            catalog,
            clock);

        var letterId = "LTR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var objectKey = $"letters/{trimmedStudyId}/{trimmedSiteId}/{letterId}.pdf";

        string? etmfDocId = null;
        DateTimeOffset? dispatchedAt = null;
        string? recipientEmail = null;

        if (pdfResult.IsSigned)
        {
            etmfDocId = await EtmfFilingHelper.FileToEtmfFromServicesAsync(
                normalizedRequest, pdfResult, templateName, letterId, serviceProvider, clock, user, ct);

            recipientEmail = await EmailDispatchHelper.DispatchEmailFromServicesAsync(
                normalizedRequest, pdfResult, templateName, letterId, serviceProvider, clock, user, ct);
            if (recipientEmail is not null)
            {
                dispatchedAt = now;
            }
        }

        var letterStatus = pdfResult.IsSigned ? CtmsGeneratedLetterStatus.Finalized : CtmsGeneratedLetterStatus.Draft;
        var entity = new CtmsGeneratedLetter(letterId, letterStatus)
        {
            StudyId = trimmedStudyId,
            SiteId = trimmedSiteId,
            VisitId = visitId,
            LetterType = templateName,
            RenderedContent = pdfResult.RenderedHtml,
            PdfObjectKey = objectKey,
            PdfSha256Hash = pdfResult.Sha256Hash,
            IsSigned = pdfResult.IsSigned,
            SignatureManifestation = pdfResult.SignatureManifestationJson,
            EtmfDocumentId = etmfDocId,
            DispatchedAt = dispatchedAt,
            RecipientEmail = recipientEmail,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
            ReasonForChange = request.ReasonForChange ?? "Compiled GxP visit letter PDF and executed automated dispatch pipeline."
        };

        dbContext.CtmsGeneratedLetters.Add(entity);

        dbContext.CtmsAuditLogs.Add(new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = "CRA",
            Action = pdfResult.IsSigned ? "FINALIZE_VISIT_LETTER_PDF" : "GENERATE_VISIT_LETTER_DRAFT_PDF",
            Details = $"Compiled visit letter PDF for {templateName} in study {trimmedStudyId} site {trimmedSiteId}. SHA256: {pdfResult.Sha256Hash}. eTMF DocId: {etmfDocId ?? "N/A"}. Status: {entity.Status}.",
            StudyId = trimmedStudyId,
            SiteId = trimmedSiteId,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
            ReasonForChange = request.ReasonForChange ?? "Visit letter PDF compilation and audit trail persistence."
        });

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Ok(new VisitLetterCompilationResultDto(
            LetterId: letterId,
            VisitId: visitId,
            LetterType: templateName,
            PdfSha256Hash: pdfResult.Sha256Hash,
            PdfObjectKey: objectKey,
            IsSigned: pdfResult.IsSigned,
            Status: entity.Status.ToString().ToUpperInvariant(),
            SignatureManifestation: pdfResult.SignatureManifestationJson,
            EtmfDocumentId: etmfDocId,
            DispatchedAt: dispatchedAt,
            PdfAccessUrl: $"/api/v1/ctms/letters/{letterId}/pdf",
            PdfBase64: Convert.ToBase64String(pdfResult.PdfBytes)
        ));
    }

    public async Task<IResult> GetLetterPdfAsync(
        string letterId,
        TemplateCatalog catalog,
        CancellationToken ct,
        HttpContext? httpContext = null)
    {
        bool hasAuthenticatedCaller = httpContext is not null && ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null;
        if (hasAuthenticatedCaller && CtmsScopeGuard.RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return missingScope;
        }

        var letter = await dbContext.CtmsGeneratedLetters.AsNoTracking().FirstOrDefaultAsync(l => l.Id == letterId, ct);
        if (letter is null)
        {
            return TypedResults.NotFound(new { error = $"Letter with ID '{letterId}' not found." });
        }

        string? resolvedStudyId = string.IsNullOrWhiteSpace(letter.StudyId) ? null : letter.StudyId.Trim();
        string? resolvedSiteId = string.IsNullOrWhiteSpace(letter.SiteId) ? null : letter.SiteId.Trim();

        if ((resolvedStudyId is null || resolvedSiteId is null) && !string.IsNullOrWhiteSpace(letter.VisitId))
        {
            var visit = await dbContext.CtmsMonitoringVisits.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == letter.VisitId, ct);
            if (visit is not null)
            {
                resolvedStudyId ??= string.IsNullOrWhiteSpace(visit.StudyId) ? null : visit.StudyId.Trim();
                resolvedSiteId ??= string.IsNullOrWhiteSpace(visit.SiteId) ? null : visit.SiteId.Trim();
            }
        }

        if ((resolvedStudyId is null || resolvedSiteId is null) && !string.IsNullOrWhiteSpace(letter.PdfObjectKey))
        {
            var parts = letter.PdfObjectKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && string.Equals(parts[0], "letters", StringComparison.Ordinal))
            {
                resolvedStudyId ??= string.IsNullOrWhiteSpace(parts[1]) ? null : parts[1].Trim();
                resolvedSiteId ??= string.IsNullOrWhiteSpace(parts[2]) ? null : parts[2].Trim();
            }
        }

        if (hasAuthenticatedCaller
            && CtmsScopeGuard.RequireStudyAndSite(httpContext, resolvedStudyId, resolvedSiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        var model = new SiteVisitLetterModel(
            CraId: letter.CreatedBy,
            CreatedAt: letter.CreatedAt.ToString("yyyy-MM-dd"),
            StudyId: resolvedStudyId ?? "STUDY",
            SiteId: resolvedSiteId ?? "SITE",
            VisitId: letter.VisitId,
            Finalize: letter.IsSigned
        );

        var pdfResult = await GxpPdfCompiler.CompileLetterPdfAsync(
            letter.LetterType,
            model,
            renderer,
            catalog,
            clock);

        return TypedResults.Bytes(pdfResult.PdfBytes, "application/pdf", $"{letter.LetterType}_{letterId}.pdf");
    }

    public static ProblemHttpResult UnknownTemplate(TemplateCatalog catalog) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Unknown template",
            detail: $"templateName must be one of: {string.Join(", ", catalog.Keys.Order(StringComparer.Ordinal))}.",
            extensions: new Dictionary<string, object?> { ["code"] = "TEMPLATE_NOT_ALLOWED" });
}
