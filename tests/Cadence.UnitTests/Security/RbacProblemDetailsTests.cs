using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Endpoints;
using Cadence.Kernel;
using Cadence.Kernel.References;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Security;

/// <summary>
/// Verifies ADR-2482 <c>ProblemDetails.extensions["explanation"]</c> across standalone core services (PRD-SYS-001,
/// PRD-CRF-015, PRD-SYS-192): every protected standalone route answered with HTTP 403 by the authorization middleware,
/// <see cref="ClinicalScopeGuard"/>, or <see cref="CtmsScopeGuard"/> returns structured plain-English explanations
/// without leaking raw <c>resource:action</c> permission tokens in user-facing claims or condition descriptions.
/// </summary>
public sealed partial class RbacProblemDetailsTests
{
    private const string ProbeSite = "probe-site";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> StandaloneHosts =>
        new(ServiceHosts.All.Keys.Where(name => !string.Equals(name, "kernel", StringComparison.Ordinal)));

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task AllProtectedStandaloneRoutes_ReturnProblemDetailsWithExplanation_On403Denial()
    {
        int totalProtectedStandaloneRoutes = 0;
        var problems = new List<string>();

        foreach (string host in ServiceHosts.All.Keys.Where(name => !string.Equals(name, "kernel", StringComparison.Ordinal)))
        {
            await using var booted = ServiceHosts.All[host](null);
            var routes = PermissionRoutes(booted);
            totalProtectedStandaloneRoutes += routes.Count;

            var clientCache = new Dictionary<string, HttpClient>(StringComparer.Ordinal);
            HttpClient ClientFor(GatewayIdentity id)
            {
                string key = $"{id.UserId}|{string.Join(',', id.Roles)}";
                if (!clientCache.TryGetValue(key, out var cached))
                {
                    cached = booted.CreateSignedClient(id);
                    clientCache[key] = cached;
                }

                return cached;
            }

            try
            {
                foreach (var route in routes)
                {
                    var identity = CallerLacking(route.Permission);
                    using var request = new HttpRequestMessage(new HttpMethod(route.Method), route.Path);
                    if (!HttpMethods.IsGet(route.Method) && !HttpMethods.IsDelete(route.Method))
                    {
                        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    }

                    using var response = await ClientFor(identity).SendAsync(request, Ct);
                    if (response.StatusCode != HttpStatusCode.Forbidden)
                    {
                        problems.Add($"{host} {route}: expected 403 Forbidden, got {response.StatusCode}");
                        continue;
                    }

                    string mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                    if (!string.Equals(mediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
                    {
                        problems.Add($"{host} {route}: expected application/problem+json, got '{mediaType}'");
                        continue;
                    }

                    using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Ct), cancellationToken: Ct);
                    if (!doc.RootElement.TryGetProperty("explanation", out JsonElement explanation))
                    {
                        problems.Add($"{host} {route}: missing ProblemDetails.explanation");
                        continue;
                    }

                    string claim = explanation.GetProperty("claim").GetString() ?? string.Empty;
                    string producer = explanation.GetProperty("producer").GetString() ?? string.Empty;
                    var conditions = explanation.GetProperty("conditions");
                    var nextActions = explanation.GetProperty("next_actions");

                    if (string.IsNullOrWhiteSpace(claim) || !string.Equals(producer, RbacExplanationBuilder.RbacProducer, StringComparison.Ordinal))
                    {
                        problems.Add($"{host} {route}: invalid claim='{claim}' or producer='{producer}'");
                    }

                    if (RawPermissionTokenRegex().IsMatch(claim))
                    {
                        problems.Add($"{host} {route}: claim leaked raw permission token: '{claim}'");
                    }

                    if (conditions.GetArrayLength() == 0 || nextActions.GetArrayLength() == 0)
                    {
                        problems.Add($"{host} {route}: conditions or next_actions empty");
                    }

                    foreach (JsonElement condition in conditions.EnumerateArray())
                    {
                        string desc = condition.GetProperty("description").GetString() ?? string.Empty;
                        if (RawPermissionTokenRegex().IsMatch(desc))
                        {
                            problems.Add($"{host} {route}: condition description leaked raw permission token: '{desc}'");
                        }
                    }
                }
            }
            finally
            {
                foreach (var cached in clientCache.Values)
                {
                    cached.Dispose();
                }
            }
        }

        totalProtectedStandaloneRoutes.Should().BeGreaterThanOrEqualTo(113, "all protected standalone core routes must be verified");
        problems.Should().BeEmpty();
    }

    [Fact]
    public async Task ClinicalScopeGuard_And_CtmsScopeGuard_AttachExplanationWithTargetNodeRef()
    {
        var studyId = StudyId.New();
        var siteId = StudySiteId.New();

        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "crc-01",
            TenantId = "tenant-01",
            Roles = ["crc"],
            AssignedStudies = ["OTHER-STUDY"],
            AssignedSites = ["OTHER-SITE"],
        };
        httpContext.Response.Body = new System.IO.MemoryStream();

        IResult? deniedStudy = ClinicalScopeGuard.RequireStudy(httpContext, studyId.Value.ToString("D"));
        deniedStudy.Should().NotBeNull();
        await deniedStudy!.ExecuteAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        httpContext.Response.Body.Position = 0;
        using var studyDoc = await JsonDocument.ParseAsync(httpContext.Response.Body, cancellationToken: Ct);
        studyDoc.RootElement.GetProperty("code").GetString().Should().Be("FORBIDDEN_SCOPE");
        var studyExp = studyDoc.RootElement.GetProperty("explanation");
        studyExp.GetProperty("producer").GetString().Should().Be(RbacExplanationBuilder.RbacProducer);
        studyExp.GetProperty("subject").GetString().Should().Be(NodeRef.For(studyId).ToString());
        studyExp.GetProperty("next_actions")[0].GetProperty("target").GetString().Should().Be(NodeRef.For(studyId).ToString());

        var ctmsSiteDenied = CtmsScopeGuard.RequireSite(httpContext, siteId.Value.ToString("D"));
        ctmsSiteDenied.Should().NotBeNull();
        ctmsSiteDenied!.ProblemDetails.Extensions["code"].Should().Be(CtmsScopeGuard.ForbiddenCode);
        ctmsSiteDenied.ProblemDetails.Extensions.Should().ContainKey("explanation");
        string serialized = JsonSerializer.Serialize(ctmsSiteDenied.ProblemDetails.Extensions["explanation"]);
        serialized.Should().Contain(NodeRef.For(siteId).ToString());
    }

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

    private static GatewayIdentity CallerLacking(string permission) =>
        TestSecurityContext.Identity(
            $"lacks-{permission}",
            [.. PermissionMatrix.ROLE_PERMISSIONS.Keys.Where(role => !Holds(role, permission)).Order(StringComparer.Ordinal)])
        with
        {
            SiteIds = [ProbeSite],
            ChangeReason = "Authorized route verification reason.",
        };

    private static bool Holds(string role, string permission) =>
        PermissionMatrix.HasPermission(
            new Principal { Roles = RoleNormalization.GetNormalizedRoles([role]), RawRoles = [role] },
            permission);

    private sealed record PermissionRoute(string Method, RoutePattern Template, string Permission)
    {
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

    [GeneratedRegex(@"\b[a-z]+_[a-z0-9_]*:[a-z0-9_]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex RawPermissionTokenRegex();
}
