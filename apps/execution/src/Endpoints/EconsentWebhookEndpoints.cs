using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application;
using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        HttpContext? http = null,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        ct.ThrowIfCancellationRequested();

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(http, payload.StudyCode, payload.SiteCode, code: ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } denied)
        {
            return denied;
        }

        if (await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, payload.StudyCode, payload.SiteCode, payload.SubjectCode, ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        var result = await svc.ApplyConsentSignedAsync(payload, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: _ => Results.Ok(new OperationEnvelopeResponse("success", "Consent status synchronized successfully.")),
            onFailure: e => e.ToProblemResult());
    }
}
