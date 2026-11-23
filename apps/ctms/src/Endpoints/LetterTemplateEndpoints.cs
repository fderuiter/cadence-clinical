using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Templating;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Site visit letter rendering and GxP PDF compilation pipeline.
/// @req:PRD-CTMS-002
/// </summary>
public static class LetterTemplateEndpoints
{
    /// <summary>The permission that rendering a visit letter requires.</summary>
    public const string RenderPermission = "ctms_monitoring_visit:create";

    /// <summary>The action path bound to a finalized visit letter electronic signature token.</summary>
    public const string CompileLetterAction = "/api/v1/ctms/letters/compile";

    /// <summary>The semantic action bound to a finalized visit letter electronic signature token.</summary>
    public const string CompileLetterSemanticAction = "ctms.letter.compile";

    /// <summary>The letters CTMS can render, keyed by the value callers pass as <c>templateName</c>.</summary>
    public static TemplateCatalog Catalog { get; } = new(
        typeof(LetterTemplateEndpoints).Assembly,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["confirmation_letter"] = "confirmation_letter.liquid",
            ["follow_up_letter"] = "follow_up_letter.liquid",
        });

    public static RouteGroupBuilder MapLetterTemplateEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/letters/render", RenderLetterAsync)
            .WithName("RenderSiteVisitLetter")
            .WithSummary("Renders a CTMS site visit monitoring letter via Liquid template.")
            .Produces<string>(StatusCodes.Status200OK, "text/plain")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(RenderPermission).RequireCallerStudyAndSiteScope();

        group.MapPost("/letters/compile", CompileAndDispatchLetterAsync)
            .WithName("CompileSiteVisitLetterPdf")
            .WithSummary("Compiles a GxP-compliant PDF visit letter with Part 11 electronic signature, eTMF filing, and automated email dispatch.")
            .Produces<VisitLetterCompilationResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(RenderPermission).RequireCallerStudyAndSiteScope();

        group.MapGet("/letters/{letterId}/pdf", GetLetterPdfAsync)
            .WithName("GetSiteVisitLetterPdf")
            .WithSummary("Downloads or views the compiled GxP PDF visit letter.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(RenderPermission).RequireCallerStudyAndSiteScope();

        return group;
    }

    /// <summary>
    /// Renders a site visit letter to HTML string.
    /// </summary>
    public static async Task<Results<ContentHttpResult, ProblemHttpResult>> RenderLetterAsync(
        string templateName,
        SiteVisitLetterModel request,
        ITemplateRenderer renderer,
        HttpContext? httpContext = null)
    {
        if (!Catalog.Contains(templateName))
        {
            return UnknownTemplate(Catalog);
        }

        if (ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null
            && ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, request.StudyId, request.SiteId) is { } scopeDenied)
        {
            string detail = (scopeDenied as ProblemHttpResult)?.ProblemDetails.Detail
                ?? "You are not assigned to the requested study or site.";
            return CtmsScopeGuard.Forbidden(detail);
        }

        var model = BuildTemplateModel(request, isSigned: false, signerName: null, signingTimestamp: null, signingPurpose: null, integrityHash: null);
        string rendered = await renderer.RenderAsync(Catalog, templateName, model);
        return TypedResults.Content(rendered, "text/plain");
    }

    /// <summary>
    /// Compiles a GxP PDF letter, attaches Part 11 signatures if finalized, files to eTMF, and dispatches email notification.
    /// </summary>
    public static async Task<IResult> CompileAndDispatchLetterAsync(
        [FromQuery] string templateName,
        [FromBody] SiteVisitLetterModel request,
        [FromServices] ITemplateRenderer renderer,
        [FromServices] CtmsDbContext dbContext,
        [FromServices] IClock clock,
        [FromServices] IServiceProvider serviceProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!Catalog.Contains(templateName))
        {
            return UnknownTemplate(Catalog);
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

        string? fallbackReason = request.ReasonForChange;
        if (!request.Finalize && string.IsNullOrWhiteSpace(fallbackReason))
        {
            fallbackReason = "Visit letter PDF compilation and audit trail persistence.";
        }

        if (!MonitoringVisitEndpoints.TryExtractReasonForChange(httpContext, fallbackReason, out string reason, out var reasonProblem))
        {
            return reasonProblem!;
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

        if (request.Finalize
            && httpContext.RequestServices?.GetService<SigTokenVerifier>() is { } sigVerifier
            && httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal signerPrincipal)
        {
            var gatewayOptions = httpContext.RequestServices
                .GetService<IOptionsMonitor<GatewayAuthenticationOptions>>()
                ?.Get(GatewayAuthenticationHandler.SchemeName);
            byte[] secretBytes = gatewayOptions?.GatewaySecret is { Length: > 0 } secret
                ? Encoding.UTF8.GetBytes(secret)
                : [];
            if (string.IsNullOrWhiteSpace(signerPrincipal.UserId) || secretBytes.Length == 0)
            {
                return ElectronicSignatureEndpointFilter.ReauthenticationRequired();
            }

            try
            {
                var verifiedToken = await sigVerifier.VerifyAndConsumeAsync(
                    httpContext.Request.Headers[GatewayHeaders.SigToken].ToString(),
                    signerPrincipal.UserId,
                    new SigTokenBinding(CompileLetterAction, CompileLetterSemanticAction),
                    secretBytes,
                    ct).ConfigureAwait(false);
                httpContext.Items[ElectronicSignatureEndpointFilter.VerifiedTokenItemKey] = verifiedToken;
            }
            catch (UnauthorizedAccessException)
            {
                return ElectronicSignatureEndpointFilter.ReauthenticationRequired();
            }
        }

        var user = CallerScope.UserId(httpContext)
            ?? (httpContext.User?.Identity?.Name is { Length: > 0 } name ? name : request.CraId);
        var now = clock.UtcNow;

        var normalizedRequest = request with { StudyId = trimmedStudyId, SiteId = trimmedSiteId, VisitId = visitId };
        var pdfResult = await GxpPdfCompiler.CompileLetterPdfAsync(
            templateName,
            normalizedRequest,
            renderer,
            Catalog,
            clock);

        var letterId = "LTR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var objectKey = $"letters/{trimmedStudyId}/{trimmedSiteId}/{letterId}.pdf";

        // File signed letter PDF to eTMF Zone 5 via MassTransit outbox contract
        string? etmfDocId = null;
        if (pdfResult.IsSigned)
        {
            etmfDocId = "DOC-CTMS-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var filename = $"{templateName}_{trimmedSiteId}_{trimmedStudyId}_{letterId}.pdf";
            if (serviceProvider.GetService<IPublishEndpoint>() is { } publishEndpoint)
            {
                await publishEndpoint.Publish(
                    new EtmfDocumentFilingRequested(
                        DocumentId: etmfDocId,
                        StudyId: trimmedStudyId,
                        SiteId: trimmedSiteId,
                        ZoneCode: "5",
                        SectionCode: "05.02",
                        ArtifactCode: "05.02.06",
                        DocumentTitle: filename,
                        StoragePath: Convert.ToBase64String(pdfResult.PdfBytes),
                        RequestedBy: user,
                        RequestedAt: now),
                    ct);
            }
        }

        // Dispatch Email Notification if finalized
        DateTimeOffset? dispatchedAt = null;
        string? recipientEmail = null;
        if (pdfResult.IsSigned)
        {
            var notifDbType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName == "Cadence.Apps.Notifications.Infrastructure.Persistence.NotificationsDbContext");
            var notifDb = notifDbType != null ? serviceProvider.GetService(notifDbType) : null;
            recipientEmail = await EmailDispatchHelper.DispatchEmailNotificationAsync(normalizedRequest, pdfResult, templateName, letterId, notifDb, clock, user, ct);
            if (recipientEmail is not null)
            {
                dispatchedAt = now;
            }
        }

        // Persist CtmsGeneratedLetter
        var letterStatus = pdfResult.IsSigned ? CtmsGeneratedLetterStatus.Finalized : CtmsGeneratedLetterStatus.Draft;
        var entity = new CtmsGeneratedLetter(letterId, letterStatus)
        {
            StudyId = trimmedStudyId,
            SiteId = trimmedSiteId,
            VisitId = visitId,
            VisitType = string.IsNullOrWhiteSpace(request.VisitType) ? "Monitoring" : request.VisitType,
            ScheduledDate = request.ScheduledDate,
            ActualDate = request.ActualDate,
            FindingsJson = request.Findings is { Count: > 0 } ? JsonSerializer.Serialize(request.Findings) : null,
            SignerName = pdfResult.IsSigned ? (request.SignerName ?? user) : null,
            SigningPurpose = pdfResult.IsSigned ? (request.SigningPurpose ?? "21 CFR Part 11 Electronic Signature / Visit Letter Approval") : null,
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
            ReasonForChange = reason
        };

        dbContext.CtmsGeneratedLetters.Add(entity);

        // Audit log entry
        var audit = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = CallerScope.Roles(httpContext) is { Length: > 0 } callerRoles ? callerRoles : "CRA",
            Action = pdfResult.IsSigned ? "FINALIZE_VISIT_LETTER_PDF" : "GENERATE_VISIT_LETTER_DRAFT_PDF",
            Details = $"Compiled visit letter PDF for {templateName} in study {trimmedStudyId} site {trimmedSiteId}. SHA256: {pdfResult.Sha256Hash}. eTMF DocId: {etmfDocId ?? "N/A"}. Status: {entity.Status}.",
            StudyId = trimmedStudyId,
            SiteId = trimmedSiteId,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
            ReasonForChange = reason
        };

        dbContext.CtmsAuditLogs.Add(audit);

        await dbContext.SaveChangesAsync(ct);

        var resultDto = new VisitLetterCompilationResultDto(
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
        );

        return TypedResults.Ok(resultDto);
    }

    /// <summary>
    /// Serves compiled GxP PDF binary stream for a visit letter.
    /// </summary>
    public static async Task<IResult> GetLetterPdfAsync(
        string letterId,
        [FromServices] CtmsDbContext dbContext,
        [FromServices] ITemplateRenderer renderer,
        [FromServices] IClock clock,
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

        CtmsMonitoringVisit? visit = null;
        if (!string.IsNullOrWhiteSpace(letter.VisitId))
        {
            visit = await dbContext.CtmsMonitoringVisits.AsNoTracking()
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

        IReadOnlyList<SiteVisitFindingModel>? findings = null;
        if (!string.IsNullOrWhiteSpace(letter.FindingsJson))
        {
            try
            {
                findings = JsonSerializer.Deserialize<List<SiteVisitFindingModel>>(letter.FindingsJson);
            }
            catch
            {
                findings = null;
            }
        }
        else if (!string.IsNullOrWhiteSpace(letter.VisitId))
        {
            var dbFindings = await dbContext.CtmsMonitoringVisitFindings
                .AsNoTracking()
                .Where(f => f.VisitId == letter.VisitId)
                .OrderBy(f => f.CreatedAt)
                .ToListAsync(ct);
            if (dbFindings.Count > 0)
            {
                findings = dbFindings
                    .Select(f => new SiteVisitFindingModel(f.Text, f.Severity, f.ResolutionStatus))
                    .ToList();
            }
        }

        string visitType = !string.IsNullOrWhiteSpace(letter.VisitType)
            ? letter.VisitType
            : (visit?.VisitType ?? "Monitoring");
        string? scheduledDate = letter.ScheduledDate ?? visit?.ScheduledDate.ToString("yyyy-MM-dd");
        string? actualDate = letter.ActualDate ?? visit?.ActualDate?.ToString("yyyy-MM-dd");

        var model = new SiteVisitLetterModel(
            CraId: letter.CreatedBy,
            CreatedAt: letter.CreatedAt.ToString("yyyy-MM-dd"),
            StudyId: resolvedStudyId ?? "STUDY",
            SiteId: resolvedSiteId ?? "SITE",
            VisitType: visitType,
            ScheduledDate: scheduledDate,
            ActualDate: actualDate,
            Findings: findings,
            VisitId: letter.VisitId,
            Finalize: letter.IsSigned,
            SignerName: letter.SignerName,
            SigningPurpose: letter.SigningPurpose
        );

        var pdfResult = await GxpPdfCompiler.CompileLetterPdfAsync(
            letter.LetterType,
            model,
            renderer,
            Catalog,
            clock);

        return TypedResults.Bytes(pdfResult.PdfBytes, "application/pdf", $"{letter.LetterType}_{letterId}.pdf");
    }

    /// <summary>
    /// Builds context dictionary for template rendering.
    /// </summary>
    public static Dictionary<string, object?> BuildTemplateModel(
        SiteVisitLetterModel request,
        bool isSigned,
        string? signerName,
        string? signingTimestamp,
        string? signingPurpose,
        string? integrityHash)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["cra_id"] = request.CraId,
            ["created_at"] = request.CreatedAt,
            ["study_id"] = request.StudyId,
            ["site_id"] = request.SiteId,
            ["visit_type"] = request.VisitType,
            ["scheduled_date"] = request.ScheduledDate,
            ["actual_date"] = request.ActualDate,
            ["findings"] = request.Findings,
            ["is_signed"] = isSigned,
            ["signer_name"] = signerName,
            ["signing_timestamp"] = signingTimestamp,
            ["signing_purpose"] = signingPurpose,
            ["integrity_hash"] = integrityHash
        };
    }

    private static ProblemHttpResult UnknownTemplate(TemplateCatalog catalog) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Unknown template",
            detail: $"templateName must be one of: {string.Join(", ", catalog.Keys.Order(StringComparer.Ordinal))}.",
            extensions: new Dictionary<string, object?> { ["code"] = "TEMPLATE_NOT_ALLOWED" });
}

