using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Application;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Etmf.Endpoints;

public static class EtmfDocumentEndpoints
{
    public static void MapEtmfDocumentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/etmf")
            .WithTags("EtmfIntelligenceEndpoints");

        group.MapGet("/documents", GetDocuments).RequirePermission("etmf_document:read").RequireCallerStudyAndSiteScope();
        group.MapGet("/documents/{document_id}", GetDocumentById).RequirePermission("etmf_document:read").RequireCallerStudyAndSiteScope();
        group.MapGet("/documents/{document_id}/content", DownloadDocumentContent).RequirePermission("etmf_document:read").RequireCallerStudyAndSiteScope();
        group.MapPost("/documents/{document_id}/submit-qc", SubmitDocumentForQc).RequirePermission("etmf_document:create").RequireCallerStudyAndSiteScope();
        group.MapPatch("/documents/{document_id}/taxonomy", UpdateDocumentTaxonomy).RequirePermission("etmf_document:tag").RequireCallerStudyAndSiteScope();

        group.MapPost("/ingest", IngestDocument).RequirePermission("etmf_document:create").RequireCallerStudyAndSiteScope();
        group.MapGet("/taxonomy", GetTaxonomy).RequirePermission("etmf_taxonomy:read").RequireCallerStudyScope();
    }

    public static Task<IResult> GetDocuments(
        [FromQuery] string? study_id,
        [FromQuery] int? zone,
        [FromServices] IEtmfDocumentService documentService,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery] string? site_id = null) =>
        documentService.GetDocumentsAsync(study_id, zone, site_id, httpContext, ct);

    public static Task<IResult> GetDocumentById(
        [FromRoute] string document_id,
        [FromServices] IEtmfDocumentService documentService,
        HttpContext httpContext,
        CancellationToken ct) =>
        documentService.GetDocumentByIdAsync(document_id, httpContext, ct);

    public static Task<IResult> DownloadDocumentContent(
        [FromRoute] string document_id,
        [FromServices] IEtmfDocumentService documentService,
        HttpContext httpContext,
        CancellationToken ct) =>
        documentService.DownloadDocumentContentAsync(document_id, httpContext, ct);

    public static Task<IResult> SubmitDocumentForQc(
        [FromRoute] string document_id,
        [FromBody] SubmitDocumentForQcRequest request,
        [FromServices] IEtmfDocumentService documentService,
        HttpContext httpContext,
        CancellationToken ct) =>
        documentService.SubmitDocumentForQcAsync(document_id, request, httpContext, ct);

    public static Task<IResult> UpdateDocumentTaxonomy(
        [FromRoute] string document_id,
        [FromBody] UpdateDocumentTaxonomyRequest request,
        [FromServices] IEtmfDocumentService documentService,
        HttpContext httpContext,
        CancellationToken ct) =>
        documentService.UpdateDocumentTaxonomyAsync(document_id, request, httpContext, ct);

    public static Task<IResult> IngestDocument(
        [FromBody] IngestDocumentRequest request,
        [FromServices] IEtmfDocumentService documentService,
        HttpContext httpContext,
        CancellationToken ct) =>
        documentService.IngestDocumentAsync(request, httpContext, ct);

    public static Task<IResult> GetTaxonomy([FromQuery] string? version) =>
        Task.FromResult<IResult>(TypedResults.Ok(EtmfClassificationEngine.GetStandardTaxonomy()));
}
