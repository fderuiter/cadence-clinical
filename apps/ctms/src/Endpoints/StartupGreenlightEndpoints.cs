using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application.Services;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Site Startup (SSU) and Country-Level Multi-Site Greenlight Activation Matrix endpoints.
/// @req:PRD-SSU-001
/// @req:PRD-SSU-002
/// @req:PRD-SSU-003
/// </summary>
public static class StartupGreenlightEndpoints
{
    public const string CertifyPermission = "ctms_greenlight:certify";
    public const string ReadPermission = "ctms_greenlight:read";

    public static RouteGroupBuilder MapStartupGreenlightEndpoints(this RouteGroupBuilder group)
    {
        var startup = group.MapGroup("/startup");

        startup.MapGet("/sites/{siteId}/greenlight", GetSiteGreenlightAsync)
            .WithName("EvaluateSiteGreenlight")
            .RequirePermission(ReadPermission)
            .RequireSiteScope("siteId")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Evaluates 6-facet greenlight readiness for a single trial site.")
            .Produces<SiteGreenlightGateResponseDto>(StatusCodes.Status200OK);

        startup.MapPost("/sites/{siteId}/greenlight/certify", CertifySiteGreenlightAsync)
            .WithName("CertifySiteGreenlight")
            .RequirePermission(CertifyPermission)
            .RequireSiteScope("siteId")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Certifies greenlight activation for a single trial site, issuing an HMAC-SHA256 token.")
            .Produces<SiteGreenlightGateResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireElectronicSignature("ctms.greenlight.certify", http => http.Request.Path.Value ?? string.Empty);

        startup.MapGet("/matrix", GetCountryMatrixAsync)
            .WithName("GetCountryGreenlightMatrix")
            .RequirePermission(ReadPermission).RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Displays country-level matrix showing 6-facet greenlight readiness for all sites in a selected country.")
            .Produces<List<SiteGreenlightGateResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        startup.MapPost("/matrix/batch-certify", BatchCertifyCountrySitesAsync)
            .WithName("BatchCertifyCountrySites")
            .RequirePermission(CertifyPermission).RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Batch-certifies all selected 100% compliant sites in a target country with a single e-signature.")
            .Produces<BatchCertifyResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature("ctms.greenlight.batch_certify", http => http.Request.Path.Value ?? string.Empty);

        return group;
    }

    public static Task<Results<Ok<SiteGreenlightGateResponseDto>, ProblemHttpResult>> GetSiteGreenlightAsync(
        string siteId,
        [FromQuery(Name = "study_id")] string studyId,
        [FromQuery(Name = "country_code")] string? countryCode,
        [FromServices] IStartupGreenlightApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetSiteGreenlightAsync(siteId, studyId, countryCode, httpContext, ct);

    public static Task<Results<Ok<SiteGreenlightGateResponseDto>, BadRequest<ProblemDetails>, ProblemHttpResult>> CertifySiteGreenlightAsync(
        string siteId,
        [FromQuery(Name = "study_id")] string studyId,
        [FromQuery(Name = "country_code")] string? countryCode,
        [FromHeader(Name = "X-Change-Reason")] string? changeReasonHeader,
        [FromServices] IStartupGreenlightApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.CertifySiteGreenlightAsync(siteId, studyId, countryCode, changeReasonHeader, httpContext, ct);

    public static Task<Results<Ok<List<SiteGreenlightGateResponseDto>>, ProblemHttpResult>> GetCountryMatrixAsync(
        [FromQuery(Name = "study_id")] string studyId,
        [FromQuery(Name = "country_code")] string countryCode,
        [FromServices] IStartupGreenlightApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetCountryMatrixAsync(studyId, countryCode, httpContext, ct);

    public static Task<Results<Ok<BatchCertifyResponseDto>, BadRequest<ProblemDetails>, ProblemHttpResult>> BatchCertifyCountrySitesAsync(
        [FromBody] BatchCertifyRequestDto request,
        [FromHeader(Name = "X-Change-Reason")] string? changeReasonHeader,
        [FromServices] IStartupGreenlightApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.BatchCertifyCountrySitesAsync(request, changeReasonHeader, httpContext, ct);
}