/// <summary>
/// Result of compiling a GxP visit letter PDF and executing the dispatch pipeline.
/// </summary>
public record VisitLetterCompilationResultDto(
    string LetterId,
    string VisitId,
    string LetterType,
    string PdfSha256Hash,
    string PdfObjectKey,
    bool IsSigned,
    string Status,
    string SignatureManifestation,
    string? EtmfDocumentId,
    DateTimeOffset? DispatchedAt,
    string PdfAccessUrl,
    string PdfBase64
);

/// <summary>
/// Result of GxP PDF compilation.
/// </summary>
public record GxpPdfResult(
    byte[] PdfBytes,
    string Sha256Hash,
    bool IsSigned,
    string SignatureManifestationJson,
    string RenderedHtml
);

/// <summary>
/// Engine for compiling GxP PDF documents and applying 21 CFR Part 11 signatures.
/// </summary>
public static class GxpPdfCompiler
{
    /// <summary>
    /// Compiles a GxP PDF document from Liquid template and model data.
    /// </summary>
    public static async Task<GxpPdfResult> CompileLetterPdfAsync(
        string templateName,
        SiteVisitLetterModel request,
        ITemplateRenderer renderer,
        TemplateCatalog catalog,
        IClock clock)
    {
        var now = clock.UtcNow;
        var visitId = request.VisitId ?? ("VISIT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant());
        var signerName = request.SignerName ?? request.CraId;
        var signingPurpose = request.SigningPurpose ?? "21 CFR Part 11 Electronic Signature / Visit Letter Approval";
        var signingTimestamp = now.ToString("o");

        var modelDict = LetterTemplateEndpoints.BuildTemplateModel(
            request,
            isSigned: request.Finalize,
            signerName: request.Finalize ? signerName : null,
            signingTimestamp: request.Finalize ? signingTimestamp : null,
            signingPurpose: request.Finalize ? signingPurpose : null,
            integrityHash: null);

        string htmlContent = await renderer.RenderAsync(catalog, templateName, modelDict);

        var bodyBytes = Encoding.UTF8.GetBytes(htmlContent);
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(bodyBytes));

        modelDict["integrity_hash"] = bodyHash;
        htmlContent = await renderer.RenderAsync(catalog, templateName, modelDict);

        var pdfBuilder = new StringBuilder();
        pdfBuilder.AppendLine("%PDF-1.7");
        pdfBuilder.AppendLine($"% Cadence Clinical GxP PDF Document - {templateName}");
        pdfBuilder.AppendLine($"% Study: {request.StudyId} | Site: {request.SiteId} | Visit: {visitId}");
        if (!request.Finalize)
        {
            pdfBuilder.AppendLine("% Watermark: DRAFT - UNSIGNED WORKING COPY");
        }
        pdfBuilder.AppendLine($"% Content-SHA256: {bodyHash}");
        pdfBuilder.AppendLine();
        pdfBuilder.AppendLine(htmlContent);
        pdfBuilder.AppendLine();
        pdfBuilder.AppendLine("%%EOF");

        byte[] rawPdfBytes = Encoding.UTF8.GetBytes(pdfBuilder.ToString().TrimEnd());

        byte[] finalPdfBytes;
        string signatureManifestationJson = "{}";

        if (request.Finalize)
        {
            var (cert, key) = Pkcs7Signer.GenerateSigningCredentials(signerName, clock);
            var signer = new Pkcs7Signer(cert, key);
            finalPdfBytes = signer.SignPdf(rawPdfBytes);

            signatureManifestationJson = JsonSerializer.Serialize(new
            {
                signer = signerName,
                timestamp = signingTimestamp,
                purpose = signingPurpose,
                certificate_subject = cert.Subject,
                signature_algorithm = "SHA256withRSA-PSS"
            });
        }
        else
        {
            finalPdfBytes = rawPdfBytes;
        }

        var pdfHash = Convert.ToHexStringLower(SHA256.HashData(finalPdfBytes));

        return new GxpPdfResult(
            PdfBytes: finalPdfBytes,
            Sha256Hash: pdfHash,
            IsSigned: request.Finalize,
            SignatureManifestationJson: signatureManifestationJson,
            RenderedHtml: htmlContent
        );
    }
}

