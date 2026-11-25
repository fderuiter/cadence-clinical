using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Interop.Endpoints;

/// <summary>
/// Minimal API endpoint definitions for SMART on FHIR eSource polling and side-by-side reconciliation drawer workflows.
/// @req:PRD-SYS-001
/// @req:PRD-ESR-001
/// @req:PRD-ESR-002
/// @req:PRD-ESR-003
/// </summary>
public static class EsourceEndpoints
{
    /// <summary>The longest client assertion SMART Backend Services allows.</summary>
    public const int MaxAssertionLifetimeSeconds = EsourceIngestionService.MaxAssertionLifetimeSeconds;

    public static IEndpointRouteBuilder MapEsourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/interop/esource")
            .WithTags("eSource Reconciliation & SMART on FHIR");

        group.MapGet("/endpoints", ListEhrEndpointsAsync)
            .WithName("ListEhrEndpoints")
            .RequirePermission("esource_endpoint:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrEndpointListResponse>(StatusCodes.Status200OK);

        group.MapPost("/endpoints", RegisterEhrEndpointAsync)
            .WithName("RegisterEhrEndpoint")
            .RequirePermission("esource_endpoint:configure").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<Cadence.Apps.Interop.Domain.EhrEndpointRecord>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/smart/assertion", GenerateClientAssertionAsync)
            .WithName("GenerateClientAssertion")
            .RequirePermission("esource_endpoint:configure").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<SmartAssertionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/smart/verify", VerifyClientAssertionAsync)
            .WithName("VerifyClientAssertion")
            .RequirePermission("esource_endpoint:configure").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<SmartVerifyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/poll", PollHospitalEhrAsync)
            .WithName("PollHospitalEhr")
            .RequirePermission("esource_candidate:ingest").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/ingest", IngestFhirBundleAsync)
            .WithName("IngestFhirBundle")
            .RequirePermission("esource_candidate:ingest").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<FhirBundleIngestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapEsourceReconciliationEndpoints();
        return app;
    }

    public static Task<IResult> ListEhrEndpointsAsync(
        bool active_only,
        [FromServices] IEsourceIngestionService service,
        CancellationToken ct) =>
        service.ListEhrEndpointsAsync(active_only, ct);

    public static Task<IResult> RegisterEhrEndpointAsync(
        EhrEndpointCreateRequest request,
        [FromServices] IEsourceIngestionService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.RegisterEhrEndpointAsync(request, httpContext, ct);

    public static Task<IResult> GenerateClientAssertionAsync(
        SmartAssertionRequest request,
        [FromServices] IEsourceIngestionService service,
        CancellationToken ct) =>
        service.GenerateClientAssertionAsync(request, ct);

    public static Task<IResult> VerifyClientAssertionAsync(
        SmartVerifyRequest request,
        [FromServices] IEsourceIngestionService service,
        CancellationToken ct) =>
        service.VerifyClientAssertionAsync(request, ct);

    public static IResult PollHospitalEhrAsync(
        EhrPollRequest request,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.StudyId) || string.IsNullOrWhiteSpace(request.SubjectId))
        {
            return Results.Problem("StudyId and SubjectId are required for polling.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, request.StudyId, request.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        return Results.Problem(
            "Polling an EHR is not available: no FHIR client is configured. Ingest an exported FHIR bundle instead.",
            statusCode: StatusCodes.Status501NotImplemented,
            title: "EHR polling not configured");
    }

    public static Task<IResult> IngestFhirBundleAsync(
        FhirBundleIngestRequest request,
        [FromServices] IEsourceIngestionService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.IngestFhirBundleAsync(request, httpContext, ct);
}
