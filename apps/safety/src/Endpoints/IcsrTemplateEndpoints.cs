using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Security.DependencyInjection;
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

    /// <summary>The permission that rendering an ICSR requires.</summary>
    public const string RenderPermission = "safety_report:create";

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
            .WithSummary("Renders an ICSR E2B(R3) safety report as Liquid XML; a report missing a mandatory element is refused with 400 naming each one.")
            .Produces<string>(StatusCodes.Status200OK, "application/xml")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            // A regulatory safety submission: medical monitors and system administrators (#4960, ADR-2511).
            .RequirePermission(RenderPermission);

        return group;
    }

    /// <summary>
    /// Renders <paramref name="request"/>, or refuses it with a validation problem keyed by the rendered path of each
    /// mandatory element it lacks, so an incomplete ICSR is never emitted with empty elements (#5464).
    /// </summary>
    public static async Task<Results<ContentHttpResult, ValidationProblem>> RenderIcsrAsync(IcsrReport request, ITemplateRenderer renderer)
    {
        var missing = IcsrMandatoryElements.Missing(request);
        if (missing.Count > 0)
        {
            return TypedResults.ValidationProblem(
                missing,
                detail: $"The ICSR is missing mandatory E2B(R3) elements: {string.Join(", ", missing.Keys)}.",
                title: "Mandatory ICSR elements are missing");
        }

        // The template reads the report as `icsr` (#4959).
        string rendered = await renderer.RenderAsync(
            Catalog, IcsrTemplateKey, new Dictionary<string, object?> { ["icsr"] = request });
        return TypedResults.Content(rendered, "application/xml");
    }
}
