using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Application;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Etmf.Endpoints;

public static class EtmfIntelligenceEndpoints
{
    public static void MapEtmfEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/etmf");

        // Every route names its permission (ADR-2510, ADR-2516). A study id in a body or on a stored document is checked
        // against the caller's signed study assignments, and lists are narrowed to them (#5361).
        group.MapPost("/intelligence/classify", ClassifyDocument).RequirePermission("etmf_document:tag").RequireCallerStudyScope();
        group.MapPost("/intelligence/stage-qc", StageQcDocument).RequirePermission("etmf_document:create").RequireCallerStudyAndSiteScope();
        group.MapGet("/intelligence/qc-queue", GetQcQueue).RequirePermission("etmf_document:read").RequireCallerStudyAndSiteScope();
        group.MapPost("/intelligence/qc-review/{doc_id}", ReviewQcDocument).RequirePermission("etmf_document:transition_clinical_qc").RequireCallerStudyAndSiteScope();

        routes.MapEtmfDocumentEndpoints();
        routes.MapEtmfExportEndpoints();
    }

    public static Task<IResult> ClassifyDocument(
        [FromBody] ClassifyRequest request,
        [FromServices] IClock clock) =>
        Task.FromResult(EtmfClassificationEngine.ClassifyDocument(request, clock));

    public static Task<IResult> StageQcDocument(
        [FromBody] StageDocumentQCRequest request,
        [FromServices] IEtmfQcService qcService,
        HttpContext httpContext,
        CancellationToken ct) =>
        qcService.StageQcDocumentAsync(request, httpContext, ct);

    public static Task<IResult> GetQcQueue(
        [FromQuery] string? study_id,
        [FromServices] IEtmfQcService qcService,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery] string? site_id = null) =>
        qcService.GetQcQueueAsync(study_id, site_id, httpContext, ct);

    public static Task<IResult> ReviewQcDocument(
        [FromRoute] string doc_id,
        [FromBody] CraqcReviewRequest request,
        [FromServices] IEtmfQcService qcService,
        HttpContext httpContext,
        CancellationToken ct) =>
        qcService.ReviewQcDocumentAsync(doc_id, request, httpContext, ct);
}
