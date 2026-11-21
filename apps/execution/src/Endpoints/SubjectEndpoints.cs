using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Debug,
        Message = "Fetching detail for subject '{SubjectId}' (isBlindedCaller: {IsBlindedCaller})")]
    public static partial void LogFetchingSubjectDetail(this ILogger logger, string subjectId, bool isBlindedCaller);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Information,
        Message = "Updating demographics for subject '{SubjectId}' with reason '{ChangeReason}'")]
    public static partial void LogUpdatingSubjectDemographics(this ILogger logger, string subjectId, string changeReason);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Information,
        Message = "Deleting demographics for subject '{SubjectId}' with reason '{ChangeReason}'")]
    public static partial void LogDeletingSubjectDemographics(this ILogger logger, string subjectId, string changeReason);

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Information,
        Message = "Issuing 30-day BYOD activation code for subject '{SubjectId}' with reason '{ChangeReason}'")]
    public static partial void LogIssuingSubjectActivationCode(this ILogger logger, string subjectId, string changeReason);

    [LoggerMessage(
        EventId = 3009,
        Level = LogLevel.Information,
        Message = "Redeeming BYOD activation code with reason '{ChangeReason}'")]
    public static partial void LogRedeemingSubjectActivationCode(this ILogger logger, string changeReason);

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
            .AddEndpointFilter<ValidationFilter<SubjectCreateRequest>>();

        group.MapPost("/{subjectId}/consent", RecordSubjectConsentAsync)
            .WithName("RecordSubjectConsent")
            .Produces<SubjectConsentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("subject_enrollment:update").RequireCallerStudyScope()
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
            .AddEndpointFilter<ValidationFilter<SubjectConsentRequest>>()
            .ExcludeFromDescription();

        group.MapPatch("/{subjectId}/state", AdvanceSubjectStateAsync)
            .WithName("UpdateSubjectState")
            .Produces<SubjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission("subject_enrollment:update").RequireCallerStudyScope()
            .AddEndpointFilter<ValidationFilter<SubjectStateUpdateRequest>>()
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapGet("/", ListSubjectsAsync)
            .WithName("ListSubjects")
            .WithSummary("Lists one page of a study's subjects in enrollment order.")
            .Produces<SubjectListResponse>(StatusCodes.Status200OK)
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
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapDelete("/{subjectId}/demographics", DeleteSubjectDemographicsAsync)
            .WithName("DeleteSubjectDemographics")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("subject_enrollment:update").RequireCallerStudyScope()
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{subjectId}/activation-code", IssueSubjectActivationCodeAsync)
            .WithName("IssueSubjectActivationCode")
            .Produces<SubjectActivationCodeIssueResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("subject_enrollment:update").RequireCallerStudyScope()
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/activation-codes/redeem", RedeemSubjectActivationCodeAsync)
            .WithName("RedeemSubjectActivationCode")
            .Produces<SubjectActivationCodeRedeemResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission("ecoa_activation:redeem").RequireTenantScope()
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapGet("/activation-codes/me", GetMyRedeemedActivationCodeAsync)
            .WithName("GetMyRedeemedActivationCode")
            .Produces<SubjectActivationCodeRedeemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission("ecoa_activation:redeem").RequireTenantScope();

        return app;
    }

    /// <summary>
    /// <c>POST /api/v1/execution/subjects</c>. <see cref="ValidationFilter{TRequest}"/> guarantees
    /// <paramref name="request"/> already passed <see cref="SubjectCreateRequestValidator"/> by the time this
    /// runs. Success maps to <c>201 Created</c> with a <c>Location</c> header built from the domain subject
    /// code (<see cref="SubjectResponse.SubjectId_"/>), per `02_technical_design.md` §3.3.
    /// </summary>
    public static async Task<IResult> CreateSubjectAsync(
        SubjectCreateRequest request, HttpContext http, ISubjectApplicationService svc, CancellationToken ct, [FromServices] ILogger? logger = null)
    {
        ct.ThrowIfCancellationRequested();

        if (SubjectAuthority.AuthorizeParticipantChange(http.User.ToAuthenticatedCaller()) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        if (ExecutionScopeGuard.RequireStudyAndOptionalSite(
                http,
                request.StudyId,
                request.SiteId,
                allowUnassignedSiteForScopedCaller: true) is { } scopeDenied)
        {
            return scopeDenied;
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
        string subjectId, SubjectConsentRequest request, HttpContext http, ISubjectApplicationService svc, CancellationToken ct, [FromServices] ILogger? logger = null)
    {
        ct.ThrowIfCancellationRequested();

        if (SubjectAuthority.AuthorizeParticipantChange(http.User.ToAuthenticatedCaller()) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        if (!string.IsNullOrWhiteSpace(request.ProtocolVersion?.StudyId)
            && ExecutionScopeGuard.RequireStudy(http, request.ProtocolVersion.StudyId) is { } requestStudyDenied)
        {
            return requestStudyDenied;
        }

        if (await GuardSubjectScopeAsync(subjectId, http, svc, ct).ConfigureAwait(false) is { } scopeDenied)
        {
            return scopeDenied;
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
        string subjectId, SubjectStateUpdateRequest request, HttpContext http, ISubjectApplicationService svc, CancellationToken ct, [FromServices] ILogger? logger = null)
    {
        ct.ThrowIfCancellationRequested();

        string changeReason = ChangeReasonHeader.Read(http.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter should have rejected this request.");

        if (SubjectAuthority.AuthorizeStateChange(http.User.ToAuthenticatedCaller(), request.Target) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        if (await GuardSubjectScopeAsync(subjectId, http, svc, ct).ConfigureAwait(false) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (logger != null)
        {
            logger.LogAdvancingSubjectState(subjectId, request.Target, changeReason);
        }

        var result = await svc.AdvanceStateAsync(subjectId, request, changeReason, ct).ConfigureAwait(false);
        return result.Match(r => Results.Ok(r), e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>GET /api/v1/execution/subjects?study_id=&amp;site_id=&amp;status=&amp;q=&amp;limit=&amp;offset=</c>. The list
    /// carries no treatment group, so it needs no blinding check.
    /// </summary>
    public static async Task<IResult> ListSubjectsAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "offset")] int? offset,
        HttpContext http,
        ISubjectApplicationService svc,
        CancellationToken ct,
        [FromQuery(Name = "q")] string? q = null)
    {
        ct.ThrowIfCancellationRequested();

        IResult? studyDenied = !string.IsNullOrWhiteSpace(studyId)
            ? ExecutionScopeGuard.RequireStudy(http, studyId)
            : ExecutionScopeGuard.RequireAnyStudyScope(http);
        if (studyDenied is not null)
        {
            return studyDenied;
        }

        if (ExecutionScopeGuard.ResolveSiteFilter(http, siteId, out var siteFilter, out _) is { } siteDenied)
        {
            return siteDenied;
        }

        var result = await svc.ListAsync(
                studyId,
                siteId,
                status,
                limit,
                offset,
                ct,
                string.IsNullOrWhiteSpace(siteId) ? siteFilter : null,
                q)
            .ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>GET /api/v1/execution/subjects/{subjectId}</c>. Blinded by default: the treatment group and kit are revealed
    /// from the randomisation allocation only to a caller whose signed identity carries
    /// <see cref="CadenceClaimTypes.UnblindedAccess"/>, which the gateway sets from the identity provider (#4943, #5607).
    /// Study and site scope on the loaded <see cref="ClinicalSubject"/> are verified before <c>RevealAsync</c> runs.
    /// </summary>
    public static async Task<IResult> GetSubjectDetailAsync(
        string subjectId, HttpContext http, ISubjectApplicationService svc, IRandomizationApplicationService randomization,
        CancellationToken ct, [FromServices] ILogger? logger = null)
    {
        ct.ThrowIfCancellationRequested();

        bool isBlindedCaller = !IsUnblindedCaller(http.User);

        if (logger != null)
        {
            logger.LogFetchingSubjectDetail(subjectId, isBlindedCaller);
        }

        var result = await svc.GetDetailAsync(subjectId, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return GuardMissingRecordScope(http) ?? result.Error!.ToProblemResult();
        }

        var detail = result.Value!;
        if (ExecutionScopeGuard.RequireStudyAndOptionalSite(
                http,
                detail.StudyId.Value,
                detail.SiteId?.Value,
                allowUnassignedSiteForScopedCaller: true) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (isBlindedCaller)
        {
            return Results.Ok(detail);
        }

        var revealed = await randomization.RevealAsync(subjectId, ct).ConfigureAwait(false);
        return Results.Ok(revealed is null
            ? detail
            : detail with { TreatmentGroup = revealed.Arm, InvestigationalProductId = revealed.KitNumber });
    }

    /// <summary>
    /// <c>PUT /api/v1/execution/subjects/{subjectId}/demographics</c>. The null-forgiving <c>!</c> below is
    /// provably safe, not a suppressed nullable warning: <see cref="ChangeReasonHeaderFilter"/> (applied to
    /// this route, see <see cref="MapSubjectEndpoints"/>) guarantees <c>X-Change-Reason</c> is present and
    /// non-whitespace before this delegate ever runs — matching that filter's own doc comment and
    /// `02_technical_design.md` §3.3's reference implementation for this exact route.
    /// </summary>
    public static async Task<IResult> UpdateSubjectDemographicsAsync(
        string subjectId, SubjectDemographicsUpdateRequest request, HttpContext http, ISubjectApplicationService svc, CancellationToken ct, [FromServices] ILogger? logger = null)
    {
        ct.ThrowIfCancellationRequested();

        string changeReason = ChangeReasonHeader.Read(http.Request)!;

        if (SubjectAuthority.AuthorizeParticipantChange(http.User.ToAuthenticatedCaller()) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        if (await GuardSubjectScopeAsync(subjectId, http, svc, ct).ConfigureAwait(false) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (logger != null)
        {
            logger.LogUpdatingSubjectDemographics(subjectId, changeReason);
        }

        var result = await svc.UpdateDemographicsAsync(subjectId, request, changeReason, ct).ConfigureAwait(false);
        return result.Match(r => Results.Ok(r), e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>DELETE /api/v1/execution/subjects/{subjectId}/demographics</c>, closing MIG-SUBJ-022's noted
    /// `!`-safety gap: this route did not exist before this ticket, so <see cref="ChangeReasonHeaderFilter"/>
    /// is applied here for the first time rather than retrofitted. The null-forgiving <c>!</c> below has the
    /// exact same justification as <see cref="UpdateSubjectDemographicsAsync"/>'s.
    /// </summary>
    public static async Task<IResult> DeleteSubjectDemographicsAsync(
        string subjectId, HttpContext http, ISubjectApplicationService svc, CancellationToken ct, [FromServices] ILogger? logger = null)
    {
        ct.ThrowIfCancellationRequested();

        string changeReason = ChangeReasonHeader.Read(http.Request)!;

        if (SubjectAuthority.AuthorizeParticipantChange(http.User.ToAuthenticatedCaller()) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        if (await GuardSubjectScopeAsync(subjectId, http, svc, ct).ConfigureAwait(false) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (logger != null)
        {
            logger.LogDeletingSubjectDemographics(subjectId, changeReason);
        }

        var result = await svc.DeleteDemographicsAsync(subjectId, changeReason, ct).ConfigureAwait(false);
        return result.Match(r => Results.Ok(r), e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>POST /api/v1/execution/subjects/{subjectId}/activation-code</c>. Issues a single-use 30-day BYOD
    /// activation code for <paramref name="subjectId"/>, storing only its keyed HMAC-SHA256 digest at rest.
    /// Restricted to investigators and clinical research coordinators via <see cref="SubjectAuthority.AuthorizeParticipantChange"/>.
    /// </summary>
    public static async Task<IResult> IssueSubjectActivationCodeAsync(
        string subjectId, HttpContext http, ISubjectApplicationService svc, CancellationToken ct, [FromServices] ILogger? logger = null)
    {
        ct.ThrowIfCancellationRequested();

        string changeReason = ChangeReasonHeader.Read(http.Request)!;

        if (SubjectAuthority.AuthorizeParticipantChange(http.User.ToAuthenticatedCaller()) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        if (await GuardSubjectScopeAsync(subjectId, http, svc, ct).ConfigureAwait(false) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (logger != null)
        {
            logger.LogIssuingSubjectActivationCode(subjectId, changeReason);
        }

        var result = await svc.IssueActivationCodeAsync(subjectId, changeReason, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: r => Results.Created($"/api/v1/execution/subjects/{r.SubjectId}", r),
            onFailure: e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>POST /api/v1/execution/subjects/activation-codes/redeem</c>. Redeems a 30-day BYOD activation code
    /// for the authenticated participant and binds their hashed principal identifier to the participant's <see cref="SubjectCode"/>.
    /// </summary>
    public static async Task<IResult> RedeemSubjectActivationCodeAsync(
        SubjectActivationCodeRedeemRequest request, HttpContext http, ISubjectApplicationService svc, CancellationToken ct, [FromServices] ILogger? logger = null)
    {
        ct.ThrowIfCancellationRequested();

        string changeReason = ChangeReasonHeader.Read(http.Request)!;

        if (string.IsNullOrWhiteSpace(request.ActivationCode))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["activation_code"] = ["Activation code is required."],
            });
        }

        string? callerPrincipalId = ResolveCallerPrincipalId(http.User);
        if (string.IsNullOrWhiteSpace(callerPrincipalId))
        {
            return Results.Problem("Authenticated principal identifier is required.", statusCode: StatusCodes.Status403Forbidden);
        }

        if (logger != null)
        {
            logger.LogRedeemingSubjectActivationCode(changeReason);
        }

        var result = await svc.RedeemActivationCodeAsync(request.ActivationCode, callerPrincipalId, changeReason, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>GET /api/v1/execution/subjects/activation-codes/me</c>. Resolves the participant <see cref="SubjectCode"/>
    /// bound to the authenticated caller from a previously redeemed BYOD activation code.
    /// </summary>
    public static async Task<IResult> GetMyRedeemedActivationCodeAsync(
        HttpContext http, ISubjectApplicationService svc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string? callerPrincipalId = ResolveCallerPrincipalId(http.User);
        if (string.IsNullOrWhiteSpace(callerPrincipalId))
        {
            return Results.Problem("Authenticated principal identifier is required.", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await svc.GetRedeemedActivationAsync(callerPrincipalId, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    private static string? ResolveCallerPrincipalId(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private static async Task<IResult?> GuardSubjectScopeAsync(
        string subjectId, HttpContext http, ISubjectApplicationService svc, CancellationToken ct)
    {
        var scope = await svc.GetSubjectScopeAsync(subjectId, ct).ConfigureAwait(false);
        return scope is { } s
            ? ExecutionScopeGuard.RequireStudyAndOptionalSite(
                http,
                s.StudyId.Value,
                s.SiteId?.Value,
                allowUnassignedSiteForScopedCaller: true)
            : GuardMissingRecordScope(http);
    }

    private static IResult? GuardMissingRecordScope(HttpContext http) =>
        ExecutionScopeGuard.RequireAnyStudyScope(http)
        ?? ExecutionScopeGuard.ResolveSiteFilter(http, null, out _, out _);

    /// <summary>Whether <paramref name="user"/> carries the gateway-signed unblinded-access claim.</summary>
    private static bool IsUnblindedCaller(ClaimsPrincipal user) =>
        user.Identity is { IsAuthenticated: true }
        && string.Equals(user.FindFirst(CadenceClaimTypes.UnblindedAccess)?.Value, "true", StringComparison.OrdinalIgnoreCase);
}
