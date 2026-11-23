using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Toxicity;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API route definitions for laboratory toxicity governance, review queue, investigator actions, and real-time evaluation.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public static class LabToxicityEndpoints
{
    public const string ReviewActionSemanticAction = "execution.lab_toxicity_review.action";

    public static string ReviewActionPath(string reviewId) => $"/api/v1/execution/labs/toxicity/review/{reviewId}/action";

    private static string ReviewActionFor(HttpContext httpContext) =>
        ReviewActionPath(httpContext.Request.RouteValues["review_id"]?.ToString() ?? string.Empty);

    public static IEndpointRouteBuilder MapLabToxicityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/execution/labs/toxicity")
            .WithTags("Laboratory Governance & Ingestion");

        group.MapGet("/review-queue", ListReviewQueueAsync)
            .WithName("ListStagedAeReviewQueue")
            .WithSummary("List Staged Ae Review Queue")
            .WithDescription("Retrieve laboratory Grade 3/4 toxicity triggers staged for Investigator review.")
            .Produces<IReadOnlyList<StagedAdverseEventReviewResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("lab_toxicity_review:read").RequireCallerStudyScope();

        group.MapPost("/review/{review_id}/action", ExecuteReviewActionAsync)
            .WithName("ActionStagedAeReview")
            .WithSummary("Action Staged Ae Review")
            .WithDescription("Perform Investigator workflow action on a staged Grade 3/4 AE (Confirm, Dismiss, Escalate).")
            .Produces<StagedAdverseEventActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithChangeReasonHeader(required: true)
            .AddEndpointFilter<ChangeReasonHeaderFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("lab_toxicity_review:review").RequireCallerStudyScope()
            .RequireElectronicSignature(ReviewActionSemanticAction, ReviewActionFor);

        group.MapPost("/evaluate", EvaluateToxicityAsync)
            .WithName("EvaluateLabToxicityEndpoint")
            .WithSummary("Evaluate Lab Toxicity Endpoint")
            .WithDescription("Evaluate CTCAE v5.0 Grade, Hy's Law criteria, and delta checks for a single lab observation.")
            .Produces<LabToxicityEvaluateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("lab_range:alert").RequireCallerStudyScope();

        return app;
    }

    public static async Task<IResult> ListReviewQueueAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        [FromQuery(Name = "review_status")] string? reviewStatus,
        HttpContext httpContext,
        ILabToxicityGovernanceService svc,
        CancellationToken ct)
    {
        if (ExecutionScopeGuard.ResolveStudyAndSiteFilters(httpContext, studyId, siteId, out var studyFilter, out var siteFilter, out var effectiveSiteId) is { } denied)
        {
            return denied;
        }

        string status = string.IsNullOrWhiteSpace(reviewStatus) ? "STAGED_FOR_INVESTIGATOR_REVIEW" : reviewStatus;
        var result = await svc.ListQueueAsync(studyId, effectiveSiteId, status, ct).ConfigureAwait(false);
        return result.Match(
            items => Results.Ok(ExecutionScopeGuard.ApplyFilters(items, studyFilter, r => r.StudyId, siteFilter, r => r.SiteId)),
            e => e.ToProblemResult());
    }

    public static async Task<IResult> ExecuteReviewActionAsync(
        [FromRoute(Name = "review_id")] string reviewId,
        StagedAdverseEventActionRequest request,
        HttpContext httpContext,
        ILabToxicityGovernanceService svc,
        CancellationToken ct)
    {
        var existing = await svc.GetReviewByIdAsync(reviewId, ct).ConfigureAwait(false);
        IResult? scopeDenied = existing is not null
            ? ExecutionScopeGuard.RequireStudyAndOptionalSite(
                httpContext,
                existing.StudyId,
                existing.SiteId,
                allowUnassignedSiteForScopedCaller: true)
            : (ExecutionScopeGuard.RequireAnyStudyScope(httpContext)
                ?? ExecutionScopeGuard.ResolveSiteFilter(httpContext, null, out _, out _));
        if (scopeDenied is not null)
        {
            return scopeDenied;
        }

        var result = await svc.ExecuteActionAsync(reviewId, request, httpContext.User.ToAuthenticatedCaller(), ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> EvaluateToxicityAsync(
        LabToxicityEvaluateRequest request,
        HttpContext httpContext,
        ILabToxicityGovernanceService svc,
        CancellationToken ct)
    {
        if (ExecutionScopeGuard.RequireAnyStudyScope(httpContext) is { } denied)
        {
            return denied;
        }

        var result = await svc.EvaluateAsync(request, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }
}
