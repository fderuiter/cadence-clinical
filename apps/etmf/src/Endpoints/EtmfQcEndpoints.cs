using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Application;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Etmf.Endpoints;

internal static class EtmfQcEndpoints
{
    public static void MapEtmfQcRoutes(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/etmf")
            .WithTags("EtmfIntelligenceEndpoints");

        group.MapPost("/intelligence/classify", ClassifyDocument).RequirePermission("etmf_document:tag").RequireCallerStudyScope();
        group.MapPost("/intelligence/stage-qc", StageQcDocument).RequirePermission("etmf_document:create").RequireCallerStudyAndSiteScope();
        group.MapGet("/intelligence/qc-queue", GetQcQueue).RequirePermission("etmf_document:read").RequireCallerStudyAndSiteScope();
        group.MapPost("/intelligence/qc-review/{doc_id}", ReviewQcDocument).RequirePermission("etmf_document:transition_clinical_qc").RequireCallerStudyAndSiteScope();
        group.MapPost("/documents/{document_id}/submit-qc", SubmitDocumentForQc).RequirePermission("etmf_document:create").RequireCallerStudyAndSiteScope();
    }

    private static Task<IResult> ClassifyDocument(
        [FromBody] ClassifyRequest request,
        [FromServices] IClock clock) =>
        EtmfClassificationService.ClassifyDocumentAsync(request, clock);

    private static Task<IResult> StageQcDocument(
        [FromBody] StageDocumentQCRequest request,
        [FromServices] EtmfQcApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.StageQcDocumentAsync(request, httpContext, ct);

    private static Task<IResult> GetQcQueue(
        [FromQuery] string? study_id,
        [FromServices] EtmfQcApplicationService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery] string? site_id = null) =>
        service.GetQcQueueAsync(study_id, httpContext, ct, site_id);

    private static Task<IResult> ReviewQcDocument(
        [FromRoute] string doc_id,
        [FromBody] CraqcReviewRequest request,
        [FromServices] EtmfQcApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ReviewQcDocumentAsync(doc_id, request, httpContext, ct);

    private static Task<IResult> SubmitDocumentForQc(
        [FromRoute] string document_id,
        [FromBody] SubmitDocumentForQcRequest request,
        [FromServices] EtmfQcApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.SubmitDocumentForQcAsync(document_id, request, httpContext, ct);
}
