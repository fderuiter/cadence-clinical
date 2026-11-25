using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Application;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Econsent.Endpoints;

/// <summary>
/// Minimal API route definitions for eConsent capture and step-up signature verification.
/// Gated by 21 CFR Part 11 step-up e-signature token verification (X-Sig-Token).
/// </summary>
public static class EconsentEndpoints
{
    public const string CaptureConsentSemanticAction = "econsent.consent.capture";

    public static string CaptureConsentAction(string templateId, int versionIndex) =>
        $"/api/v1/econsent/templates/{templateId}/versions/{versionIndex}/capture-consent";

    public static string CaptureConsentLegacyAction(string templateId) =>
        $"/api/v1/econsent/templates/{templateId}/capture-consent";

    public static IEndpointRouteBuilder MapEconsentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/econsent/templates")
            .WithTags("eConsent");

        group.MapPost("/{template_id}/versions/{version_index}/publish", PublishTemplateVersionedAsync)
            .WithName("PublishTemplateVersioned")
            .RequirePermission("econsent_template:publish").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<PublishTemplateResponse>(StatusCodes.Status200OK);

        group.MapPost("/{template_id}/publish", PublishTemplateAsync)
            .WithName("PublishTemplate")
            .RequirePermission("econsent_template:publish").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<PublishTemplateResponse>(StatusCodes.Status200OK);

        group.MapPost("/{template_id}/versions/{version_index}/capture-consent", CaptureConsentAsync)
            .WithName("CaptureConsentVersioned")
            .RequirePermission("econsent_consent:capture").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<CaptureConsentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature(CaptureConsentSemanticAction, CaptureConsentActionFor);

        group.MapPost("/{template_id}/capture-consent", CaptureConsentLegacyAsync)
            .WithName("CaptureConsentLegacy")
            .RequirePermission("econsent_consent:capture").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<CaptureConsentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature(CaptureConsentSemanticAction, CaptureConsentLegacyActionFor);

        return app;
    }

    private static string CaptureConsentActionFor(HttpContext httpContext)
    {
        string templateId = httpContext.Request.RouteValues["template_id"]?.ToString() ?? string.Empty;
        int versionIndex = int.TryParse(httpContext.Request.RouteValues["version_index"]?.ToString(), out int v) ? v : 1;
        return CaptureConsentAction(templateId, versionIndex);
    }

    private static string CaptureConsentLegacyActionFor(HttpContext httpContext)
    {
        string templateId = httpContext.Request.RouteValues["template_id"]?.ToString() ?? string.Empty;
        return CaptureConsentLegacyAction(templateId);
    }

    public static Task<IResult> ProcessCaptureConsentAsync(
        string templateId,
        int versionIndex,
        CaptureConsentRequest request,
        EconsentApplicationService service,
        HttpContext httpContext,
        IOptions<BiometricLivenessOptions> livenessOptions,
        ISigTokenUseStore sigTokenUseStore,
        CancellationToken cancellationToken) =>
        service.CaptureConsentAsync(
            templateId,
            versionIndex,
            request,
            httpContext,
            livenessOptions,
            sigTokenUseStore,
            cancellationToken);

    public static Task<IResult> ProcessPublishTemplateAsync(
        string templateId,
        int versionIndex,
        PublishTemplateRequest? request,
        EconsentApplicationService service,
        IPublishEndpoint publishEndpoint,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        service.PublishTemplateAsync(
            templateId,
            versionIndex,
            request,
            publishEndpoint,
            httpContext,
            cancellationToken);

    private static Task<IResult> CaptureConsentAsync(
        string template_id,
        int version_index,
        CaptureConsentRequest request,
        [FromServices] EconsentApplicationService service,
        HttpContext httpContext,
        IOptions<BiometricLivenessOptions> livenessOptions,
        ISigTokenUseStore sigTokenUseStore,
        CancellationToken cancellationToken) =>
        ProcessCaptureConsentAsync(
            template_id,
            version_index,
            request,
            service,
            httpContext,
            livenessOptions,
            sigTokenUseStore,
            cancellationToken);

    private static Task<IResult> CaptureConsentLegacyAsync(
        string template_id,
        CaptureConsentRequest request,
        [FromServices] EconsentApplicationService service,
        HttpContext httpContext,
        IOptions<BiometricLivenessOptions> livenessOptions,
        ISigTokenUseStore sigTokenUseStore,
        CancellationToken cancellationToken) =>
        ProcessCaptureConsentAsync(
            template_id,
            1,
            request,
            service,
            httpContext,
            livenessOptions,
            sigTokenUseStore,
            cancellationToken);

    private static Task<IResult> PublishTemplateVersionedAsync(
        string template_id,
        int version_index,
        PublishTemplateRequest? request,
        [FromServices] EconsentApplicationService service,
        IPublishEndpoint publishEndpoint,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        ProcessPublishTemplateAsync(template_id, version_index, request, service, publishEndpoint, httpContext, cancellationToken);

    private static Task<IResult> PublishTemplateAsync(
        string template_id,
        PublishTemplateRequest? request,
        [FromServices] EconsentApplicationService service,
        IPublishEndpoint publishEndpoint,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        ProcessPublishTemplateAsync(template_id, 1, request, service, publishEndpoint, httpContext, cancellationToken);
}
