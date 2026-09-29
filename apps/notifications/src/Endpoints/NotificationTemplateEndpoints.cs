using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Domain;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Notifications.Endpoints;

/// <summary>
/// Notification rendering. The caller picks a notification by key from <see cref="Catalog"/>; nothing
/// outside that allow-list can be rendered.
/// @req:PRD-SYS-053
/// </summary>
public static class NotificationTemplateEndpoints
{
    /// <summary>The notifications this service can render, keyed by the value callers pass as <c>templateName</c>.</summary>
    public static TemplateCatalog Catalog { get; } = new(
        typeof(NotificationTemplateEndpoints).Assembly,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["edc_query_raised"] = "edc_query_raised.html.liquid",
            ["etmf_expiry_alert"] = "etmf_expiry_alert.html.liquid",
            ["missing_diary_alert"] = "missing_diary_alert.html.liquid",
            ["protocol_amendment"] = "protocol_amendment.html.liquid",
            ["reconsent_required"] = "reconsent_required.html.liquid",
            ["sae_reconciliation"] = "sae_reconciliation.html.liquid",
        });

    public static RouteGroupBuilder MapNotificationTemplateEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/render", RenderNotificationAsync)
            .WithName("RenderNotificationTemplate")
            .WithSummary("Renders a Liquid notification template asynchronously.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    public static async Task<Results<Ok<RenderedNotification>, ProblemHttpResult>> RenderNotificationAsync(
        string templateName,
        NotificationRenderModel request,
        ITemplateRenderer renderer)
    {
        if (!Catalog.Contains(templateName))
        {
            return UnknownTemplate(Catalog);
        }

        string rendered = await renderer.RenderAsync(Catalog, templateName, request);
        return TypedResults.Ok(new RenderedNotification(templateName, rendered));
    }

    private static ProblemHttpResult UnknownTemplate(TemplateCatalog catalog) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Unknown template",
            detail: $"templateName must be one of: {string.Join(", ", catalog.Keys.Order(StringComparer.Ordinal))}.",
            extensions: new Dictionary<string, object?> { ["code"] = "TEMPLATE_NOT_ALLOWED" });
}

/// <summary>A rendered notification body.</summary>
public sealed record RenderedNotification(string TemplateName, string Content);
