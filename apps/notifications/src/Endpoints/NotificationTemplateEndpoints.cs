using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Infrastructure;
using Cadence.Security.DependencyInjection;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Notifications.Endpoints;

/// <summary>
/// Notification rendering. The caller picks a notification by key from <see cref="Catalog"/>; nothing
/// outside that allow-list can be rendered.
/// </summary>
public static class NotificationTemplateEndpoints
{
    /// <summary>The permission that rendering a notification requires.</summary>
    public const string RenderPermission = "notification:send";

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
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            // Study alerts are sent by sponsor staff and monitors (#4960, ADR-2511).
            .RequirePermission(RenderPermission);

        return group;
    }

    public static async Task<Results<Ok<RenderedNotification>, ProblemHttpResult>> RenderNotificationAsync(
        string templateName,
        NotificationRenderModel request,
        ITemplateRenderer renderer,
        IOptions<NotificationBrandingOptions> branding)
    {
        if (!Catalog.Contains(templateName))
        {
            return UnknownTemplate(Catalog);
        }

        // Branding and the portal link come from configuration, so a caller cannot point a notification's
        // links elsewhere (#4983).
        var options = branding.Value;
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["study_id"] = request.StudyId,
            ["event_id"] = request.EventId,
            ["timestamp_utc"] = request.TimestampUtc,
            ["payload"] = request.Payload,
            ["brand_name"] = options.BrandName,
            ["portal_url"] = string.IsNullOrWhiteSpace(options.PortalUrl) ? null : options.PortalUrl,
        };

        string rendered = await renderer.RenderAsync(Catalog, templateName, model);
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