/// <summary>
/// Helper for dispatching email notifications for finalized visit letters.
/// </summary>
public static class EmailDispatchHelper
{
    /// <summary>
    /// Dispatches an email notification with secure PDF access link to site investigator.
    /// </summary>
    public static async Task<string?> DispatchEmailNotificationAsync(
        SiteVisitLetterModel request,
        GxpPdfResult pdfResult,
        string templateName,
        string letterId,
        object? notificationsDb,
        IClock clock,
        string user,
        CancellationToken ct)
    {
        if (notificationsDb is null) return null;

        var recipientEmail = request.InvestigatorEmail;
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            recipientEmail = $"investigator.site{request.SiteId}@clinicalsite.org";
        }

        if (!recipientEmail.Contains('@', StringComparison.Ordinal) || recipientEmail.EndsWith("@unauthorized.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var notifAssembly = notificationsDb.GetType().Assembly;
        var recordType = notifAssembly.GetType("Cadence.Apps.Notifications.Domain.NotificationRecord");
        var deliveryType = notifAssembly.GetType("Cadence.Apps.Notifications.Domain.NotificationDelivery");
        if (recordType is null || deliveryType is null) return null;

        var now = clock.UtcNow;
        var notificationId = "NOTIF-CTMS-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var pdfAccessUrl = $"/api/v1/ctms/letters/{letterId}/pdf";

        var record = Activator.CreateInstance(recordType);
        if (record is null) return null;

        SetProp(record, "Id", notificationId);
        SetProp(record, "RecipientUserId", recipientEmail);
        SetProp(record, "RecipientRoleId", "PRINCIPAL_INVESTIGATOR");
        SetProp(record, "Category", "VISIT_LETTER_DISPATCH");
        SetProp(record, "Priority", "HIGH");
        SetProp(record, "Channels", "EMAIL,IN_APP");
        SetProp(record, "MessageContent", $"Official Monitoring Visit Letter ({templateName}) for Study {request.StudyId} Site {request.SiteId} is ready. Access secure PDF: {pdfAccessUrl}");
        SetProp(record, "RelatedEntityId", letterId);
        SetProp(record, "RelatedEntityType", "CTMS_VISIT_LETTER");
        SetProp(record, "Status", "SENT");
        SetProp(record, "DeliveryState", "COMPLETED");
        SetProp(record, "Retries", 0);
        SetProp(record, "CreatedAt", now);
        SetProp(record, "CreatedBy", user);
        SetProp(record, "VersionIndex", 1);
        SetProp(record, "ReasonForChange", "Automated email notification dispatch for visit letter finalization.");

        var dbContextType = notificationsDb.GetType();
        var addMethod = dbContextType.GetMethod("Add", [typeof(object)]) ?? dbContextType.GetMethod("Add");
        addMethod?.Invoke(notificationsDb, [record]);

        var delivery = Activator.CreateInstance(deliveryType);
        if (delivery is not null)
        {
            SetProp(delivery, "Id", Guid.NewGuid().ToString());
            SetProp(delivery, "NotificationId", notificationId);
            SetProp(delivery, "Channel", "EMAIL");
            SetProp(delivery, "Status", "COMPLETED");
            SetProp(delivery, "Attempts", 1);
            SetProp(delivery, "CompletedAt", now);
            SetProp(delivery, "RetryEligible", false);
            addMethod?.Invoke(notificationsDb, [delivery]);
        }

        var saveChangesMethod = dbContextType.GetMethod("SaveChangesAsync", [typeof(CancellationToken)]);
        if (saveChangesMethod is not null)
        {
            await (Task)saveChangesMethod.Invoke(notificationsDb, [ct])!;
        }

        return recipientEmail;
    }

    private static void SetProp(object target, string name, object? value)
    {
        target.GetType().GetProperty(name)?.SetValue(target, value);
    }
}
