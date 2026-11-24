using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Security;

/// <summary>
/// The route inventory of every service (#5361, ADR-2516). Each route must declare who may call it, and the declaration
/// must hold when called: a signed caller whose roles lack a route's permission is refused by the authorization
/// middleware, before the handler runs, so the operation neither reads nor writes; a caller holding it is admitted.
/// Requests here never reach a handler: <see cref="ProbeResultHandler"/> answers in place of the endpoint once
/// authorization has decided.
/// </summary>
public sealed class RouteAuthorizationTests
{
    /// <summary>The only anonymous routes: liveness and readiness, the service status probe, and development API documents.</summary>
    private static readonly string[] AnonymousRoutes =
    [
        "/healthz", "/healthz/live", "/healthz/ready",
        "/openapi/{documentName}.json", "/scalar/{documentName?}", "/scalar/scalar.js", "/scalar/scalar.aspnetcore.js", "/scalar/favicon.svg",
    ];

    /// <summary>
    /// Routes open to any signed-in caller, with why each needs no role permission. A new one is a review decision, so it
    /// must be added here.
    /// </summary>
    private static readonly (string Host, string Route, string Reason)[] SignedInCallerRoutes =
    [
        ("kernel", "/api/v1/kernel/*", "The kernel authorizes each object from the caller's grants: reads mask what they do not cover, commands go through the authority evaluator."),
        ("kernel", "GET /api/v1/execution/forms", "Answers only when the caller may see the enrollment, as kernel reads decide it; otherwise 403."),
        ("kernel", "POST /api/v1/execution/forms", "Sends OpenCollectionInstance, which the kernel authorizes against the caller's grant on the enrollment (collection.open)."),
        ("kernel", "GET /api/v1/execution/forms/{formId:guid}", "Answers only when the caller may see the form's enrollment, as kernel reads decide it; otherwise 403."),
        ("kernel", "PUT /api/v1/execution/forms/{formId:guid}/values", "Sends RecordCollectedValues, which the kernel authorizes against the caller's grant on the form's enrollment (collection.record)."),
        ("kernel", "POST /api/v1/execution/forms/{formId:guid}/preview", "Answers only when the caller may see the form's enrollment, as kernel reads decide it, and evaluates pinned collection rules in memory without mutating state; otherwise 403."),
        ("kernel", "POST /api/v1/execution/forms/{formId:guid}/signatures", "Needs a fresh single-use X-Sig-Token for this form, then sends SignCollectionInstance, which the kernel authorizes against the caller's grant on the form's enrollment (collection.sign)."),
        ("kernel", "GET /api/v1/execution/forms/queries", "Answers only when the caller may see the enrollment, as kernel reads decide it; otherwise 403."),
        ("kernel", "GET /api/v1/execution/forms/queries/digests", "Answers only when the caller may see the study and filters site digests by site visibility; otherwise 403."),
        ("kernel", "POST /api/v1/execution/forms/queries/escalate", "Checks study visibility and sends EscalateFormQuery and GenerateFormQueryAgingDigest, which the kernel authorizes against execution.query.manage."),
        ("kernel", "GET /api/v1/execution/forms/{formId:guid}/queries", "Answers only when the caller may see the form's enrollment, as kernel reads decide it; otherwise 403."),
        ("kernel", "POST /api/v1/execution/forms/{formId:guid}/queries", "Sends RaiseFormQuery, which the kernel authorizes against the caller's grant on the form's enrollment (execution.query.raise)."),
        ("kernel", "POST /api/v1/execution/forms/{formId:guid}/queries/{queryId:guid}/answer", "Sends AnswerFormQuery, which the kernel authorizes against the caller's grant on the query's enrollment (execution.query.answer)."),
        ("kernel", "POST /api/v1/execution/forms/{formId:guid}/queries/{queryId:guid}/reopen", "Sends ReopenFormQuery, which the kernel authorizes against the caller's grant on the query's enrollment (execution.query.manage)."),
        ("kernel", "POST /api/v1/execution/forms/{formId:guid}/queries/{queryId:guid}/reassign", "Sends ReassignFormQuery, which the kernel authorizes against the caller's grant on the query's enrollment (execution.query.manage)."),
        ("kernel", "POST /api/v1/execution/forms/{formId:guid}/queries/{queryId:guid}/close", "Sends CloseFormQuery, which the kernel authorizes against the caller's grant on the query's enrollment (execution.query.manage)."),
        ("kernel", "POST /api/v1/execution/forms/{formId:guid}/queries/{queryId:guid}/cancel", "Sends CancelFormQuery, which the kernel authorizes against the caller's grant on the query's enrollment (execution.query.manage)."),
        ("kernel", "POST /api/v1/ai/intent/route", "Matches fixed patterns, reads and writes no record, and filters recommendations by the caller's signed roles."),
        ("kernel", "GET /api/v1/ai/runs", "Lists only the caller's own agent runs (requested_by is the signed subject), under the tenant's Row-Level Security."),
        ("kernel", "GET /api/v1/ai/runs/{runId:guid}", "Answers only the caller's own agent run; another person's is 404."),
        ("kernel", "GET /api/v1/ai/tasks", "The registered task configurations and the caller's tenant's qualifications of them (Row-Level Security); no clinical record."),
        ("kernel", "POST /api/v1/ai/evaluation-runs", "Sends RecordEvaluationRun, which the kernel authority evaluator authorizes against evaluation_run.record (ADR-2634, #5574)."),
        ("kernel", "GET /api/v1/ai/evaluation-runs/{runId:guid}", "One stored evaluation run of the caller's tenant (Row-Level Security): measurement of a task configuration on de-identified cases, no clinical record."),
        ("kernel", "GET /api/v1/ai/tasks/{key}/evaluation-runs", "The stored evaluation runs of a task in the caller's tenant (Row-Level Security), which every member may see next to the task's qualification; no clinical record."),
        ("kernel", "POST /api/v1/ai/tasks/{key}/versions/{version:int}/qualification", "Sends QualifyTaskConfiguration, which the kernel authority evaluator authorizes against task_configuration.qualify (Approve, people only) and which needs the caller's own attestation (ADR-2631)."),
        ("kernel", "POST /api/v1/ai/tasks/{key}/versions/{version:int}/suspension", "Sends SuspendTaskConfiguration, which the kernel authority evaluator authorizes against task_configuration.suspend (ADR-2631)."),
        ("kernel", "GET /api/v1/ai/controls", "The tenant's AI kill switches and budgets, which every member may see so they know whether an assistant is off."),
        ("kernel", "PUT /api/v1/ai/controls/{scope}", "Sends SetAgentControl, which the kernel authority evaluator authorizes against agent_control.set (ADR-2630)."),
        ("kernel", "POST /api/v1/ai/mcp", "Read-only agent tools for outside agents (ADR-2633): every read runs as the signed-in caller, masked for both the caller and the agent, and no tool writes."),
        ("kernel", "POST /api/v1/ai/biometrics/verify-liveness", "Anti-spoof facial liveness verification generates short-lived HMAC signed tokens for consent capture."),
    ];

