using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.Authentication;

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
/// <b>Cross-cutting gap, flagged rather than silently invented (see <see cref="GetSubjectDetailAsync"/>):</b>
/// `03_tickets.md`'s own text for this ticket names an "existing platform abstraction",
/// <c>ICurrentUserAccessor.HasBlindedRole</c>, for resolving <c>GET /{subjectId}</c>'s <c>isBlindedCaller</c>
/// flag, but instructs implementers to verify it actually exists before assuming the placeholder name is
/// final. A solution-wide search (every <c>.cs</c> file, not just `apps/execution`) for
/// <c>ICurrentUserAccessor</c>/<c>HasBlindedRole</c>/<c>BlindedRole</c>/<c>IsBlinded</c> found zero matches:
/// no such accessor exists anywhere — not in `Cadence.Apps.Gateway`, not in `Cadence.Apps.Designer`, not in
/// any shared `packages/` project. Per the ticket's own instruction, this is a cross-cutting RBAC gap to
/// flag back to the technical design, not to silently paper over with a new one-off named interface inside
/// this slice (unlike `MIG-SUBJ-017`'s <see cref="Cadence.Apps.Execution.Application.Abstractions.ICurrentAuditContextAccessor"/>,
/// which the design doc did establish as the vetted, cross-cutting standard). See
/// <see cref="GetSubjectDetailAsync"/>'s own remarks for the minimal, narrowly-scoped stand-in used instead.
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
            .AddEndpointFilter<ValidationFilter<SubjectCreateRequest>>();

        group.MapPost("/{subjectId}/consent", RecordSubjectConsentAsync)
            .WithName("RecordSubjectConsent")
            .Produces<SubjectConsentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
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
            .AddEndpointFilter<ValidationFilter<SubjectConsentRequest>>()
            .ExcludeFromDescription();

        group.MapPatch("/{subjectId}/state", AdvanceSubjectStateAsync)
            .WithName("UpdateSubjectState")
            .Produces<SubjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AddEndpointFilter<ValidationFilter<SubjectStateUpdateRequest>>()
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapGet("/", ListSubjectsAsync)
            .WithName("ListSubjects")
            .WithSummary("Lists one page of a study's subjects in enrollment order.")
            .Produces<SubjectListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{subjectId}", GetSubjectDetailAsync)
            .WithName("GetSubjectDetail")
            .Produces<SubjectDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{subjectId}/demographics", UpdateSubjectDemographicsAsync)
            .WithName("UpdateSubjectDemographics")
            .Produces<SubjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapDelete("/{subjectId}/demographics", DeleteSubjectDemographicsAsync)
            .WithName("DeleteSubjectDemographics")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        return app;
    }

    /// <summary>
    /// <c>POST /api/v1/execution/subjects</c>. <see cref="ValidationFilter{TRequest}"/> guarantees
    /// <paramref name="request"/> already passed <see cref="SubjectCreateRequestValidator"/> by the time this
    /// runs. Success maps to <c>201 Created</c> with a <c>Location</c> header built from the domain subject
    /// code (<see cref="SubjectResponse.SubjectId_"/>), per `02_technical_design.md` §3.3.
    /// </summary>
    public static async Task<IResult> CreateSubjectAsync(
        SubjectCreateRequest request, ISubjectApplicationService svc, CancellationToken ct, [FromServices] ILogger? logger = null)
    {
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
        string subjectId, SubjectConsentRequest request, ISubjectApplicationService svc, CancellationToken ct, [FromServices] ILogger? logger = null)
    {
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
    /// `02_technical_design.md` §3.3's own reference implementation for this route verbatim.
    /// </summary>
    public static async Task<IResult> AdvanceSubjectStateAsync(
        string subjectId, SubjectStateUpdateRequest request, HttpContext http, ISubjectApplicationService svc, CancellationToken ct, [FromServices] ILogger? logger = null)
    {
        string changeReason = ChangeReasonHeader.Read(http.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter should have rejected this request.");

        if (logger != null)
        {
            logger.LogAdvancingSubjectState(subjectId, request.Target, changeReason);
        }

        var result = await svc.AdvanceStateAsync(subjectId, request, changeReason, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>GET /api/v1/execution/subjects?study_id=&amp;site_id=&amp;status=&amp;limit=&amp;offset=</c>. The list
    /// carries no treatment group, so it needs no blinding check.
    /// </summary>
    public static async Task<IResult> ListSubjectsAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "offset")] int? offset,
        ISubjectApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.ListAsync(studyId, siteId, status, limit, offset, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>GET /api/v1/execution/subjects/{subjectId}</c>.
    /// <para>
    /// <b>Minimal, explicitly-temporary stand-in for the missing RBAC abstraction</b> (see this class's own
    /// remarks for the solution-wide search confirming no <c>ICurrentUserAccessor</c>-shaped accessor exists
    /// anywhere today): reads a <c>"Blinded"</c> role claim directly off <c>HttpContext.User</c>, scoped to
    /// only this one route, purely to satisfy this ticket's AC — proving the wiring from caller role to
    /// <see cref="ISubjectApplicationService.GetDetailAsync"/>'s <c>isBlindedCaller</c> parameter, not the
    /// redaction logic itself (already covered by <c>SubjectApplicationServiceDetailTests</c>). Do not copy
    /// this pattern into another slice or treat it as an established platform convention — replace it with a
    /// real, vetted, cross-cutting accessor once the technical design settles on one.
    /// <c>ClaimsPrincipal.IsInRole</c> on the framework's default, unauthenticated
    /// <see cref="HttpContext.User"/> (no authentication middleware is registered anywhere in this slice yet)
    /// safely returns <see langword="false"/> rather than throwing, so this remains safe to call
    /// unconditionally.
    /// </para>
    /// </summary>
    public static async Task<IResult> GetSubjectDetailAsync(
        string subjectId, HttpContext http, ISubjectApplicationService svc, CancellationToken ct, [FromServices] ILogger? logger = null)
    {
        bool isBlindedCaller = http.User.IsInRole(BlindedRoleClaimStandIn);

        if (logger != null)
        {
            logger.LogFetchingSubjectDetail(subjectId, isBlindedCaller);
        }

        var result = await svc.GetDetailAsync(subjectId, isBlindedCaller, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
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
        string changeReason = ChangeReasonHeader.Read(http.Request)!;

        if (logger != null)
        {
            logger.LogUpdatingSubjectDemographics(subjectId, changeReason);
        }

        var result = await svc.UpdateDemographicsAsync(subjectId, request, changeReason, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
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
        string changeReason = ChangeReasonHeader.Read(http.Request)!;

        if (logger != null)
        {
            logger.LogDeletingSubjectDemographics(subjectId, changeReason);
        }

        var result = await svc.DeleteDemographicsAsync(subjectId, changeReason, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary>
    /// The claim/role name this slice's temporary <see cref="GetSubjectDetailAsync"/> stand-in checks for.
    /// Named distinctly (not just an inline literal) so the eventual swap to a real RBAC abstraction is a
    /// one-line diff, and so a reviewer/grep can find every place this stand-in is consulted.
    /// </summary>
    private const string BlindedRoleClaimStandIn = "Blinded";
}
