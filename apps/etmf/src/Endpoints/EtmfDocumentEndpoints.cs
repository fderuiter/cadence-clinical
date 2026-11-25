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

internal static class EtmfDocumentEndpoints
{
    public static void MapEtmfDocumentRoutes(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/etmf")
            .WithTags("EtmfIntelligenceEndpoints");

        group.MapGet("/documents", GetDocuments).RequirePermission("etmf_document:read").RequireCallerStudyAndSiteScope();
        group.MapGet("/documents/{document_id}", GetDocumentById).RequirePermission("etmf_document:read").RequireCallerStudyAndSiteScope();
        group.MapGet("/documents/{document_id}/content", DownloadDocumentContent).RequirePermission("etmf_document:read").RequireCallerStudyAndSiteScope();
        group.MapPatch("/documents/{document_id}/taxonomy", UpdateDocumentTaxonomy).RequirePermission("etmf_document:tag").RequireCallerStudyAndSiteScope();
        group.MapPost("/ingest", IngestDocument).RequirePermission("etmf_document:create").RequireCallerStudyAndSiteScope();
        group.MapGet("/taxonomy", GetTaxonomy).RequirePermission("etmf_taxonomy:read").RequireCallerStudyScope();
    }

    private static Task<IResult> GetDocuments(
        [FromQuery] string? study_id,
        [FromQuery] int? zone,
        [FromServices] EtmfDocumentApplicationService service,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery] string? site_id = null) =>
        service.GetDocumentsAsync(study_id, zone, httpContext, ct, site_id);

    private static Task<IResult> GetDocumentById(
        [FromRoute] string document_id,
        [FromServices] EtmfDocumentApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetDocumentByIdAsync(document_id, httpContext, ct);

    private static Task<IResult> DownloadDocumentContent(
        [FromRoute] string document_id,
        [FromServices] EtmfDocumentApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.DownloadDocumentContentAsync(document_id, httpContext, ct);

    private static Task<IResult> UpdateDocumentTaxonomy(
        [FromRoute] string document_id,
        [FromBody] UpdateDocumentTaxonomyRequest request,
        [FromServices] EtmfDocumentApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.UpdateDocumentTaxonomyAsync(document_id, request, httpContext, ct);

    private static Task<IResult> IngestDocument(
        [FromBody] IngestDocumentRequest request,
        [FromServices] EtmfDocumentApplicationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.IngestDocumentAsync(request, httpContext, ct);

    private static Task<IResult> GetTaxonomy([FromQuery] string? version) =>
        EtmfClassificationService.GetTaxonomyAsync(version);
}
