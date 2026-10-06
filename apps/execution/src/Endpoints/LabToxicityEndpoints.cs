using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Toxicity;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API route definitions for laboratory toxicity governance, review queue, investigator actions, and real-time evaluation.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// @req:SRS-EDC-001
/// </summary>
public static class LabToxicityEndpoints
{
    public static IEndpointRouteBuilder MapLabToxicityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/execution/labs/toxicity")
            .WithTags("Laboratory Governance & Ingestion");

        group.MapGet("/review-queue", ListReviewQueueAsync)
            .WithName("ListStagedAeReviewQueue")
            .WithSummary("List Staged Ae Review Queue")
            .WithDescription("Retrieve laboratory Grade 3/4 toxicity triggers staged for Investigator review.")
            .Produces<IReadOnlyList<StagedAdverseEventReviewResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/review/{review_id}/action", ExecuteReviewActionAsync)
            .WithName("ActionStagedAeReview")
            .WithSummary("Action Staged Ae Review")
            .WithDescription("Perform Investigator workflow action on a staged Grade 3/4 AE (Confirm, Dismiss, Escalate).")
            .Produces<StagedAdverseEventActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/evaluate", EvaluateToxicityAsync)
            .WithName("EvaluateLabToxicityEndpoint")
            .WithSummary("Evaluate Lab Toxicity Endpoint")
            .WithDescription("Evaluate CTCAE v5.0 Grade, Hy's Law criteria, and delta checks for a single lab observation.")
            .Produces<LabToxicityEvaluateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public static async Task<IResult> ListReviewQueueAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        [FromQuery(Name = "review_status")] string? reviewStatus,
        ILabToxicityGovernanceService svc,
        CancellationToken ct)
    {
        string status = string.IsNullOrWhiteSpace(reviewStatus) ? "STAGED_FOR_INVESTIGATOR_REVIEW" : reviewStatus;
        var result = await svc.ListQueueAsync(studyId, siteId, status, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> ExecuteReviewActionAsync(
        [FromRoute(Name = "review_id")] string reviewId,
        StagedAdverseEventActionRequest request,
        HttpContext httpContext,
        ILabToxicityGovernanceService svc,
        CancellationToken ct)
    {
        var result = await svc.ExecuteActionAsync(reviewId, request, httpContext.User.ToAuthenticatedCaller(), ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> EvaluateToxicityAsync(
        LabToxicityEvaluateRequest request,
        ILabToxicityGovernanceService svc,
        CancellationToken ct)
    {
        var result = await svc.EvaluateAsync(request, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }
}
