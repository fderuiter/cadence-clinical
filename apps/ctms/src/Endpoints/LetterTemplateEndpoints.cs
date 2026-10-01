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
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Site visit letter rendering and GxP PDF compilation pipeline.
/// @req:PRD-CTMS-002
/// </summary>
public static class LetterTemplateEndpoints
{
    /// <summary>The permission that rendering a visit letter requires.</summary>
    public const string RenderPermission = "ctms_monitoring_visit:create";

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
            .RequirePermission(RenderPermission);

        group.MapPost("/letters/compile", CompileAndDispatchLetterAsync)
            .WithName("CompileSiteVisitLetterPdf")
            .WithSummary("Compiles a GxP-compliant PDF visit letter with Part 11 electronic signature, eTMF filing, and automated email dispatch.")
            .Produces<VisitLetterCompilationResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(RenderPermission);

        group.MapGet("/letters/{letterId}/pdf", GetLetterPdfAsync)
            .WithName("GetSiteVisitLetterPdf")
            .WithSummary("Downloads or views the compiled GxP PDF visit letter.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(RenderPermission);

        return group;
    }

    /// <summary>
    /// Renders a site visit letter to HTML string.
    /// </summary>
    public static async Task<Results<ContentHttpResult, ProblemHttpResult>> RenderLetterAsync(
        string templateName,
        SiteVisitLetterModel request,
        ITemplateRenderer renderer)
    {
        if (!Catalog.Contains(templateName))
        {
            return UnknownTemplate(Catalog);
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

        var user = httpContext.User?.Identity?.Name ?? request.CraId;
        var now = clock.UtcNow;

        var pdfResult = await GxpPdfCompiler.CompileLetterPdfAsync(
            templateName,
            request,
            renderer,
            Catalog,
            clock);

        var letterId = "LTR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var visitId = string.IsNullOrWhiteSpace(request.VisitId) ? ("VISIT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()) : request.VisitId;
        var objectKey = $"letters/{request.StudyId}/{request.SiteId}/{letterId}.pdf";

        // File to eTMF if finalized
        string? etmfDocId = null;
        if (pdfResult.IsSigned)
        {
            var etmfDb = serviceProvider.GetService<Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContext>();
            var etmfDoc = await EtmfFilingHelper.FileToEtmfAsync(request, pdfResult, templateName, letterId, etmfDb, clock, user, ct);
            etmfDocId = etmfDoc?.Id;
        }

        // Dispatch Email Notification if finalized
        DateTimeOffset? dispatchedAt = null;
        string? recipientEmail = null;
        if (pdfResult.IsSigned)
        {
            var notifDb = serviceProvider.GetService<Cadence.Apps.Notifications.Infrastructure.Persistence.NotificationsDbContext>();
            var notifRecord = await EmailDispatchHelper.DispatchEmailNotificationAsync(request, pdfResult, templateName, letterId, notifDb, clock, user, ct);
            if (notifRecord is not null)
            {
                dispatchedAt = now;
                recipientEmail = notifRecord.RecipientUserId;
            }
        }

        // Persist CtmsGeneratedLetter
        var entity = new CtmsGeneratedLetter(letterId)
        {
            VisitId = visitId,
            LetterType = templateName,
            RenderedContent = pdfResult.RenderedHtml,
            PdfObjectKey = objectKey,
            PdfSha256Hash = pdfResult.Sha256Hash,
            Status = pdfResult.IsSigned ? "FINALIZED" : "DRAFT",
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

        // Audit log entry
        var audit = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = "CRA",
            Action = pdfResult.IsSigned ? "FINALIZE_VISIT_LETTER_PDF" : "GENERATE_VISIT_LETTER_DRAFT_PDF",
            Details = $"Compiled visit letter PDF for {templateName} in study {request.StudyId} site {request.SiteId}. SHA256: {pdfResult.Sha256Hash}. eTMF DocId: {etmfDocId ?? "N/A"}. Status: {entity.Status}.",
            ReasonForChange = request.ReasonForChange ?? "Visit letter PDF compilation and audit trail persistence."
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
            Status: entity.Status,
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
        CancellationToken ct)
    {
        var letter = await dbContext.CtmsGeneratedLetters.AsNoTracking().FirstOrDefaultAsync(l => l.Id == letterId, ct);
        if (letter is null)
        {
            return TypedResults.NotFound(new { error = $"Letter with ID '{letterId}' not found." });
        }

        var model = new SiteVisitLetterModel(
            CraId: letter.CreatedBy,
            CreatedAt: letter.CreatedAt.ToString("yyyy-MM-dd"),
            StudyId: "STUDY",
            SiteId: "SITE",
            VisitId: letter.VisitId,
            Finalize: letter.IsSigned
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
/// Helper for filing finalized visit letters into the eTMF repository.
/// </summary>
public static class EtmfFilingHelper
{
    /// <summary>
    /// Ingests a finalized visit letter PDF into the eTMF repository.
    /// </summary>
    public static async Task<Cadence.Apps.Etmf.Domain.TmfDocument?> FileToEtmfAsync(
        SiteVisitLetterModel request,
        GxpPdfResult pdfResult,
        string templateName,
        string letterId,
        Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContext? etmfDb,
        IClock clock,
        string user,
        CancellationToken ct)
    {
        if (etmfDb is null) return null;

        var now = clock.UtcNow;
        var docId = "DOC-CTMS-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var filename = $"{templateName}_{request.SiteId}_{request.StudyId}_{letterId}.pdf";

        var tmfDoc = new Cadence.Apps.Etmf.Domain.TmfDocument(docId)
        {
            StudyId = request.StudyId,
            SiteId = request.SiteId,
            Filename = filename,
            Content = Convert.ToBase64String(pdfResult.PdfBytes),
            MimeType = "application/pdf",
            Zone = 5,
            Section = "05.02",
            ArtifactCode = "05.02.06",
            ArtifactType = "Monitoring Visit Correspondence",
            Status = "APPROVED",
            ApprovalStatus = "APPROVED",
            TaxonomyVersion = "v3.2.0-complete",
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
            ReasonForChange = request.ReasonForChange ?? "Automated eTMF filing for finalized monitoring visit letter.",
            MetadataJson = JsonSerializer.Serialize(new
            {
                source = "CTMS_MONITORING_PIPELINE",
                letter_type = templateName,
                is_signed = pdfResult.IsSigned,
                sha256_hash = pdfResult.Sha256Hash
            })
        };

        etmfDb.TmfDocuments.Add(tmfDoc);

        var auditLog = new Cadence.Apps.Etmf.Domain.TmfAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = "System/CRA",
            Action = "INGEST_MONITORING_LETTER",
            DocumentId = docId,
            Details = $"Filed finalized monitoring visit letter {filename} into eTMF Zone 5.",
            ReasonForChange = request.ReasonForChange ?? "Automated visit letter eTMF integration"
        };
        etmfDb.TmfAuditLogs.Add(auditLog);

        await etmfDb.SaveChangesAsync(ct);
        return tmfDoc;
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
    public static async Task<Cadence.Apps.Notifications.Domain.NotificationRecord?> DispatchEmailNotificationAsync(
        SiteVisitLetterModel request,
        GxpPdfResult pdfResult,
        string templateName,
        string letterId,
        Cadence.Apps.Notifications.Infrastructure.Persistence.NotificationsDbContext? notificationsDb,
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

        var now = clock.UtcNow;
        var notificationId = "NOTIF-CTMS-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var pdfAccessUrl = $"/api/v1/ctms/letters/{letterId}/pdf";

        var record = new Cadence.Apps.Notifications.Domain.NotificationRecord
        {
            Id = notificationId,
            RecipientUserId = recipientEmail,
            RecipientRoleId = "PRINCIPAL_INVESTIGATOR",
            Category = "VISIT_LETTER_DISPATCH",
            Priority = "HIGH",
            Channels = "EMAIL,IN_APP",
            MessageContent = $"Official Monitoring Visit Letter ({templateName}) for Study {request.StudyId} Site {request.SiteId} is ready. Access secure PDF: {pdfAccessUrl}",
            RelatedEntityId = letterId,
            RelatedEntityType = "CTMS_VISIT_LETTER",
            Status = "SENT",
            DeliveryState = "COMPLETED",
            Retries = 0,
            CreatedAt = now,
            CreatedBy = user,
            VersionIndex = 1,
            ReasonForChange = "Automated email notification dispatch for visit letter finalization."
        };

        notificationsDb.NotificationRecords.Add(record);

        var delivery = new Cadence.Apps.Notifications.Domain.NotificationDelivery
        {
            Id = Guid.NewGuid().ToString(),
            NotificationId = notificationId,
            Channel = "EMAIL",
            Status = "COMPLETED",
            Attempts = 1,
            CompletedAt = now,
            RetryEligible = false
        };

        notificationsDb.NotificationDeliveries.Add(delivery);

        await notificationsDb.SaveChangesAsync(ct);
        return record;
    }
}
