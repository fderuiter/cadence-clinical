using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Safety.Endpoints;

/// <summary>ICSR E2B(R3) rendering from this service's own template.</summary>
public static class IcsrTemplateEndpoints
{
    public const string IcsrTemplateKey = "e2b_r3_icsr";

    public static TemplateCatalog Catalog { get; } = new(
        typeof(IcsrTemplateEndpoints).Assembly,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IcsrTemplateKey] = "e2b_r3_icsr.xml.liquid",
        });

    public static RouteGroupBuilder MapIcsrTemplateEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/icsr/render", RenderIcsrAsync)
            .WithName("RenderIcsrReport")
            .WithSummary("Renders an ICSR E2B(R3) safety report as Liquid XML.");

        return group;
    }

    public static async Task<ContentHttpResult> RenderIcsrAsync(IcsrReport request, ITemplateRenderer renderer)
    {
        string rendered = await renderer.RenderAsync(Catalog, IcsrTemplateKey, request);
        return TypedResults.Content(rendered, "application/xml");
    }
}
