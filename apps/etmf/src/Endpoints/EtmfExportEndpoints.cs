using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Application;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Etmf.Endpoints;

internal static class EtmfExportEndpoints
{
    public static void MapEtmfExportRoutes(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/etmf")
            .WithTags("EtmfIntelligenceEndpoints");

        group.MapPost("/documents/export", ExportDocuments).RequirePermission("etmf_document:export").RequireCallerStudyAndSiteScope();
        group.MapGet("/export-jobs/{job_id}", GetExportJob).RequirePermission("etmf_document:export").RequireCallerStudyAndSiteScope();
        group.MapGet("/export-jobs/{job_id}/download", DownloadExportJob).RequirePermission("etmf_document:export").RequireCallerStudyAndSiteScope();
        group.MapGet("/export-jobs", ListExportJobs).RequirePermission("etmf_document:export").RequireCallerStudyAndSiteScope();
    }

    private static Task<IResult> ExportDocuments(
        [FromBody] ExportDocumentsRequest request,
        [FromServices] EtmfExportApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ExportDocumentsAsync(request, httpContext, ct);

    private static Task<IResult> GetExportJob(
        [FromRoute] string job_id,
        [FromServices] EtmfExportApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetExportJobAsync(job_id, httpContext, ct);

    private static Task<IResult> DownloadExportJob(
        [FromRoute] string job_id,
        [FromServices] EtmfExportApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.DownloadExportJobAsync(job_id, httpContext, ct);

    private static Task<IResult> ListExportJobs(
        [FromQuery] string? study_id,
        [FromServices] EtmfExportApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ListExportJobsAsync(study_id, httpContext, ct);
}
