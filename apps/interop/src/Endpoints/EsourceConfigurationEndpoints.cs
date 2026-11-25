using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Domain;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Interop.Endpoints;

public static class EsourceConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapEsourceConfigurationEndpoints(this IEndpointRouteBuilder app)
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
            .Produces<EhrEndpointRecord>(StatusCodes.Status201Created)
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

        return app;
    }

    public static Task<IResult> ListEhrEndpointsAsync(
        bool active_only,
        EsourceConfigurationService service,
        CancellationToken ct) =>
        service.ListEhrEndpointsAsync(active_only, ct);

    public static Task<IResult> RegisterEhrEndpointAsync(
        EhrEndpointCreateRequest request,
        EsourceConfigurationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.RegisterEhrEndpointAsync(request, httpContext, ct);

    public static Task<IResult> GenerateClientAssertionAsync(
        SmartAssertionRequest request,
        EsourceConfigurationService service,
        CancellationToken ct) =>
        service.GenerateClientAssertionAsync(request, ct);

    public static Task<IResult> VerifyClientAssertionAsync(
        SmartVerifyRequest request,
        EsourceConfigurationService service,
        CancellationToken ct) =>
        service.VerifyClientAssertionAsync(request, ct);

    public static IResult PollHospitalEhrAsync(
        EhrPollRequest request,
        HttpContext httpContext) =>
        EsourceConfigurationService.PollHospitalEhr(request, httpContext);

    public static Task<IResult> IngestFhirBundleAsync(
        FhirBundleIngestRequest request,
        EsourceIngestionAndQueryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.IngestFhirBundleAsync(request, httpContext, ct);
}
