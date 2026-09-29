using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Site visit letter rendering. The caller picks a letter by key from <see cref="Catalog"/>; nothing
/// outside that allow-list can be rendered.
/// @req:PRD-SYS-053
/// </summary>
public static class LetterTemplateEndpoints
{
    /// <summary>The letters CTMS can render, keyed by the value callers pass as <c>templateName</c>.</summary>
    public static TemplateCatalog Catalog { get; } = new(
        typeof(LetterTemplateEndpoints).Assembly,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["confirmation_letter"] = "confirmation_letter.liquid",
            ["follow_up_letter"] = "follow_up_letter.liquid",
        });

    public static RouteGroupBuilder MapLetterTemplateEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/letters/render", RenderLetterAsync)
            .WithName("RenderSiteVisitLetter")
            .WithSummary("Renders a CTMS site visit monitoring letter via Liquid template.")
            .Produces<string>(StatusCodes.Status200OK, "text/plain")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    public static async Task<Results<ContentHttpResult, ProblemHttpResult>> RenderLetterAsync(
        string templateName,
        SiteVisitLetterModel request,
        ITemplateRenderer renderer)
    {
        if (!Catalog.Contains(templateName))
        {
            return UnknownTemplate(Catalog);
        }

        string rendered = await renderer.RenderAsync(Catalog, templateName, request);
        return TypedResults.Content(rendered, "text/plain");
    }

    private static ProblemHttpResult UnknownTemplate(TemplateCatalog catalog) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Unknown template",
            detail: $"templateName must be one of: {string.Join(", ", catalog.Keys.Order(StringComparer.Ordinal))}.",
            extensions: new Dictionary<string, object?> { ["code"] = "TEMPLATE_NOT_ALLOWED" });
}
