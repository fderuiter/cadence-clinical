using Cadence.Apps.Interop.Application.Services;

using Microsoft.AspNetCore.Http;
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
    public const int MaxAssertionLifetimeSeconds = EsourceConfigurationService.MaxAssertionLifetimeSeconds;

    public static IEndpointRouteBuilder MapEsourceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapEsourceConfigurationEndpoints();
        app.MapEsourceReconciliationEndpoints();
        return app;
    }

    public static IResult PollHospitalEhrAsync(EhrPollRequest request, HttpContext httpContext) =>
        EsourceConfigurationService.PollHospitalEhr(request, httpContext);
}
