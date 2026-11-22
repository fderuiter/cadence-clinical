using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// `MapSiteComplianceEndpoints` wires the three site-compliance routes (eTMF-completeness webhook,
/// site-activation gate, compliance-status query) to <see cref="ISiteComplianceApplicationService"/>, per
/// `02_technical_design.md` §3.3. Handler bodies are named `public static` methods (per `AGENTS.md`'s own
/// "Minimal API Route Definition" convention, and matching <c>SubjectEndpoints</c>'s own established
/// precedent from `MIG-SUBJ-023`) rather than the design doc's illustrative inline lambdas, so each handler
/// remains independently unit-testable without changing any wire-visible behavior.
/// <para>
/// <b>Deliberate divergence from `02_technical_design.md` §3.3's literal code sample:</b> that sample's
/// <c>GetSiteComplianceStatusAsync</c>-equivalent lambda declares its query-string parameters as plain
/// <c>string studyId</c> and <c>string? milestone</c> with no binding-source override. Left as-is, ASP.NET
/// Core's minimal-API query binder would only ever populate those from literal query keys
/// <c>?studyId=</c>/<c>?milestone=</c> (the C# parameter name, verbatim) — but `01_spec.md` §1.2 is explicit
/// and authoritative that this route's actual wire contract is <c>study_id</c>/<c>milestone</c>
/// (snake_case, matching every other query/body field in this API). Reproducing the design sample verbatim
/// would silently break every real caller sending the documented <c>?study_id=...</c> query key: a missing
/// required <c>study_id</c> would bind to a JSON <see langword="null"/> and throw inside
/// <c>StudyCode</c>'s constructor deep in the application service, well past this route's own "always 200,
/// never a failure path" contract (this ticket's own AC #4). <see cref="Microsoft.AspNetCore.Mvc.FromQueryAttribute"/>'s
/// <c>Name</c> override closes that gap explicitly — this is the first query-parameter-taking endpoint
/// anywhere in this .NET solution, so there is no established prior-art convention this diverges from.
/// </para>
/// Legacy Python source being replaced: `apps/execution/presentation/routers/subjects.py`
/// (<c>etmf_completeness_webhook</c>, <c>activate_site_endpoint</c>, <c>get_site_compliance_status</c>).
/// @req:PRD-SUB-001
/// </summary>
public static class SiteComplianceEndpoints
{
    public static IEndpointRouteBuilder MapSiteComplianceEndpoints(this IEndpointRouteBuilder app)
    {
        // `.WithOpenApi()` is deliberately omitted -- see SubjectEndpoints.MapSubjectEndpoints's own remarks:
        // it is obsolete (ASPDEPR002) under this repo's pinned ASP.NET Core version, and this solution treats
        // every warning (deprecation diagnostics included) as a build error under <TreatWarningsAsErrors>.
        app.MapPost("/api/v1/execution/webhooks/etmf", ApplyEtmfWebhookAsync)
            .WithTags("SiteCompliance")
            .WithName("EtmfCompletenessWebhook")
            .Produces<OperationEnvelopeResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            // Called service to service by eTMF, signed as its service account holding the etmf_service role.
            .RequirePermission("site_compliance:etmf_sync").RequireCallerStudyScope()
            .AddEndpointFilter<ValidationFilter<EtmfCompletenessWebhookPayload>>();

        app.MapPost("/api/v1/execution/sites/{siteId}/activate", ActivateSiteAsync)
            .WithTags("SiteCompliance")
            .WithName("ActivateSite")
            .Produces<OperationEnvelopeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission("site_compliance:activate")
            .RequireSiteScope("siteId");

        app.MapGet("/api/v1/execution/sites/{siteId}/compliance-status", GetSiteComplianceStatusAsync)
            .WithTags("SiteCompliance")
            .WithName("GetSiteComplianceStatus")
            .Produces<ComplianceStatusResponse>(StatusCodes.Status200OK) // always 200 per §1.2 -- never 404
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("site_compliance:read")
            .RequireSiteScope("siteId");

        return app;
    }

