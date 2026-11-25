using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application;
using Cadence.Apps.Ctms.Domain;
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
            return LetterTemplateApplicationService.UnknownTemplate(Catalog);
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
    public static Task<IResult> CompileAndDispatchLetterAsync(
        [FromQuery] string templateName,
        [FromBody] SiteVisitLetterModel request,
        LetterTemplateApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.CompileAndDispatchLetterAsync(templateName, request, Catalog, httpContext, ct);

    /// <summary>
    /// Serves compiled GxP PDF binary stream for a visit letter.
    /// </summary>
    public static Task<IResult> GetLetterPdfAsync(
        string letterId,
        LetterTemplateApplicationService service,
        CancellationToken ct,
        HttpContext? httpContext = null) =>
        service.GetLetterPdfAsync(letterId, Catalog, ct, httpContext);

    /// <summary>
    /// Builds context dictionary for template rendering.
    /// </summary>
    public static Dictionary<string, object?> BuildTemplateModel(
        SiteVisitLetterModel request,
        bool isSigned,
        string? signerName,
        string? signingTimestamp,
        string? signingPurpose,
        string? integrityHash) =>
        GxpPdfCompiler.BuildTemplateModel(request, isSigned, signerName, signingTimestamp, signingPurpose, integrityHash);
}
