using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Toxicity;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

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
        if (ClinicalScopeGuard.ResolveStudyAndSingleSiteFilters(httpContext, studyId, siteId, out var studyFilter, out var siteFilter, out var effectiveSiteId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } denied)
        {
            return denied;
        }

        string status = string.IsNullOrWhiteSpace(reviewStatus) ? "STAGED_FOR_INVESTIGATOR_REVIEW" : reviewStatus;
        var result = await svc.ListQueueAsync(studyId, effectiveSiteId, status, ct).ConfigureAwait(false);
        return result.Match(
            items => Results.Ok(ClinicalScopeGuard.ApplyFilters(items, studyFilter, siteFilter, r => r.StudyId, r => r.SiteId).ToList()),
            e => e.ToProblemResult());
    }

    public static async Task<IResult> ExecuteReviewActionAsync(
        [FromRoute(Name = "review_id")] string reviewId,
        StagedAdverseEventActionRequest request,
        HttpContext httpContext,
        ILabToxicityGovernanceService svc,
        CancellationToken ct)
    {
        if (ClinicalScopeGuard.RequireVerifiedTenant(httpContext, ClinicalScopeGuard.ForbiddenCode) is { } tenantDenied)
        {
            return tenantDenied;
        }

        var existing = await svc.GetReviewByIdAsync(reviewId, ct).ConfigureAwait(false);
        IResult? scopeDenied = existing is not null
            ? ClinicalScopeGuard.RequireStudyAndOptionalSite(
                httpContext,
                existing.StudyId,
                existing.SiteId,
                allowUnassignedSiteForScopedCaller: true,
                code: ClinicalScopeGuard.ForbiddenCode,
                requireVerifiedTenant: true)
            : ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);
        if (scopeDenied is not null)
        {
            return scopeDenied;
        }

        var caller = ClinicalScopeGuard.ResolvePrincipal(httpContext) is { UserId.Length: > 0 } p
            ? new AuthenticatedCaller(p.UserId, p.Roles)
            : null;
        var result = await svc.ExecuteActionAsync(reviewId, request, caller, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> EvaluateToxicityAsync(
        LabToxicityEvaluateRequest request,
        HttpContext httpContext,
        ILabToxicityGovernanceService svc,
        CancellationToken ct)
    {
        if (ClinicalScopeGuard.RequireAnyStudyScope(httpContext, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } denied)
        {
            return denied;
        }

        var result = await svc.EvaluateAsync(request, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }
}
