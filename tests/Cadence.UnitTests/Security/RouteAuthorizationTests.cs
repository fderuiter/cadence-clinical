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
        "/openapi/{documentName}.json", "/scalar/{documentName?}", "/scalar/scalar.js", "/scalar/scalar.aspnetcore.js",
    ];

    /// <summary>
    /// Routes open to any signed-in caller, with why each needs no role permission. A new one is a review decision, so it
    /// must be added here.
    /// </summary>
    private static readonly (string Host, string Route, string Reason)[] SignedInCallerRoutes =
    [
        ("kernel", "/api/v1/kernel/*", "The kernel authorizes each object from the caller's grants: reads mask what they do not cover, commands go through the authority evaluator."),
        ("kernel", "POST /api/v1/ai/intent/route", "Matches fixed patterns, reads and writes no record, and filters recommendations by the caller's signed roles."),
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
}
