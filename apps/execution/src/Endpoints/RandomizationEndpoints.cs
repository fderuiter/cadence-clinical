using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Randomization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

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
    /// <summary>The semantic action a scheme approval step-up token must carry.</summary>
    public const string ApproveSchemeSemanticAction = "execution.form.approve";

    /// <summary>The semantic action a subject randomization step-up token must carry.</summary>
    public const string RandomizeSubjectSemanticAction = "execution.subject.randomize";

    /// <summary>The concrete action a scheme approval's step-up token is bound to.</summary>
    public static string ApproveSchemeAction(Guid schemeId) => $"/api/v1/execution/randomization/schemes/{schemeId:D}/approve";

    /// <summary>The concrete action a subject randomization's step-up token is bound to.</summary>
    public static string RandomizeSubjectAction(string subjectId) => $"/api/v1/execution/subjects/{subjectId}/randomize";

    public static IEndpointRouteBuilder MapRandomizationEndpoints(this IEndpointRouteBuilder app)
    {
        var schemes = app.MapGroup("/api/v1/execution/randomization/schemes").WithTags("Randomization");

        schemes.MapPost("/", DraftSchemeAsync)
            .WithName("DraftRandomizationScheme")
            .WithSummary("Drafts the study's next randomisation scheme version.")
            .Produces<RandomizationSchemeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("randomization_scheme:draft").RequireCallerStudyScope()
            .AddEndpointFilter<ValidationFilter<RandomizationSchemeDraftRequest>>()
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        schemes.MapPost("/{schemeId:guid}/approve", ApproveSchemeAsync)
            .WithName("ApproveRandomizationScheme")
            .WithSummary("Approves a draft version for go-live, retiring the previous one and generating its list.")
            .Produces<RandomizationSchemeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("randomization_scheme:approve").RequireCallerStudyScope()
            .RequireElectronicSignature(ApproveSchemeSemanticAction, ApproveSchemeActionFor)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        schemes.MapGet("/", ListSchemesAsync)
            .WithName("ListRandomizationSchemes")
            .WithSummary("Lists a study's scheme versions, newest first, without lists or allocations.")
            .Produces<RandomizationSchemeResponse[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("randomization_scheme:read").RequireCallerStudyScope();

        app.MapGroup("/api/v1/execution/subjects").WithTags("Subjects")
            .MapPost("/{subjectId}/randomize", RandomizeSubjectAsync)
            .WithName("RandomizeSubject")
            .WithSummary("Randomises an enrolled participant under the live scheme and assigns a kit when it needs one.")
            .Produces<RandomizationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("subject_enrollment:randomize").RequireCallerStudyScope()
            .RequireElectronicSignature(RandomizeSubjectSemanticAction, RandomizeSubjectActionFor)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        return app;
    }

    private static string ApproveSchemeActionFor(HttpContext context) =>
        Guid.TryParse(context.Request.RouteValues["schemeId"]?.ToString(), CultureInfo.InvariantCulture, out var schemeId)
            ? ApproveSchemeAction(schemeId)
            : string.Empty;

    private static string RandomizeSubjectActionFor(HttpContext context) =>
        RandomizeSubjectAction(context.Request.RouteValues["subjectId"]?.ToString() ?? string.Empty);

    /// <summary><c>POST /api/v1/execution/randomization/schemes</c>.</summary>
    public static async Task<IResult> DraftSchemeAsync(
        RandomizationSchemeDraftRequest request,
        HttpContext http,
        IRandomizationApplicationService svc,
        CancellationToken ct,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        ct.ThrowIfCancellationRequested();

        if (ClinicalScopeGuard.RequireStudy(http, request.StudyId.Value, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, request.StudyId.Value, cancellationToken: ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        var result = await svc.DraftSchemeAsync(request, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: r => Results.Created($"/api/v1/execution/randomization/schemes?study_id={Uri.EscapeDataString(r.StudyId.Value)}", r),
            onFailure: e => e.ToProblemResult());
    }

    /// <summary><c>POST /api/v1/execution/randomization/schemes/{schemeId}/approve</c>.</summary>
    public static async Task<IResult> ApproveSchemeAsync(
        Guid schemeId,
        HttpContext http,
        IRandomizationApplicationService svc,
        CancellationToken ct,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        ct.ThrowIfCancellationRequested();

        string changeReason = ChangeReasonHeader.Read(http.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter should have rejected this request.");

        if (ClinicalScopeGuard.RequireVerifiedTenant(http, ClinicalScopeGuard.ForbiddenCode) is { } tenantDenied)
        {
            return tenantDenied;
        }

        var schemeStudy = await svc.GetSchemeStudyAsync(schemeId, ct).ConfigureAwait(false);
        IResult? scopeDenied = schemeStudy is { } study
            ? ClinicalScopeGuard.RequireStudy(http, study.Value, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            : ClinicalScopeGuard.RequireAnyStudyScope(http, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);
        if (scopeDenied is not null)
        {
            return scopeDenied;
        }

        if (schemeStudy is { } resolvedStudy
            && await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, resolvedStudy.Value, cancellationToken: ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        var result = await svc.ApproveSchemeAsync(schemeId, changeReason, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary><c>GET /api/v1/execution/randomization/schemes?study_id=</c>.</summary>
    public static async Task<IResult> ListSchemesAsync(
        [FromQuery(Name = "study_id")] string? studyId, HttpContext http, IRandomizationApplicationService svc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IResult? scopeDenied = !string.IsNullOrWhiteSpace(studyId)
            ? ClinicalScopeGuard.RequireStudy(http, studyId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            : ClinicalScopeGuard.RequireAnyStudyScope(http, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);
        if (scopeDenied is not null)
        {
            return scopeDenied;
        }

        var result = await svc.ListSchemesAsync(studyId, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>POST /api/v1/execution/subjects/{subjectId}/randomize</c>. The caller needs a role that may change a
    /// participant's record (<see cref="SubjectAuthority.AuthorizeParticipantChange"/>), plus study and site scope
    /// on the loaded participant record.
    /// </summary>
    public static async Task<IResult> RandomizeSubjectAsync(
        string subjectId,
        RandomizeSubjectRequest request,
        HttpContext http,
        IRandomizationApplicationService svc,
        CancellationToken ct,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        ct.ThrowIfCancellationRequested();

        string changeReason = ChangeReasonHeader.Read(http.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter should have rejected this request.");
        var caller = ClinicalScopeGuard.ResolvePrincipal(http) is { UserId.Length: > 0 } p
            ? new AuthenticatedCaller(p.UserId, p.Roles, p.TenantId)
            : null;
        if (SubjectAuthority.AuthorizeParticipantChange(caller) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        if (ClinicalScopeGuard.RequireVerifiedTenant(http, ClinicalScopeGuard.ForbiddenCode) is { } tenantDenied)
        {
            return tenantDenied;
        }

        var subjectScope = await svc.GetSubjectScopeAsync(subjectId, ct).ConfigureAwait(false);
        var subjectLockScope = await svc.GetSubjectLockScopeAsync(subjectId, ct).ConfigureAwait(false);
        IResult? scopeDenied = subjectScope is { } s
            ? ClinicalScopeGuard.RequireStudyAndOptionalSite(
                http,
                s.StudyId.Value,
                s.SiteId?.Value,
                allowUnassignedSiteForScopedCaller: true,
                code: ClinicalScopeGuard.ForbiddenCode,
                requireVerifiedTenant: true)
            : ClinicalScopeGuard.RequireAnyStudyAndSiteScope(http, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);
        if (scopeDenied is not null)
        {
            return scopeDenied;
        }

        string? resolvedStudyCode = subjectLockScope?.StudyCode.Value ?? subjectScope?.StudyId.Value;
        if (resolvedStudyCode is not null
            && await ExecutionLockGuard.EnsureUnlockedAsync(
                lockGuard,
                resolvedStudyCode,
                subjectLockScope?.SiteCode?.Value ?? subjectScope?.SiteId?.Value,
                subjectLockScope?.SubjectCode.Value ?? subjectId,
                ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        var result = await svc.RandomizeAsync(subjectId, request, changeReason, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }
}
