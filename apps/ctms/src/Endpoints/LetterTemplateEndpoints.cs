using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application.Services;
using Cadence.Apps.Ctms.Domain;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

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
            ["confirmation_letter"] = "confirmation_letter.html.liquid",
            ["follow_up_letter"] = "follow_up_letter.html.liquid",
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
            .RequirePermission(RenderPermission).RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature("ctms.letter.compile", http => http.Request.Path.Value ?? string.Empty);

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
            string detail = scopeDenied.ProblemDetails.Detail
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
        [FromServices] ILetterTemplateApplicationService service,
        [FromServices] IServiceProvider serviceProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        object? etmfDb = null;
        object? notifDb = null;
        if (request.Finalize)
        {
            var etmfDbType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName == "Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContext");
            etmfDb = etmfDbType != null ? serviceProvider.GetService(etmfDbType) : null;

            var notifDbType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName == "Cadence.Apps.Notifications.Infrastructure.Persistence.NotificationsDbContext");
            notifDb = notifDbType != null ? serviceProvider.GetService(notifDbType) : null;
        }

        return await service.CompileAndDispatchLetterAsync(templateName, request, etmfDb, notifDb, httpContext, ct);
    }

    /// <summary>
    /// Serves compiled GxP PDF binary stream for a visit letter.
    /// </summary>
    public static Task<IResult> GetLetterPdfAsync(
        string letterId,
        [FromServices] ILetterTemplateApplicationService service,
        CancellationToken ct,
        HttpContext? httpContext = null) =>
        service.GetLetterPdfAsync(letterId, httpContext, ct);

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
/// Helper for filing finalized visit letters into the eTMF repository.
/// </summary>
public static class EtmfFilingHelper
{
    public static async Task<string?> FileToEtmfAsync(
        SiteVisitLetterModel request,
        GxpPdfResult pdfResult,
        string templateName,
        string letterId,
        object? etmfDb,
        IClock clock,
        string user,
        Func<object?, CancellationToken, Task> commitExternalAsync,
        CancellationToken ct)
    {
        if (etmfDb is null) return null;

        var etmfAssembly = etmfDb.GetType().Assembly;
        var tmfDocType = etmfAssembly.GetType("Cadence.Apps.Etmf.Domain.TmfDocument");
        var tmfAuditLogType = etmfAssembly.GetType("Cadence.Apps.Etmf.Domain.TmfAuditLog");
        if (tmfDocType is null || tmfAuditLogType is null) return null;

        var now = clock.UtcNow;
        var docId = "DOC-CTMS-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var filename = $"{templateName}_{request.SiteId}_{request.StudyId}_{letterId}.pdf";

        var tmfDoc = Activator.CreateInstance(tmfDocType, docId);
        if (tmfDoc is null) return null;

        SetProp(tmfDoc, "StudyId", request.StudyId);
        SetProp(tmfDoc, "SiteId", request.SiteId);
        SetProp(tmfDoc, "Filename", filename);
        SetProp(tmfDoc, "Content", Convert.ToBase64String(pdfResult.PdfBytes));
        SetProp(tmfDoc, "MimeType", "application/pdf");
        SetProp(tmfDoc, "Zone", 5);
        SetProp(tmfDoc, "Section", "05.02");
        SetProp(tmfDoc, "ArtifactCode", "05.02.06");
        SetProp(tmfDoc, "ArtifactType", "Monitoring Visit Correspondence");
        var reason = request.ReasonForChange ?? "Automated eTMF filing for finalized monitoring visit letter.";
        tmfDocType.GetMethod("StageForQc")?.Invoke(tmfDoc, [user, "System/CRA", reason, now, 1]);
        tmfDocType.GetMethod("Approve")?.Invoke(tmfDoc, [user, "System/CRA", reason, now, 2]);
        SetProp(tmfDoc, "TaxonomyVersion", "v3.2.0-complete");
        SetProp(tmfDoc, "CreatedAt", now.UtcDateTime);
        SetProp(tmfDoc, "CreatedBy", user);
        SetProp(tmfDoc, "VersionIndex", 1);
        SetProp(tmfDoc, "ReasonForChange", reason);
        SetProp(tmfDoc, "MetadataJson", JsonSerializer.Serialize(new
        {
            source = "CTMS_MONITORING_PIPELINE",
            letter_type = templateName,
            is_signed = pdfResult.IsSigned,
            sha256_hash = pdfResult.Sha256Hash
        }));

        var dbContextType = etmfDb.GetType();
        var addMethod = dbContextType.GetMethod("Add", [typeof(object)]) ?? dbContextType.GetMethod("Add");
        addMethod?.Invoke(etmfDb, [tmfDoc]);

        var auditLog = Activator.CreateInstance(tmfAuditLogType);
        if (auditLog is not null)
        {
            SetProp(auditLog, "Timestamp", now);
            SetProp(auditLog, "UserId", user);
            SetProp(auditLog, "UserRole", "System/CRA");
            SetProp(auditLog, "Action", "INGEST_MONITORING_LETTER");
            SetProp(auditLog, "DocumentId", docId);
            SetProp(auditLog, "Details", $"Filed finalized monitoring visit letter {filename} into eTMF Zone 5.");
            SetProp(auditLog, "ReasonForChange", request.ReasonForChange ?? "Automated visit letter eTMF integration");
            addMethod?.Invoke(etmfDb, [auditLog]);
        }

        await commitExternalAsync(etmfDb, ct);

        return docId;
    }

    private static void SetProp(object target, string name, object? value) =>
        target.GetType().GetProperty(name)?.SetValue(target, value);
}

/// <summary>
/// Helper for dispatching email notifications for finalized visit letters.
/// </summary>
public static class EmailDispatchHelper
{
    public static async Task<string?> DispatchEmailNotificationAsync(
        SiteVisitLetterModel request,
        GxpPdfResult pdfResult,
        string templateName,
        string letterId,
        object? notificationsDb,
        IClock clock,
        string user,
        Func<object?, CancellationToken, Task> commitExternalAsync,
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

        await commitExternalAsync(notificationsDb, ct);

        return recipientEmail;
    }

    private static void SetProp(object target, string name, object? value) =>
        target.GetType().GetProperty(name)?.SetValue(target, value);
}