    private const string ProbeHeader = "X-Authorization-Probe";

    [Theory]
    [MemberData(nameof(Hosts))]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task EveryRoute_DeclaresWhoMayCallIt(string host)
    {
        await using var booted = ServiceHosts.All[host](null);

        var problems = booted.Endpoints.Select(endpoint => Problem(host, endpoint)).OfType<string>().ToList();

        problems.Should().BeEmpty(
            "every route must use RequirePermission, RequireSignedInCaller (listed in this test), or AllowAnonymous for health, status and API documents only");
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task ACallerWithoutTheRoutePermission_IsRefusedBeforeTheHandlerRuns(string host)
    {
        await using var booted = ServiceHosts.All[host](ProbeResultHandler.Register);
        var routes = PermissionRoutes(booted);

        var problems = new List<string>();
        foreach (var route in routes)
        {
            foreach (var identity in new[] { CallerLacking(route.Permission), CallerLacking(route.Permission) with { Roles = [] } })
            {
                using var client = booted.CreateSignedClient(identity);
                var outcome = await SendAsync(client, route);
                if (outcome != Outcome.Forbidden)
                {
                    problems.Add($"{route}: a caller with roles [{string.Join(", ", identity.Roles)}] got {outcome}");
                }
            }

            using var unsigned = booted.CreateUnsignedClient();
            var anonymous = await SendAsync(unsigned, route);
            if (anonymous != Outcome.Challenged)
            {
                problems.Add($"{route}: an unsigned caller got {anonymous}");
            }
        }

        problems.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task ACallerHoldingTheRoutePermission_IsAdmitted(string host)
    {
        await using var booted = ServiceHosts.All[host](ProbeResultHandler.Register);

        var problems = new List<string>();
        foreach (var route in PermissionRoutes(booted))
        {
            using var client = booted.CreateSignedClient(CallerHolding(route.Permission));
            var outcome = await SendAsync(client, route);
            if (outcome != Outcome.Admitted)
            {
                problems.Add($"{route}: a holder of {route.Permission} got {outcome}");
            }
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task EveryListedSignedInCallerRoute_StillExists()
    {
        var stale = new List<string>();
        foreach (var group in SignedInCallerRoutes.GroupBy(r => r.Host))
        {
            await using var booted = ServiceHosts.All[group.Key](null);
            var routes = booted.Endpoints.Select(Describe).ToList();
            stale.AddRange(group.Where(listed => !routes.Any(route => Matches(listed.Route, route))).Select(listed => $"{listed.Host} {listed.Route}"));
        }

        stale.Should().BeEmpty("a listed route that no longer exists should be removed from the list");
    }

    public static TheoryData<string> Hosts => ServiceHosts.Names;

    private static string? Problem(string host, RouteEndpoint endpoint)
    {
        string route = Describe(endpoint);
        var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            string pattern = Pattern(endpoint);
            return AnonymousRoutes.Contains(pattern, StringComparer.Ordinal) || IsStatusProbe(pattern)
                ? null
                : $"{host} {route} allows anonymous callers";
        }

        if (authorize.Count == 0)
        {
            return $"{host} {route} declares no authorization (it is denied by default, so nobody can call it)";
        }

        if (authorize.Any(a => !string.IsNullOrEmpty(a.Roles) || !string.IsNullOrEmpty(a.AuthenticationSchemes)))
        {
            return $"{host} {route} names roles or schemes on the route instead of a permission";
        }

        var policies = authorize.Select(a => a.Policy).ToList();
        if (policies.Any(p => p is null))
        {
            return $"{host} {route} uses RequireAuthorization() with the default policy instead of a permission";
        }

        var permissions = policies.Where(p => p!.StartsWith(PermissionPolicyProvider.PolicyPrefix, StringComparison.Ordinal))
            .Select(p => p![PermissionPolicyProvider.PolicyPrefix.Length..])
            .ToList();
        if (permissions.FirstOrDefault(p => !PermissionMatrix.IsDefined(p)) is { } undefined)
        {
            return $"{host} {route} requires {undefined}, which no role holds";
        }

        if (permissions.Count > 0)
        {
            return null;
        }

        if (policies.Contains(CadencePolicies.SignedInCaller))
        {
            return SignedInCallerRoutes.Any(listed => listed.Host == host && Matches(listed.Route, route))
                ? null
                : $"{host} {route} admits any signed-in caller but is not listed in {nameof(SignedInCallerRoutes)}";
        }

        return $"{host} {route} requires only {string.Join(", ", policies)}: scope narrows a permission and never replaces one";
    }

    private static bool IsStatusProbe(string pattern) =>
        pattern.StartsWith("/api/v1/", StringComparison.Ordinal) && pattern.EndsWith("/status", StringComparison.Ordinal)
        && pattern.Count(c => c == '/') == 4;

    private static bool Matches(string listed, string route) =>
        listed.EndsWith("/*", StringComparison.Ordinal)
            ? route.Split(' ')[1].StartsWith(listed[..^1], StringComparison.Ordinal)
            : string.Equals(listed, route, StringComparison.Ordinal);

    private static string Pattern(RouteEndpoint endpoint) => "/" + (endpoint.RoutePattern.RawText ?? string.Empty).Trim('/');

    private static string Describe(RouteEndpoint endpoint) =>
        $"{string.Join(",", endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])} {Pattern(endpoint)}";

    private static List<PermissionRoute> PermissionRoutes(BootedHost booted) =>
        booted.Endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy)
                .Where(p => p is not null && p.StartsWith(PermissionPolicyProvider.PolicyPrefix, StringComparison.Ordinal))
                .Take(1)
                .Select(p => new PermissionRoute(
                    e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Get,
                    e.RoutePattern,
                    p![PermissionPolicyProvider.PolicyPrefix.Length..])))
            .ToList();

    /// <summary>A signed caller holding every role in the matrix that lacks <paramref name="permission"/>, in every study.</summary>
    private static GatewayIdentity CallerLacking(string permission) =>
        TestSecurityContext.Identity(
            $"lacks-{permission}",
            [.. PermissionMatrix.ROLE_PERMISSIONS.Keys.Where(role => !Holds(role, permission)).Order(StringComparer.Ordinal)]);

    /// <summary>A signed caller holding one role that has <paramref name="permission"/>, in every study and at the probe site.</summary>
    private static GatewayIdentity CallerHolding(string permission) =>
        TestSecurityContext.Identity(
            $"holds-{permission}",
            PermissionMatrix.ROLE_PERMISSIONS.Keys.Order(StringComparer.Ordinal).First(role => Holds(role, permission)))
        with
        { SiteIds = [ProbeSite] };

    private static bool Holds(string role, string permission) =>
        PermissionMatrix.HasPermission(
            new Principal { Roles = RoleNormalization.GetNormalizedRoles([role]), RawRoles = [role] },
            permission);

    private const string ProbeSite = "probe-site";

    private static async Task<Outcome> SendAsync(HttpClient client, PermissionRoute route)
    {
        using var request = new HttpRequestMessage(new HttpMethod(route.Method), route.Path);
        request.Headers.Add(ProbeHeader, "1");
        if (!HttpMethods.IsGet(route.Method) && !HttpMethods.IsDelete(route.Method))
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, Ct);
        string? decided = response.Headers.TryGetValues(ProbeHeader, out var values) ? values.Single() : null;
        return (decided, response.StatusCode) switch
        {
            ("admitted", _) => Outcome.Admitted,
            ("forbidden", HttpStatusCode.Forbidden) => Outcome.Forbidden,
            ("challenged", HttpStatusCode.Unauthorized) => Outcome.Challenged,
            _ => Outcome.NotDecidedByAuthorization,
        };
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private enum Outcome
    {
        Admitted,
        Forbidden,
        Challenged,
        NotDecidedByAuthorization,
    }

    private sealed record PermissionRoute(string Method, RoutePattern Template, string Permission)
    {
        /// <summary>The pattern with each route parameter filled with a value its constraints accept.</summary>
        public string Path => "/" + string.Join('/', Template.PathSegments.Select(segment => string.Concat(segment.Parts.Select(Fill))));

        public override string ToString() => $"{Method} {Template.RawText} ({Permission})";

        private static string Fill(RoutePatternPart part) => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart parameter => Value(parameter),
            _ => string.Empty,
        };

        private static string Value(RoutePatternParameterPart parameter)
        {
            var constraints = parameter.ParameterPolicies.Select(p => p.Content ?? string.Empty).ToList();
            if (constraints.Contains("guid"))
            {
                return "8d1c4f0e-2f8b-4c39-9a51-0c6d7c1b2a10";
            }

            if (constraints.Any(c => c is "int" or "long"))
            {
                return "1";
            }

            return parameter.Name.Contains("site", StringComparison.OrdinalIgnoreCase) ? ProbeSite : $"probe-{parameter.Name}";
        }
    }

