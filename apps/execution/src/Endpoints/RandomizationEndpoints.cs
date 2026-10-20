using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Randomization;
using Cadence.Apps.Execution.Application.Subjects;
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
/// Randomisation routes (#5607, ADR-2640): scheme versions are drafted and approved by unblinded statisticians, and
/// site staff randomise an enrolled participant. No route returns an arm; subject detail reveals it to an unblinded
/// caller only.
/// @req:PRD-SUB-001
/// </summary>
public static class RandomizationEndpoints
{
    public static IEndpointRouteBuilder MapRandomizationEndpoints(this IEndpointRouteBuilder app)
    {
        var schemes = app.MapGroup("/api/v1/execution/randomization/schemes").WithTags("Randomization");

        schemes.MapPost("/", DraftSchemeAsync)
            .WithName("DraftRandomizationScheme")
            .WithSummary("Drafts the study's next randomisation scheme version.")
            .Produces<RandomizationSchemeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("randomization_scheme:draft")
            .AddEndpointFilter<ValidationFilter<RandomizationSchemeDraftRequest>>()
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        schemes.MapPost("/{schemeId:guid}/approve", ApproveSchemeAsync)
            .WithName("ApproveRandomizationScheme")
            .WithSummary("Approves a draft version for go-live, retiring the previous one and generating its list.")
            .Produces<RandomizationSchemeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("randomization_scheme:approve")
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        schemes.MapGet("/", ListSchemesAsync)
            .WithName("ListRandomizationSchemes")
            .WithSummary("Lists a study's scheme versions, newest first, without lists or allocations.")
            .Produces<RandomizationSchemeResponse[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("randomization_scheme:read");

        app.MapGroup("/api/v1/execution/subjects").WithTags("Subjects")
            .MapPost("/{subjectId}/randomize", RandomizeSubjectAsync)
            .WithName("RandomizeSubject")
            .WithSummary("Randomises an enrolled participant under the live scheme and assigns a kit when it needs one.")
            .Produces<RandomizationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("subject_enrollment:randomize")
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        return app;
    }

    /// <summary><c>POST /api/v1/execution/randomization/schemes</c>.</summary>
    public static async Task<IResult> DraftSchemeAsync(
        RandomizationSchemeDraftRequest request, IRandomizationApplicationService svc, CancellationToken ct)
    {
        var result = await svc.DraftSchemeAsync(request, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: r => Results.Created($"/api/v1/execution/randomization/schemes?study_id={Uri.EscapeDataString(r.StudyId.Value)}", r),
            onFailure: e => e.ToProblemResult());
    }

    /// <summary><c>POST /api/v1/execution/randomization/schemes/{schemeId}/approve</c>.</summary>
    public static async Task<IResult> ApproveSchemeAsync(
        Guid schemeId, HttpContext http, IRandomizationApplicationService svc, CancellationToken ct)
    {
        string changeReason = ChangeReasonHeader.Read(http.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter should have rejected this request.");
        var result = await svc.ApproveSchemeAsync(schemeId, changeReason, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary><c>GET /api/v1/execution/randomization/schemes?study_id=</c>.</summary>
    public static async Task<IResult> ListSchemesAsync(
        [FromQuery(Name = "study_id")] string? studyId, IRandomizationApplicationService svc, CancellationToken ct)
    {
        var result = await svc.ListSchemesAsync(studyId, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>POST /api/v1/execution/subjects/{subjectId}/randomize</c>. The caller needs a role that may change a
    /// participant's record (<see cref="SubjectAuthority.AuthorizeParticipantChange"/>).
    /// </summary>
    public static async Task<IResult> RandomizeSubjectAsync(
        string subjectId, RandomizeSubjectRequest request, HttpContext http, IRandomizationApplicationService svc, CancellationToken ct)
    {
        string changeReason = ChangeReasonHeader.Read(http.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter should have rejected this request.");
        if (SubjectAuthority.AuthorizeParticipantChange(http.User.ToAuthenticatedCaller()) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        var result = await svc.RandomizeAsync(subjectId, request, changeReason, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }
}
