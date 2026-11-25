using System.Threading.Tasks;

using Cadence.Apps.Etmf.Application;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Etmf.Endpoints;

public static class EtmfIntelligenceEndpoints
{
    public static void MapEtmfEndpoints(this IEndpointRouteBuilder routes)
    {
        EtmfQcEndpoints.MapEtmfQcRoutes(routes);
        EtmfDocumentEndpoints.MapEtmfDocumentRoutes(routes);
        EtmfExportEndpoints.MapEtmfExportRoutes(routes);
    }

    public static Task<IResult> ClassifyDocument(
        [FromBody] ClassifyRequest request,
        [FromServices] IClock clock) =>
        EtmfClassificationService.ClassifyDocumentAsync(request, clock);

    public static Task<IResult> GetTaxonomy([FromQuery] string? version) =>
        EtmfClassificationService.GetTaxonomyAsync(version);
}
