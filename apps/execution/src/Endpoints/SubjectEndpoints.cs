using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// `MapSubjectEndpoints` wires all 6 subject routes (create, record-consent + its undocumented plural
/// alias, advance-state, get-detail, update-demographics, delete-demographics) to
/// <see cref="ISubjectApplicationService"/>, per `02_technical_design.md` §3.3. Handler bodies are named
/// `public static` methods (per `AGENTS.md`'s own "Minimal API Route Definition" convention) rather than the
/// inline lambdas §3.3's illustrative code sample uses, so each handler is independently unit-testable
/// (in particular, the <c>CancellationToken</c> boundary check in
/// <c>SubjectEndpointsRoutingTests</c> calls these methods directly) without changing any wire-visible
/// behavior. Route registration itself is this ticket's (`MIG-SUBJ-023`'s) job; wiring
/// <see cref="MapSubjectEndpoints"/> into `Program.cs`'s composition root (plus registering
/// <see cref="ISubjectApplicationService"/>/its validators in DI) is explicitly `MIG-SUBJ-024`'s "final
/// wiring" per its own Target Files entry — this file does not call <see cref="MapSubjectEndpoints"/> from
/// `Program.cs` itself.
/// <para>
/// Every route that changes a participant's record checks the caller's signed roles against
/// <see cref="SubjectAuthority"/> and answers <c>403</c> otherwise (#4943). Subject detail is blinded unless the
/// caller carries the gateway-signed <see cref="CadenceClaimTypes.UnblindedAccess"/> claim.
/// </para>
/// Legacy Python source being replaced: `apps/execution/presentation/routers/subjects.py` (all
/// subject-scoped route handlers, lines 180–773).
/// @req:PRD-SUB-001
/// </summary>
public static partial class SubjectEndpoints
{
    public const string UnblindSubjectSemanticAction = "execution.subject.unblind";
    public const string WithdrawSubjectSemanticAction = "execution.subject.withdraw";

