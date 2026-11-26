using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Application;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Etmf.Endpoints;

public static class EtmfExportEndpoints
{
    public static void MapEtmfExportEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/etmf")
            .WithTags("EtmfIntelligenceEndpoints");

        group.MapPost("/documents/export", ExportDocuments).RequirePermission("etmf_document:export").RequireCallerStudyAndSiteScope();
        group.MapGet("/export-jobs/{job_id}", GetExportJob).RequirePermission("etmf_document:export").RequireCallerStudyAndSiteScope();
        group.MapGet("/export-jobs/{job_id}/download", DownloadExportJob).RequirePermission("etmf_document:export").RequireCallerStudyAndSiteScope();
        group.MapGet("/export-jobs", ListExportJobs).RequirePermission("etmf_document:export").RequireCallerStudyAndSiteScope();
    }

    public static Task<IResult> ExportDocuments(
        [FromBody] ExportDocumentsRequest request,
        [FromServices] IEtmfExportService exportService,
        HttpContext httpContext,
        CancellationToken ct) =>
        exportService.ExportDocumentsAsync(request, httpContext, ct);

    public static Task<IResult> GetExportJob(
        [FromRoute] string job_id,
        [FromServices] IEtmfExportService exportService,
        HttpContext httpContext,
        CancellationToken ct) =>
        exportService.GetExportJobAsync(job_id, httpContext, ct);

    public static Task<IResult> DownloadExportJob(
        [FromRoute] string job_id,
        [FromServices] IEtmfExportService exportService,
        HttpContext httpContext,
        CancellationToken ct) =>
        exportService.DownloadExportJobAsync(job_id, httpContext, ct);

    public static Task<IResult> ListExportJobs(
        [FromQuery] string? study_id,
        [FromServices] IEtmfExportService exportService,
        HttpContext httpContext,
        CancellationToken ct) =>
        exportService.ListExportJobsAsync(study_id, httpContext, ct);
}
