using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application.Services;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Ctms.Endpoints;

public static class DoaQueryEndpoints
{
    public static RouteGroupBuilder MapDoaQueryEndpoints(this RouteGroupBuilder group)
    {
        var doa = group.MapGroup("/doa")
            .WithTags("DoaEndpoints");

        doa.MapGet("/sites/{site_id}/log", GetSiteDoaLogAsync)
            .WithName("GetSiteDoaLog")
            .WithSummary("Fetch active and historical DOA log matrix for a site.")
            .Produces<DoaLogResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(DoaEndpoints.ReadPermission).RequireSiteScope("site_id");

        doa.MapGet("/sites/{site_id}/export-pdf", ExportSiteDoaPdfAsync)
            .WithName("ExportSiteDoaPdf")
            .WithSummary("Export 21 CFR Part 11 signed DOA PDF log.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(DoaEndpoints.ReadPermission).RequireSiteScope("site_id")
            .RequireElectronicSignature("ctms.doa.export_pdf", http => http.Request.Path.Value ?? string.Empty);

        return group;
    }

    public static Task<IResult> GetSiteDoaLogAsync(
        [FromRoute(Name = "site_id")] string siteId,
        [FromServices] IDoaQueryAndExportService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt = null,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt = null) =>
        service.GetSiteDoaLogAsync(siteId, httpContext, ct, studyId, validAt, knownAt);

    public static Task<IResult> ExportSiteDoaPdfAsync(
        [FromRoute(Name = "site_id")] string siteId,
        [FromServices] IDoaQueryAndExportService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null) =>
        service.ExportSiteDoaPdfAsync(siteId, httpContext, ct, studyId);
}