    /// <summary>
    /// <c>POST /api/v1/execution/webhooks/etmf</c>. <see cref="ValidationFilter{TRequest}"/> guarantees
    /// <paramref name="payload"/> already passed <see cref="EtmfCompletenessWebhookPayloadValidator"/> by the
    /// time this runs. <see cref="ISiteComplianceApplicationService.ApplyEtmfWebhookAsync"/> has no
    /// documented failure path (it always upserts), but this still routes through <c>.Match(...)</c> rather
    /// than assuming success unconditionally, so a future failure path added to that method is handled
    /// correctly here without this handler needing a second look.
    /// </summary>
    public static async Task<IResult> ApplyEtmfWebhookAsync(
        EtmfCompletenessWebhookPayload payload,
        ISiteComplianceApplicationService svc,
        CancellationToken ct,
        HttpContext? http = null)
    {
        ct.ThrowIfCancellationRequested();

        if (ExecutionScopeGuard.RequireStudyAndOptionalSite(http, payload.StudyId, payload.SiteId) is { } denied)
        {
            return denied;
        }

        var result = await svc.ApplyEtmfWebhookAsync(payload, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: _ => Results.Ok(new OperationEnvelopeResponse("success", "Compliance cache updated successfully.")),
            onFailure: e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>POST /api/v1/execution/sites/{siteId}/activate</c>. No <see cref="ValidationFilter{TRequest}"/> is
    /// applied, per <see cref="SiteActivationRequest"/>'s own doc comment: its one field is non-nullable, so
    /// ASP.NET Core's JSON body binder already rejects a request missing it before this delegate ever runs.
    /// A non-compliant site's blocked-activation failure surfaces via
    /// <see cref="SubjectErrors.BlockedActivation"/>'s <c>400</c> mapping (this ticket's own AC #3).
    /// </summary>
    public static async Task<IResult> ActivateSiteAsync(
        string siteId,
        SiteActivationRequest request,
        ISiteComplianceApplicationService svc,
        CancellationToken ct,
        HttpContext? http = null)
    {
        ct.ThrowIfCancellationRequested();

        if (ExecutionScopeGuard.RequireStudyAndSite(http, request.StudyId, siteId) is { } denied)
        {
            return denied;
        }

        var result = await svc.ActivateSiteAsync(siteId, request, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: _ => Results.Ok(new OperationEnvelopeResponse("success", $"Site {siteId} activated successfully.")),
            onFailure: e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>GET /api/v1/execution/sites/{siteId}/compliance-status</c>. <paramref name="studyId"/> and
    /// <paramref name="milestone"/> are explicitly bound from the snake_case query keys `01_spec.md` §1.2
    /// documents (<c>study_id</c>/<c>milestone</c>) via <see cref="FromQueryAttribute.Name"/> -- see this
    /// class's own remarks for why the design doc's un-annotated sample cannot be reproduced verbatim here.
    /// <paramref name="milestone"/> defaults to <c>"SITE_ACTIVATION"</c> when absent, matching the spec's
    /// documented default exactly. <see cref="ISiteComplianceApplicationService.GetComplianceStatusAsync"/>
    /// has no failure path at all (unlike every other handler in this file/slice) -- always
    /// <see cref="Results.Ok(object?)"/>, never <c>.Match(...)</c>/<c>ToProblemResult()</c>, per this
    /// ticket's own AC #4 and MIG-SUBJ-020's design.
    /// </summary>
    public static async Task<IResult> GetSiteComplianceStatusAsync(
        string siteId,
        [FromQuery(Name = "study_id")] string studyId,
        [FromQuery(Name = "milestone")] string? milestone,
        ISiteComplianceApplicationService svc,
        CancellationToken ct,
        HttpContext? http = null)
    {
        ct.ThrowIfCancellationRequested();

        if (ExecutionScopeGuard.RequireStudyAndSite(http, studyId, siteId) is { } denied)
        {
            return denied;
        }

        var response = await svc.GetComplianceStatusAsync(siteId, studyId, milestone ?? "SITE_ACTIVATION", ct).ConfigureAwait(false);
        return Results.Ok(response);
    }
}
