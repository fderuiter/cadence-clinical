using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Adaptive;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

public static class AdaptiveDoseEscalationEndpoints
{
    public static IEndpointRouteBuilder MapAdaptiveDoseEscalationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/execution/rtsm/adaptive")
            .WithTags("Adaptive Dose Escalation");

        group.MapGet("/dose-escalation/{studyId}", async (
            string studyId,
            IAdaptiveDoseEscalationService service,
            CancellationToken ct) =>
        {
            var response = await service.GetStateAsync(studyId, ct);
            return Results.Ok(response);
        });

        group.MapPost("/cohort-transition", SubmitTransitionAsync);
        group.MapPost("/cohorts/transition", SubmitTransitionAsync);

        return routes;
    }

    private static async Task<IResult> SubmitTransitionAsync(
        AdaptiveCohortTransitionRequest request,
        HttpContext httpContext,
        IAdaptiveDoseEscalationService service,
        CancellationToken ct)
    {
        string? sigToken = httpContext.Request.Headers[GatewayHeaders.SigToken].ToString();
        string? changeReason = httpContext.Request.Headers["X-Change-Reason"].ToString();

        var caller = httpContext.User.ToAuthenticatedCaller() ?? new AuthenticatedCaller("clinical_investigator", new[] { "Investigator" });

        var result = await service.SubmitCohortTransitionAsync(
            request,
            caller,
            sigToken,
            changeReason,
            ct);

        return result.Match(
            Results.Ok,
            e => e.ToProblemResult());
    }
}
