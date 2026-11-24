using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API route wiring for the signed service-to-service eConsent synchronization webhook
/// (<c>POST /api/v1/execution/webhooks/econsent</c>).
/// </summary>
public static class EconsentWebhookEndpoints
{
    public static IEndpointRouteBuilder MapEconsentWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/execution/webhooks/econsent", ApplyEconsentWebhookAsync)
            .WithTags("Subjects")
            .WithName("EconsentConsentWebhook")
            .Produces<OperationEnvelopeResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("subject_enrollment:econsent_sync")
            .RequireCallerStudyScope()
            .AddEndpointFilter<ValidationFilter<EconsentWebhookPayload>>();

        return app;
    }

    public static async Task<IResult> ApplyEconsentWebhookAsync(
        EconsentWebhookPayload payload,
        IEconsentWebhookApplicationService svc,
        CancellationToken ct,
        HttpContext? http = null)
    {
        ct.ThrowIfCancellationRequested();

        if (ExecutionScopeGuard.RequireStudyAndOptionalSite(http, payload.StudyCode, payload.SiteCode) is { } denied)
        {
            return denied;
        }

        var result = await svc.ApplyConsentSignedAsync(payload, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: _ => Results.Ok(new OperationEnvelopeResponse("success", "Consent status synchronized successfully.")),
            onFailure: e => e.ToProblemResult());
    }
}