    public static string SubjectStateAction(string subjectId) => $"/api/v1/execution/subjects/{subjectId}/state";
    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Creating subject '{SubjectId}' for study '{StudyId}'")]
    public static partial void LogCreatingSubject(this ILogger logger, string subjectId, string studyId);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Information,
        Message = "Successfully created subject '{SubjectId}' for study '{StudyId}'")]
    public static partial void LogCreatedSubject(this ILogger logger, string subjectId, string studyId);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Recording consent for subject '{SubjectId}', study '{StudyId}', version '{VersionTag}'")]
    public static partial void LogRecordingConsent(this ILogger logger, string subjectId, string? studyId, string? versionTag);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Information,
        Message = "Advancing state for subject '{SubjectId}' to '{TargetState}' with reason '{ChangeReason}'")]
    public static partial void LogAdvancingSubjectState(this ILogger logger, string subjectId, string? targetState, string changeReason);

    public static IEndpointRouteBuilder MapSubjectEndpoints(this IEndpointRouteBuilder app)
    {
        // `.WithOpenApi()` from the design doc's own §3.3 sample is deliberately not reproduced here: it is
        // obsolete (ASPDEPR002) under this repo's pinned .NET 10 SDK/ASP.NET Core version and this solution
        // treats warnings (including deprecation diagnostics) as build errors -- the newer
        // `Microsoft.AspNetCore.OpenApi` document-generation pipeline (`AddOpenApi()`/`MapOpenApi()`, already
        // registered/mapped by whichever ticket wires `Program.cs`) needs no per-group opt-in call to include
        // these routes, so dropping it changes no observable behavior.
        var group = app.MapGroup("/api/v1/execution/subjects")
            .WithTags("Subjects");

        group.MapPost("/", CreateSubjectAsync)
            .WithName("CreateSubject")
            .Produces<SubjectResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("subject_enrollment:create").RequireCallerStudyScope()
            .RequireChangeReasonHeader()
            .AddEndpointFilter<ValidationFilter<SubjectCreateRequest>>();

        group.MapPost("/{subjectId}/consent", RecordSubjectConsentAsync)
            .WithName("RecordSubjectConsent")
            .Produces<SubjectConsentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("subject_enrollment:update").RequireCallerStudyScope()
            .RequireChangeReasonHeader()
            .AddEndpointFilter<ValidationFilter<SubjectConsentRequest>>();

        // Legacy dual-route compatibility alias (`01_spec.md` §3 trap table): the legacy router declared
        // both `/consent` and `/consents` for the exact same handler. Removing the plural route would be a
        // breaking API change out of this ticket's scope, so it is reproduced here as a deliberate, minimal,
        // undocumented alias. Reusing the very same `RecordSubjectConsentAsync` method group (rather than a
        // second, textually-duplicated handler) is what makes AC's "behaves identically to `/consent`"
        // requirement structurally guaranteed rather than merely maintained-by-convention; the design doc's
        // own §3.3 illustrative sample instead returns the un-matched `Task<Result<SubjectConsentResponse>>`
        // directly for the alias (skipping both `.Match(...)` and `ValidationFilter<T>`), which would make
        // the alias always answer 200 with the raw `Result<T>` envelope shape and skip request validation —
        // observably *not* identical to `/consent` for a failure or an invalid body. Applying the same
        // handler and the same `ValidationFilter<SubjectConsentRequest>` here closes that gap. Excluded from
        // the generated OpenAPI document since it was never a documented, public contract — only a
        // legacy accidental-dual-registration quirk existing callers may still depend on.
        group.MapPost("/{subjectId}/consents", RecordSubjectConsentAsync)
            .WithName("RecordSubjectConsentAlias")
            .RequirePermission("subject_enrollment:update").RequireCallerStudyScope()
            .RequireChangeReasonHeader()
            .AddEndpointFilter<ValidationFilter<SubjectConsentRequest>>()
            .ExcludeFromDescription();

        group.MapPatch("/{subjectId}/state", AdvanceSubjectStateAsync)
            .WithName("UpdateSubjectState")
            .Produces<SubjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission("subject_enrollment:update").RequireCallerStudyScope()
            .AddEndpointFilter<ValidationFilter<SubjectStateUpdateRequest>>()
            .RequireChangeReasonHeader()
            .AddEndpointFilter(ValidateSubjectStateAuthorityAndJustificationAsync)
            .RequireElectronicSignatureWhen(
                RequiresStepUpSignatureForStateTransition,
                ResolveStateTransitionSemanticAction,
                ResolveStateTransitionConcreteAction);

        group.MapPatch("/{subjectId}/site", AssignSubjectSiteAsync)
            .WithName("AssignSubjectSite")
            .WithSummary("Assigns a clinical site to a screening subject and synchronizes screening enrollment with the kernel.")
            .Produces<SubjectResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("subject_enrollment:update").RequireCallerStudyScope()
            .AddEndpointFilter<ValidationFilter<SubjectSiteAssignmentRequest>>()
            .RequireChangeReasonHeader();

        group.MapGet("/", ListSubjectsAsync)
            .WithName("ListSubjects")
            .WithSummary("Lists one page of a study's subjects in enrollment order.")
            .Produces<SubjectListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("subject_enrollment:read").RequireCallerStudyScope();

        group.MapGet("/counts", GetSubjectCountsAsync)
            .WithName("GetSubjectCounts")
            .WithSummary("Counts a study's subjects grouped by lifecycle status.")
            .Produces<SubjectCountsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("subject_enrollment:read").RequireCallerStudyScope();

        group.MapGet("/{subjectId}", GetSubjectDetailAsync)
            .WithName("GetSubjectDetail")
            .Produces<SubjectDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("subject_enrollment:read").RequireCallerStudyScope();

        group.MapPut("/{subjectId}/demographics", UpdateSubjectDemographicsAsync)
            .WithName("UpdateSubjectDemographics")
            .Produces<SubjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission("subject_enrollment:update").RequireCallerStudyScope()
            .RequireChangeReasonHeader();

        group.MapDelete("/{subjectId}/demographics", DeleteSubjectDemographicsAsync)
            .WithName("DeleteSubjectDemographics")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("subject_enrollment:update").RequireCallerStudyScope()
            .RequireChangeReasonHeader();

        return app;
    }

    /// <summary>
    /// <c>POST /api/v1/execution/subjects</c>. <see cref="ValidationFilter{TRequest}"/> guarantees
    /// <paramref name="request"/> already passed <see cref="SubjectCreateRequestValidator"/> by the time this
    /// runs. Success maps to <c>201 Created</c> with a <c>Location</c> header built from the domain subject
    /// code (<see cref="SubjectResponse.SubjectId_"/>), per `02_technical_design.md` §3.3.
    /// </summary>
    public static async Task<IResult> CreateSubjectAsync(
        SubjectCreateRequest request,
        HttpContext http,
        ISubjectApplicationService svc,
        CancellationToken ct,
        [FromServices] ILogger? logger = null,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        ct.ThrowIfCancellationRequested();

        if (SubjectAuthority.AuthorizeParticipantChange(ResolveCaller(http)) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(
                http,
                request.StudyId,
                request.SiteId,
                allowUnassignedSiteForScopedCaller: true,
                code: ClinicalScopeGuard.ForbiddenCode,
                requireVerifiedTenant: true) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (await ExecutionLockGuard.EnsureUnlockedAsync(
                lockGuard,
                request.StudyId,
                request.SiteId,
                request.SubjectId,
                ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        if (logger != null)
        {
            logger.LogCreatingSubject(request.SubjectId, request.StudyId);
        }

        var result = await svc.CreateAsync(request, ct).ConfigureAwait(false);

        if (logger != null && result.IsSuccess && result.Value is { } response)
        {
            logger.LogCreatedSubject(response.SubjectId_.Value, response.StudyId.Value);
        }

        return result.Match(
            onSuccess: r => Results.Created($"/api/v1/execution/subjects/{r.SubjectId_}", r),
            onFailure: e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>POST /api/v1/execution/subjects/{subjectId}/consent</c>, and the undocumented plural
    /// <c>/consents</c> alias registered against this same method group (see
    /// <see cref="MapSubjectEndpoints"/>'s own remarks).
    /// </summary>
    public static async Task<IResult> RecordSubjectConsentAsync(
        string subjectId,
        SubjectConsentRequest request,
        HttpContext http,
        ISubjectApplicationService svc,
        CancellationToken ct,
        [FromServices] ILogger? logger = null,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        ct.ThrowIfCancellationRequested();

        if (SubjectAuthority.AuthorizeParticipantChange(ResolveCaller(http)) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        if (!string.IsNullOrWhiteSpace(request.ProtocolVersion?.StudyId)
            && ClinicalScopeGuard.RequireStudy(http, request.ProtocolVersion.StudyId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } requestStudyDenied)
        {
            return requestStudyDenied;
        }

        var (scopeDenied, resolvedScope) = await GuardSubjectAndResolveLockScopeAsync(subjectId, http, svc, ct).ConfigureAwait(false);
        if (scopeDenied is not null)
        {
            return scopeDenied;
        }

        if (await ExecutionLockGuard.EnsureUnlockedAsync(
                lockGuard,
                resolvedScope?.StudyCode ?? request.ProtocolVersion?.StudyId,
                resolvedScope?.SiteCode,
                resolvedScope?.SubjectCode,
                ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        if (logger != null)
        {
            logger.LogRecordingConsent(subjectId, request.ProtocolVersion?.StudyId, request.ProtocolVersion?.VersionTag);
        }

        var result = await svc.RecordConsentAsync(subjectId, request, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>PATCH /api/v1/execution/subjects/{subjectId}/state</c>. <see cref="ChangeReasonHeaderFilter"/> runs
    /// ahead of this handler and short-circuits with a <c>400</c> whenever <c>X-Change-Reason</c> is absent or
    /// whitespace-only, so <paramref name="svc"/>'s <see cref="ISubjectApplicationService.AdvanceStateAsync"/>
    /// is never reached for such a request. The <c>throw</c> below is unreachable in production traffic for
    /// exactly that reason — it exists only to fail loudly (rather than silently miscompute a reason of
    /// <c>""</c>) if this filter is ever removed from the route without updating this delegate, matching
    /// `02_technical_design.md` §3.3's own reference implementation for this route verbatim. The caller needs a
    /// role that may change a participant's record, and an unblinding authority when the target is
    /// <c>UNBLINDED</c> (<see cref="SubjectAuthority.AuthorizeStateChange"/>).
    /// </summary>
    public static async Task<IResult> AdvanceSubjectStateAsync(
        string subjectId,
        SubjectStateUpdateRequest request,
        HttpContext http,
        ISubjectApplicationService svc,
        CancellationToken ct,
        [FromServices] ILogger? logger = null,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        ct.ThrowIfCancellationRequested();

        string changeReason = ChangeReasonHeader.Read(http.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter should have rejected this request.");

        if (SubjectAuthority.AuthorizeStateChange(ResolveCaller(http), request.Target) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        var (scopeDenied, resolvedScope) = await GuardSubjectAndResolveLockScopeAsync(subjectId, http, svc, ct).ConfigureAwait(false);
        if (scopeDenied is not null)
        {
            return scopeDenied;
        }

        if (resolvedScope is { } ls
            && await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, ls.StudyCode, ls.SiteCode, ls.SubjectCode, ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        if (logger != null)
        {
            logger.LogAdvancingSubjectState(subjectId, request.Target, changeReason);
        }

        var result = await svc.AdvanceStateAsync(subjectId, request, changeReason, ct).ConfigureAwait(false);
        return result.Match(r => Results.Ok(r), e => e.ToProblemResult());
    }

    private static async Task<(IResult? Denied, (string StudyCode, string? SiteCode, string SubjectCode)? Scope)> GuardSubjectAndResolveLockScopeAsync(
        string subjectId, HttpContext http, ISubjectApplicationService svc, CancellationToken ct)
    {
        if (ClinicalScopeGuard.RequireVerifiedTenant(http, ClinicalScopeGuard.ForbiddenCode) is { } tenantDenied)
        {
            return (tenantDenied, null);
        }

        var scope = await svc.GetSubjectScopeAsync(subjectId, ct).ConfigureAwait(false);
        var lockScope = await svc.GetSubjectLockScopeAsync(subjectId, ct).ConfigureAwait(false);
        if (scope is { } s)
        {
            var denied = ClinicalScopeGuard.RequireStudyAndOptionalSite(
                http,
                s.StudyId.Value,
                s.SiteId?.Value,
                allowUnassignedSiteForScopedCaller: true,
                code: ClinicalScopeGuard.ForbiddenCode,
                requireVerifiedTenant: true);
            return (denied, (s.StudyId.Value, s.SiteId?.Value, lockScope?.SubjectCode.Value ?? subjectId));
        }

        if (lockScope is { } ls)
        {
            var denied = ClinicalScopeGuard.RequireStudyAndOptionalSite(
                http,
                ls.StudyCode.Value,
                ls.SiteCode?.Value,
                allowUnassignedSiteForScopedCaller: true,
                code: ClinicalScopeGuard.ForbiddenCode,
                requireVerifiedTenant: true);
            return (denied, (ls.StudyCode.Value, ls.SiteCode?.Value, ls.SubjectCode.Value));
        }

        return (GuardMissingRecordScope(http), null);
    }

    private static IResult? GuardMissingRecordScope(HttpContext http) =>
        ClinicalScopeGuard.RequireAnyStudyAndSiteScope(http, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);

    private const string TargetSubjectStateItemKey = "Cadence.Execution.TargetSubjectState";

    private static ValueTask<object?> ValidateSubjectStateAuthorityAndJustificationAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<SubjectStateUpdateRequest>().FirstOrDefault();
        if (SubjectAuthority.AuthorizeStateChange(ResolveCaller(context.HttpContext), request?.Target) is { } forbidden)
        {
            return ValueTask.FromResult<object?>(forbidden.ToProblemResult());
        }

        if (request is not null && SubjectStateCodec.TryParse(request.Target, out var target))
        {
            string changeReason = ChangeReasonHeader.Read(context.HttpContext.Request) ?? string.Empty;
            if (target == SubjectState.Unblinded
                && changeReason.Trim().Length < SubjectAuthority.MinimumUnblindingReasonLength)
            {
                return ValueTask.FromResult<object?>(
                    SubjectErrors.UnblindingReasonTooShort(SubjectAuthority.MinimumUnblindingReasonLength).ToProblemResult());
            }

            if (target == SubjectState.Withdrawn
                && changeReason.Trim().Length < SubjectAuthority.MinimumWithdrawalReasonLength)
            {
                return ValueTask.FromResult<object?>(
                    SubjectErrors.WithdrawalReasonTooShort(SubjectAuthority.MinimumWithdrawalReasonLength).ToProblemResult());
            }
        }

        return next(context);
    }

    private static bool RequiresStepUpSignatureForStateTransition(EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<SubjectStateUpdateRequest>().FirstOrDefault();
        if (request is not null && SubjectStateCodec.TryParse(request.Target, out var target)
            && target is SubjectState.Unblinded or SubjectState.Withdrawn)
        {
            context.HttpContext.Items[TargetSubjectStateItemKey] = target;
            return true;
        }

        return false;
    }

    private static string ResolveStateTransitionSemanticAction(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(TargetSubjectStateItemKey, out var value)
            && value is SubjectState state
            && state == SubjectState.Unblinded)
        {
            return UnblindSubjectSemanticAction;
        }

        return WithdrawSubjectSemanticAction;
    }

    private static string ResolveStateTransitionConcreteAction(HttpContext httpContext) =>
        SubjectStateAction(httpContext.Request.RouteValues["subjectId"]?.ToString() ?? string.Empty);

    private static AuthenticatedCaller? ResolveCaller(HttpContext http) =>
        ClinicalScopeGuard.ResolvePrincipal(http) is { UserId.Length: > 0 } p
            ? new AuthenticatedCaller(p.UserId, p.Roles, p.TenantId)
            : null;
}