    /// <summary>
    /// Answers in place of the endpoint once authorization has decided a probe request, so no handler runs: <c>204</c>
    /// with <c>admitted</c>, or the middleware's own <c>401</c>/<c>403</c> marked <c>challenged</c>/<c>forbidden</c>.
    /// </summary>
    private sealed class ProbeResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _inner = new();

        public static void Register(IServiceCollection services) =>
            services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProbeResultHandler>();

        public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (!context.Request.Headers.ContainsKey(ProbeHeader))
            {
                await _inner.HandleAsync(next, context, policy, authorizeResult);
                return;
            }

            if (authorizeResult.Succeeded)
            {
                context.Response.Headers[ProbeHeader] = "admitted";
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            context.Response.Headers[ProbeHeader] = authorizeResult.Challenged ? "challenged" : "forbidden";
            await _inner.HandleAsync(next, context, policy, authorizeResult);
        }
    }

    public static TheoryData<string> StandaloneHosts =>
        new(ServiceHosts.All.Keys.Where(name => !string.Equals(name, "kernel", StringComparison.Ordinal)));

    [Theory]
    [MemberData(nameof(StandaloneHosts))]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-CRF-013")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task StandaloneRoutes_DeclareExplicitScopeMetadata_AndEnforceTenantStudyAndSiteAdmission(string host)
    {
        await using var booted = ServiceHosts.All[host](ProbeResultHandler.Register);

        var missingScopeMetadata = booted.Endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(e => e.Metadata.GetMetadata<EndpointScopeMetadata>() is null)
            .Select(e => $"{host} {Describe(e)}")
            .ToList();

        missingScopeMetadata.Should().BeEmpty(
            "every non-anonymous standalone route must declare explicit EndpointScopeMetadata (RequireTenantScope, RequireCallerStudyScope, RequireCallerStudyAndSiteScope, RequireStudyScope, or RequireSiteScope)");

        var problems = new List<string>();
        var clientCache = new Dictionary<string, HttpClient>(StringComparer.Ordinal);
        HttpClient ClientFor(GatewayIdentity id)
        {
            string key = $"{id.UserId}|{id.TenantId}|{string.Join(',', id.Roles)}|{string.Join(',', id.StudyIds ?? [])}|{string.Join(',', id.SiteIds ?? [])}";
            if (!clientCache.TryGetValue(key, out var cached))
            {
                cached = booted.CreateSignedClient(id);
                clientCache[key] = cached;
            }

            return cached;
        }

        try
        {
            foreach (var endpoint in booted.Endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null))
            {
                string? permission = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Select(a => a.Policy)
                    .Where(p => p is not null && p.StartsWith(PermissionPolicyProvider.PolicyPrefix, StringComparison.Ordinal))
                    .Select(p => p![PermissionPolicyProvider.PolicyPrefix.Length..])
                    .FirstOrDefault();

                if (permission is null)
                {
                    continue;
                }

                var route = new PermissionRoute(
                    endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Get,
                    endpoint.RoutePattern,
                    permission);
                var scopeDeclarations = endpoint.Metadata.GetOrderedMetadata<EndpointScopeMetadata>();

                // 1. Fail-closed tenant admission (ADR-2514): signed caller without X-Tenant-Id must get 403 Forbidden.
                var noTenantOutcome = await SendAsync(ClientFor(CallerHolding(permission) with { TenantId = null }), route);
                if (noTenantOutcome != Outcome.Forbidden)
                {
                    problems.Add($"{host} {route}: caller without X-Tenant-Id got {noTenantOutcome}");
                }

                // 2. Study-scoped routes must refuse callers with zero study assignments.
                bool requiresStudy = scopeDeclarations.Any(s =>
                    s.Kind is EndpointScopeKind.CallerStudy
                        or EndpointScopeKind.CallerStudyAndSite
                        or EndpointScopeKind.RouteStudy
                        or EndpointScopeKind.RouteSite);
                if (requiresStudy)
                {
                    var noStudyOutcome = await SendAsync(ClientFor(CallerHolding(permission) with { StudyIds = [] }), route);
                    if (noStudyOutcome != Outcome.Forbidden)
                    {
                        problems.Add($"{host} {route}: caller with empty StudyIds got {noStudyOutcome}");
                    }
                }

                // 3. Route-parameter study/site routes must refuse callers assigned to a different study or site.
                if (scopeDeclarations.Any(s => s.Kind == EndpointScopeKind.RouteStudy))
                {
                    var wrongStudyCaller = CallerHolding(permission) with { StudyIds = ["OTHER-UNASSIGNED-STUDY"] };
                    var wrongStudyOutcome = await SendAsync(ClientFor(wrongStudyCaller), route);
                    if (wrongStudyOutcome != Outcome.Forbidden)
                    {
                        problems.Add($"{host} {route}: caller assigned to OTHER-UNASSIGNED-STUDY got {wrongStudyOutcome}");
                    }
                }

                if (scopeDeclarations.Any(s => s.Kind == EndpointScopeKind.RouteSite))
                {
                    var wrongSiteCaller = CallerHolding(permission) with { SiteIds = ["OTHER-UNASSIGNED-SITE"] };
                    var wrongSiteOutcome = await SendAsync(ClientFor(wrongSiteCaller), route);
                    if (wrongSiteOutcome != Outcome.Forbidden)
                    {
                        problems.Add($"{host} {route}: caller assigned to OTHER-UNASSIGNED-SITE got {wrongSiteOutcome}");
                    }
                }

                // 4. Site-scoped routes must refuse site-scoped roles that lack site assignments.
                bool requiresSite = scopeDeclarations.Any(s =>
                    s.Kind is EndpointScopeKind.CallerStudyAndSite or EndpointScopeKind.RouteSite);
                if (requiresSite && PickSiteScopedRoleHolding(permission) is { } siteScopedRole)
                {
                    var unassignedSiteCaller = TestSecurityContext.Identity($"unassigned-site-{permission}", siteScopedRole) with
                    {
                        SiteIds = [],
                    };
                    var noSiteOutcome = await SendAsync(ClientFor(unassignedSiteCaller), route);
                    if (noSiteOutcome != Outcome.Forbidden)
                    {
                        problems.Add($"{host} {route}: unassigned site-scoped role '{siteScopedRole}' got {noSiteOutcome}");
                    }
                }
            }
        }
        finally
        {
            foreach (var client in clientCache.Values)
            {
                client.Dispose();
            }
        }

        problems.Should().BeEmpty();
    }

    /// <summary>
    /// Executes real handler-level (no <see cref="ProbeResultHandler"/> short-circuit) cross-tenant, cross-study, and
    /// cross-site denial probes across active standalone clinical hosts so handler body guards (<see cref="ClinicalScopeGuard"/>)
    /// and tenant resolvers are verified end-to-end with zero false positives.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-CRF-013")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task StandaloneHandlers_ExecuteCrossTenantCrossStudyAndCrossSiteDenialProbes_WithoutShortCircuit()
    {
        var handlerProbes = new (string Host, string Method, string Path, string? JsonBody, GatewayIdentity CrossStudyCaller, GatewayIdentity? CrossSiteCaller)[]
        {
            (
                "safety",
                HttpMethods.Post,
                "/api/v1/safety/dsur/compile",
                """{"compoundName":"CMP-1","studyId":"STUDY-OTHER","periodStart":"2025-01-01T00:00:00Z","periodEnd":"2025-12-31T00:00:00Z","reasonForChange":"Audit probe"}""",
                TestSecurityContext.Identity("mm-probe", "Sponsor Medical Monitor") with { StudyIds = ["STUDY-ALLOWED"] },
                null),
            (
                "ctms",
                HttpMethods.Get,
                "/api/v1/ctms/monitoring-visits?study_id=STUDY-OTHER&site_id=SITE-OTHER",
                null,
                TestSecurityContext.Identity("cra-study-probe", "CRA") with { StudyIds = ["STUDY-ALLOWED"], SiteIds = ["SITE-OTHER"] },
                TestSecurityContext.Identity("cra-site-probe", "CRA") with { StudyIds = ["STUDY-OTHER"], SiteIds = ["SITE-ALLOWED"] }),
            (
                "etmf",
                HttpMethods.Post,
                "/api/v1/etmf/documents",
                """{"study_id":"STUDY-OTHER","site_id":"SITE-OTHER","filename":"protocol.pdf","reason_for_change":"Initial filing for probe"}""",
                TestSecurityContext.Identity("cra-etmf-study", "CRA") with { StudyIds = ["STUDY-ALLOWED"], SiteIds = ["SITE-OTHER"] },
                TestSecurityContext.Identity("cra-etmf-site", "CRA") with { StudyIds = ["STUDY-OTHER"], SiteIds = ["SITE-ALLOWED"] }),
            (
                "interop",
                HttpMethods.Post,
                "/api/v1/interop/esource/ingest",
                """{"studyId":"STUDY-OTHER","siteId":"SITE-OTHER","subjectId":"SUBJ-1","ehrSystemId":"EPIC","rawFhirBundleJson":"{}"}""",
                TestSecurityContext.Identity("crc-interop-study", "crc") with { StudyIds = ["STUDY-ALLOWED"], SiteIds = ["SITE-OTHER"] },
                TestSecurityContext.Identity("crc-interop-site", "crc") with { StudyIds = ["STUDY-OTHER"], SiteIds = ["SITE-ALLOWED"] }),
            (
                "imaging",
                HttpMethods.Post,
                "/api/v1/imaging/studies?studyId=STUDY-OTHER&siteId=SITE-OTHER&subjectId=SUBJ-1",
                """{"dummy":"payload"}""",
                TestSecurityContext.Identity("crc-img-study", "crc") with { StudyIds = ["STUDY-ALLOWED"], SiteIds = ["SITE-OTHER"] },
                TestSecurityContext.Identity("crc-img-site", "crc") with { StudyIds = ["STUDY-OTHER"], SiteIds = ["SITE-ALLOWED"] }),
            (
                "knowledge",
                HttpMethods.Get,
                "/api/v1/training/assignments?study_id=STUDY-OTHER&site_id=SITE-OTHER",
                null,
                TestSecurityContext.Identity("crc-kn-study", "crc") with { StudyIds = ["STUDY-ALLOWED"], SiteIds = ["SITE-OTHER"] },
                TestSecurityContext.Identity("crc-kn-site", "crc") with { StudyIds = ["STUDY-OTHER"], SiteIds = ["SITE-ALLOWED"] }),
            (
                "econsent",
                HttpMethods.Post,
                "/api/v1/econsent/templates/tpl-probe/capture-consent",
                """{"subject_pseudonym":"SUBJ-1","study_id":"STUDY-OTHER","site_id":"SITE-OTHER","digest_sha256":"a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289","signature_data":"SIG-1","liveness_token":"tok"}""",
                (TestSecurityContext.Identity("crc-ec-study", "crc") with
                {
                    StudyIds = ["STUDY-ALLOWED"],
                    SiteIds = ["SITE-OTHER"],
                    SigToken = TestSecurityContext.CreateSigToken("crc-ec-study", "/api/v1/econsent/templates/tpl-probe/capture-consent", "econsent.consent.capture", ["crc"]),
                }),
                (TestSecurityContext.Identity("crc-ec-site", "crc") with
                {
                    StudyIds = ["STUDY-OTHER"],
                    SiteIds = ["SITE-ALLOWED"],
                    SigToken = TestSecurityContext.CreateSigToken("crc-ec-site", "/api/v1/econsent/templates/tpl-probe/capture-consent", "econsent.consent.capture", ["crc"]),
                })),
            (
                "execution",
                HttpMethods.Get,
                "/api/v1/execution/subjects?study_id=STUDY-OTHER&site_id=SITE-OTHER",
                null,
                TestSecurityContext.Identity("pi-exec-study", "principal_investigator") with { StudyIds = ["STUDY-ALLOWED"], SiteIds = ["SITE-OTHER"] },
                TestSecurityContext.Identity("pi-exec-site", "principal_investigator") with { StudyIds = ["STUDY-OTHER"], SiteIds = ["SITE-ALLOWED"] }),
            (
                "payments",
                HttpMethods.Post,
                "/api/v1/payments/participant/expenses",
                """{"subjectId":"SUBJ-1","studyId":"STUDY-OTHER","siteId":"SITE-OTHER","category":"TRANSIT","merchantName":"Metro","grossAmount":20.00}""",
                TestSecurityContext.Identity("crc-pay-study", "crc") with { StudyIds = ["STUDY-ALLOWED"], SiteIds = ["SITE-OTHER"] },
                TestSecurityContext.Identity("crc-pay-site", "crc") with { StudyIds = ["STUDY-OTHER"], SiteIds = ["SITE-ALLOWED"] }),
            (
                "designer",
                HttpMethods.Post,
                "/api/v1/protocols/compile",
                """{"usdmJson":"{}","studyId":"STUDY-OTHER"}""",
                TestSecurityContext.Identity("designer-study", "Study Designer") with { StudyIds = ["STUDY-ALLOWED"] },
                null),
            (
                "tickets",
                HttpMethods.Get,
                "/api/v1/tickets?study_id=STUDY-OTHER&site_id=SITE-OTHER",
                null,
                TestSecurityContext.Identity("crc-tkt-study", "crc") with { StudyIds = ["STUDY-ALLOWED"], SiteIds = ["SITE-OTHER"] },
                TestSecurityContext.Identity("crc-tkt-site", "crc") with { StudyIds = ["STUDY-OTHER"], SiteIds = ["SITE-ALLOWED"] }),
        };

        var failures = new List<string>();

        foreach (var probe in handlerProbes)
        {
            var tenantResolver = new Cadence.Database.Tenancy.DeterministicTenantCodeResolver();
            tenantResolver.DeactivateTenant("deactivated-probe-tenant");

            await using var booted = ServiceHosts.All[probe.Host](services =>
            {
                Cadence.Database.CadenceDatabaseExtensions.AddCadenceInMemoryTenancy(services, tenantResolver);
                foreach (var existing in services.Where(d => d.ServiceType == typeof(ISigTokenUseStore)).ToList())
                {
                    services.Remove(existing);
                }

                services.AddSingleton<ISigTokenUseStore, ProbeSigTokenUseStore>();
            });

            if (booted.Services.GetService<ITenantAccessVerifier>() is not null)
            {
                using var inactiveTenantClient = booted.CreateSignedClient(probe.CrossStudyCaller with
                {
                    TenantId = "deactivated-probe-tenant",
                    StudyIds = ["*"],
                });
                using var inactiveReq = BuildProbeHttpRequest(probe.Method, probe.Path, probe.JsonBody);
                using var inactiveResp = await inactiveTenantClient.SendAsync(inactiveReq, Ct);
                if (inactiveResp.StatusCode != HttpStatusCode.Forbidden)
                {
                    failures.Add($"{probe.Host} {probe.Method} {probe.Path}: deactivated tenant got {inactiveResp.StatusCode}");
                }
            }

            using var noTenantClient = booted.CreateSignedClient(probe.CrossStudyCaller with
            {
                TenantId = null,
                StudyIds = ["*"],
            });
            using var noTenantReq = BuildProbeHttpRequest(probe.Method, probe.Path, probe.JsonBody);
            using var noTenantResp = await noTenantClient.SendAsync(noTenantReq, Ct);
            if (noTenantResp.StatusCode != HttpStatusCode.Forbidden)
            {
                failures.Add($"{probe.Host} {probe.Method} {probe.Path}: missing tenant got {noTenantResp.StatusCode}");
            }

            using var crossStudyClient = booted.CreateSignedClient(probe.CrossStudyCaller);
            using var crossStudyReq = BuildProbeHttpRequest(probe.Method, probe.Path, probe.JsonBody);
            using var crossStudyResp = await crossStudyClient.SendAsync(crossStudyReq, Ct);
            if (crossStudyResp.StatusCode != HttpStatusCode.Forbidden)
            {
                failures.Add($"{probe.Host} {probe.Method} {probe.Path}: cross-study caller got {crossStudyResp.StatusCode} ({await crossStudyResp.Content.ReadAsStringAsync(Ct)})");
            }

            if (probe.CrossSiteCaller is not null)
            {
                using var crossSiteClient = booted.CreateSignedClient(probe.CrossSiteCaller);
                using var crossSiteReq = BuildProbeHttpRequest(probe.Method, probe.Path, probe.JsonBody);
                using var crossSiteResp = await crossSiteClient.SendAsync(crossSiteReq, Ct);
                if (crossSiteResp.StatusCode != HttpStatusCode.Forbidden)
                {
                    failures.Add($"{probe.Host} {probe.Method} {probe.Path}: cross-site caller got {crossSiteResp.StatusCode} ({await crossSiteResp.Content.ReadAsStringAsync(Ct)})");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    private static HttpRequestMessage BuildProbeHttpRequest(string method, string path, string? jsonBody)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), path);
        if (jsonBody is not null)
        {
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return req;
    }

    private static string? PickSiteScopedRoleHolding(string permission) =>
        PermissionMatrix.ROLE_PERMISSIONS.Keys
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(role =>
                Holds(role, permission)
                && SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains(RoleNormalization.NormalizeRole(role)));

    private sealed class ProbeSigTokenUseStore : ReplayCache, IDurableSigTokenUseStore;

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task AllExecutionMutatingRoutes_RequireChangeReasonHeaderFilter()
    {
        await using var host = ServiceHosts.All["execution"](null);
        var mutatingRouteEndpoints = host.Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(ep =>
            {
                var methods = ep.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [];
                bool isMutatingVerb = methods.Any(m => m is "POST" or "PUT" or "PATCH" or "DELETE");
                string pattern = ep.RoutePattern.RawText ?? string.Empty;
                bool isReadOnlyComputation =
                    pattern.EndsWith("/labs/import/dry-run", StringComparison.Ordinal)
                    || pattern.EndsWith("/labs/convert-unit", StringComparison.Ordinal)
                    || pattern.EndsWith("/labs/toxicity/evaluate", StringComparison.Ordinal)
                    || pattern.EndsWith("/rtsm/cgt/verify-barcode", StringComparison.Ordinal)
                    || pattern.EndsWith("/coding/synonyms/validate", StringComparison.Ordinal)
                    || pattern.EndsWith("/devices/parse-udi", StringComparison.Ordinal)
                    || pattern.EndsWith("/rtsm/kits/validate-scan", StringComparison.Ordinal);
                return isMutatingVerb && !isReadOnlyComputation;
            })
            .ToList();

        mutatingRouteEndpoints.Count.Should().BeGreaterThanOrEqualTo(33);
        foreach (var endpoint in mutatingRouteEndpoints)
        {
            endpoint.Metadata.GetMetadata<Cadence.Apps.Execution.Endpoints.Filters.ChangeReasonRequired>()
                .Should().NotBeNull($"mutating route '{endpoint.DisplayName}' ({endpoint.RoutePattern.RawText}) must attach RequireChangeReasonHeader()");
        }
    }

    [Theory]
    [InlineData("ctms", "/api/v1/ctms/startup/sites/{siteId}/greenlight/certify")]
    [InlineData("ctms", "/api/v1/ctms/startup/matrix/batch-certify")]
    [InlineData("ctms", "/api/v1/ctms/monitoring-visits/{visit_id}/sign-off")]
    [InlineData("ctms", "/api/v1/ctms/letters/compile")]
    [InlineData("ctms", "/api/v1/ctms/doa/sign-off")]
    [InlineData("ctms", "/api/v1/ctms/doa/sites/{site_id}/export-pdf")]
    [InlineData("imaging", "/api/v1/imaging/adjudications/{sessionId}/chair-adjudicate")]
    [InlineData("payments", "/api/v1/payments/invoices/{id}/approve")]
    [InlineData("payments", "/api/v1/payments/invoices/{id}/remittance-advice/sign")]
    [InlineData("interop", "/api/v1/interop/esource/reconcile/accept")]
    [InlineData("interop", "/api/v1/interop/esource/reconcile/batch-accept")]
    [InlineData("interop", "/api/v1/interop/esource/reconcile/reject")]
    [InlineData("interop", "/api/v1/interop/esource/reconcile/batch-reject")]
    [InlineData("execution", "/api/v1/execution/subjects/{subjectId}/state")]
    [InlineData("execution", "/api/v1/execution/rtsm/cgt/bedside-verify")]
    [InlineData("execution", "/api/v1/execution/rtsm/cgt/destruction-certificate")]
    [InlineData("execution", "/api/v1/execution/devices/quarantine/release")]
    [InlineData("execution", "/api/v1/execution/rtsm/kits/reconcile-batch")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task NewlyGatedSignatureRoutes_CarryElectronicSignatureMetadata(string service, string expectedPattern)
    {
        await using var host = ServiceHosts.All[service](null);
        var endpoint = host.Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .FirstOrDefault(e => string.Equals(
                "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'),
                expectedPattern,
                StringComparison.OrdinalIgnoreCase));

        endpoint.Should().NotBeNull($"expected route '{expectedPattern}' in service '{service}'");
        endpoint!.Metadata.GetMetadata<ElectronicSignatureRequired>()
            .Should().NotBeNull($"route '{expectedPattern}' in '{service}' must enforce RequireElectronicSignature");
    }

    [Theory]
    [InlineData("ctms", "POST", "/api/v1/ctms/startup/sites/SITE-101/greenlight/certify?study_id=STUDY-1", "ctms.greenlight.certify", "{}")]
    [InlineData("ctms", "POST", "/api/v1/ctms/startup/matrix/batch-certify", "ctms.greenlight.batch_certify", "{}")]
    [InlineData("ctms", "POST", "/api/v1/ctms/monitoring-visits/VISIT-101/sign-off", "ctms.monitoring_visit.sign_off", "{}")]
    [InlineData("ctms", "POST", "/api/v1/ctms/letters/compile?templateName=follow_up_letter", "ctms.letter.compile", """{"craId":"cra-1","createdAt":"2026-11-23","studyId":"STUDY-1","siteId":"probe-site"}""")]
    [InlineData("ctms", "POST", "/api/v1/ctms/doa/sign-off", "ctms.doa.sign_off", """{"record_id":"REC-1","reason_for_change":"PI sign-off attestation"}""")]
    [InlineData("ctms", "GET", "/api/v1/ctms/doa/sites/probe-site/export-pdf", "ctms.doa.export_pdf", null)]
    [InlineData("imaging", "POST", "/api/v1/imaging/adjudications/8d1c4f0e-2f8b-4c39-9a51-0c6d7c1b2a10/chair-adjudicate", "imaging.adjudication.chair_adjudicate", """{"finalResponse":"CR","adjudicationRationale":"Central review consensus"}""")]
    [InlineData("payments", "POST", "/api/v1/payments/invoices/INV-101/approve", "payments.invoice.approve", "{}")]
    [InlineData("payments", "POST", "/api/v1/payments/invoices/INV-101/remittance-advice/sign", "payments.remittance.sign", """{"signature_reason":"Approve remittance"}""")]
    [InlineData("interop", "POST", "/api/v1/interop/esource/reconcile/accept", "interop.esource.accept", """{"candidateId":"8d1c4f0e-2f8b-4c39-9a51-0c6d7c1b2a10","reasonForChange":"Accept EHR observation"}""")]
    [InlineData("interop", "POST", "/api/v1/interop/esource/reconcile/batch-accept", "interop.esource.batch_accept", """{"candidateIds":["8d1c4f0e-2f8b-4c39-9a51-0c6d7c1b2a10"],"reasonForChange":"Batch accept EHR observations"}""")]
    [InlineData("interop", "POST", "/api/v1/interop/esource/reconcile/reject", "interop.esource.reject", """{"candidateId":"8d1c4f0e-2f8b-4c39-9a51-0c6d7c1b2a10","reasonForChange":"Reject duplicate EHR observation"}""")]
    [InlineData("interop", "POST", "/api/v1/interop/esource/reconcile/batch-reject", "interop.esource.batch_reject", """{"candidateIds":["8d1c4f0e-2f8b-4c39-9a51-0c6d7c1b2a10"],"reasonForChange":"Batch reject duplicate observations"}""")]
    [InlineData("execution", "PATCH", "/api/v1/execution/subjects/SUBJ-101/state", "execution.subject.unblind", """{"status":"UNBLINDED"}""")]
    [InlineData("execution", "PATCH", "/api/v1/execution/subjects/SUBJ-101/state", "execution.subject.withdraw", """{"status":"WITHDRAWN"}""")]
    [InlineData("execution", "POST", "/api/v1/execution/rtsm/cgt/bedside-verify", "execution.cgt.bedside_verify", """{"studyId":"STUDY-1","siteId":"probe-site","subjectId":"SUBJ-1","din":"=W12342698765400","bagBarcodePayload":"P","patientWristbandId":"SUBJ-1","primaryVerifierId":"N1","secondaryVerifierId":"N2"}""")]
    [InlineData("execution", "POST", "/api/v1/execution/rtsm/cgt/destruction-certificate", "execution.cgt.destruction_certificate", """{"studyId":"STUDY-1","siteId":"probe-site","chainOfCustodyId":"8d1c4f0e-2f8b-4c39-9a51-0c6d7c1b2a10","witness1Id":"W1","witness2Id":"W2","destructionMethod":"AUTOCLAVE"}""")]
    [InlineData("execution", "POST", "/api/v1/execution/devices/quarantine/release", "execution.device.quarantine_release", """{"quarantineRecordId":"QR-1","releaseJustification":"QA clearance verified"}""")]
    [InlineData("execution", "POST", "/api/v1/execution/rtsm/kits/reconcile-batch", "execution.rtsm.reconcile_batch", """{"studyId":"STUDY-1","siteId":"probe-site","items":[]}""")]
    [GxPRequirement("SRS-SYS-005")]
    [GxPRequirement("PRD-SUB-005")]
    public async Task NewlyGatedSignatureRoutes_Return401ReauthenticationRequired_WhenSigTokenMissingOrReplayed(
        string service,
        string method,
        string pathAndQuery,
        string semanticAction,
        string? jsonBody)
    {
        var replayStore = new ProbeSigTokenUseStore();
        await using var booted = ServiceHosts.All[service](services =>
        {
            foreach (var existing in services.Where(d => d.ServiceType == typeof(ISigTokenUseStore)).ToList())
            {
                services.Remove(existing);
            }

            services.AddSingleton<ISigTokenUseStore>(replayStore);
        });

        string pathOnly = pathAndQuery.Split('?')[0];
        var endpoint = booted.Endpoints
            .Where(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) != false)
            .First(e => e.Metadata.GetMetadata<ElectronicSignatureRequired>() is not null
                && RouteMatchesConcretePath(e.RoutePattern, pathOnly));

        string permission = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy!)
            .First(p => p.StartsWith(PermissionPolicyProvider.PolicyPrefix, StringComparison.Ordinal))[PermissionPolicyProvider.PolicyPrefix.Length..];

        string changeReason = "Emergency clinical justification exceeding fifty characters for regulated step-up verification";
        var caller = CallerHolding(permission) with
        {
            SiteIds = [ProbeSite, "SITE-101"],
            UnblindedAccess = true,
            ChangeReason = changeReason,
        };
        if (string.Equals(pathOnly, "/api/v1/execution/subjects/SUBJ-101/state", StringComparison.Ordinal))
        {
            caller = TestSecurityContext.Identity("pi-sig-probe", "principal_investigator") with
            {
                SiteIds = [ProbeSite, "SITE-101"],
                UnblindedAccess = true,
                ChangeReason = changeReason,
            };
        }

        // 1. Missing X-Sig-Token -> 401 REAUTHENTICATION_REQUIRED
        using (var noSigClient = booted.CreateSignedClient(caller with { SigToken = null }))
        {
            using var missingReq = BuildProbeHttpRequest(method, pathAndQuery, jsonBody);
            using var missingResp = await noSigClient.SendAsync(missingReq, Ct);
            missingResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{service} {method} {pathOnly} must reject missing X-Sig-Token");
            string missingBody = await missingResp.Content.ReadAsStringAsync(Ct);
            missingBody.Should().Contain(SigTokenVerifier.ReauthenticationRequired);
        }

        // 2. First use of fresh X-Sig-Token -> accepted and consumed (not 401)
        string sigToken = TestSecurityContext.CreateSigToken(
            userId: caller.UserId,
            action: pathOnly,
            semanticAction: semanticAction,
            roles: caller.Roles.ToArray());

        using var signedClient = booted.CreateSignedClient(caller with { SigToken = sigToken });
        using (var firstReq = BuildProbeHttpRequest(method, pathAndQuery, jsonBody))
        {
            using var firstResp = await signedClient.SendAsync(firstReq, Ct);
            firstResp.StatusCode.Should().NotBe(
                HttpStatusCode.Unauthorized,
                $"{service} {method} {pathOnly} must accept a valid first-use X-Sig-Token before handler execution");
        }

        // 3. Replayed X-Sig-Token -> 401 REAUTHENTICATION_REQUIRED
        using var replayReq = BuildProbeHttpRequest(method, pathAndQuery, jsonBody);
        using var replayResp = await signedClient.SendAsync(replayReq, Ct);
        replayResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{service} {method} {pathOnly} must reject replayed X-Sig-Token");
        string replayBody = await replayResp.Content.ReadAsStringAsync(Ct);
        replayBody.Should().Contain(SigTokenVerifier.ReauthenticationRequired);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ExecutionMutatingRoutes_Return400BeforeHandler_WhenChangeReasonMissingOrUnderTenChars_AndDecodeRfc8187()
    {
        await using var booted = ServiceHosts.All["execution"](null);
        var caller = TestSecurityContext.Identity("dm-reason-probe", "Data Manager") with
        {
            SiteIds = [ProbeSite],
        };

        // Missing X-Change-Reason -> 400 Bad Request
        using (var noReasonClient = booted.CreateSignedClient(caller with { ChangeReason = null }))
        {
            using var req = BuildProbeHttpRequest(HttpMethods.Post, "/api/v1/execution/sites/probe-site/activate", """{"study_id":"STUDY-1"}""");
            using var resp = await noReasonClient.SendAsync(req, Ct);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await resp.Content.ReadAsStringAsync(Ct)).Should().Contain("X-Change-Reason header is required.");
        }

        // Fewer than 10 characters -> 400 Bad Request
        using (var shortReasonClient = booted.CreateSignedClient(caller with { ChangeReason = "TooShort9" }))
        {
            using var req = BuildProbeHttpRequest(HttpMethods.Post, "/api/v1/execution/sites/probe-site/activate", """{"study_id":"STUDY-1"}""");
            using var resp = await shortReasonClient.SendAsync(req, Ct);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await resp.Content.ReadAsStringAsync(Ct)).Should().Contain("at least 10 characters");
        }

        // RFC 8187 UTF-8 encoded < 10 characters -> decoded and rejected with 400 Bad Request
        using (var shortRfc8187Client = booted.CreateSignedClient(caller with { ChangeReason = null }))
        {
            using var req = BuildProbeHttpRequest(HttpMethods.Post, "/api/v1/execution/sites/probe-site/activate", """{"study_id":"STUDY-1"}""");
            req.Headers.TryAddWithoutValidation(GatewayHeaders.ChangeReason, "UTF-8''%E7%9F%AD%E3%81%84");
            using var resp = await shortRfc8187Client.SendAsync(req, Ct);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await resp.Content.ReadAsStringAsync(Ct)).Should().Contain("at least 10 characters");
        }
    }

    private static bool RouteMatchesConcretePath(RoutePattern pattern, string concretePath)
    {
        var concreteSegments = concretePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pattern.PathSegments.Count != concreteSegments.Length)
        {
            return false;
        }

        for (int i = 0; i < concreteSegments.Length; i++)
        {
            var part = pattern.PathSegments[i].Parts.FirstOrDefault();
            if (part is RoutePatternLiteralPart literal
                && !string.Equals(literal.Content, concreteSegments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

}
