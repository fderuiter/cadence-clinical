using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Safety;

/// <summary>
/// Every safety route denies by default (ADR-2516, #5464): an unsigned request is challenged (401) and a signed caller
/// whose roles lack the route's permission is refused (403) before the handler runs, so nothing is read or written.
/// The table below pins the permission of each route, and <see cref="TheTable_ListsEverySafetyRouteWithItsPermission"/>
/// fails when a route is added, removed or changes permission without the table following.
/// </summary>
public sealed class SafetyRouteAuthorityTests(DsurApprovalAuthorityTests.SafetyFactory factory)
    : IClassFixture<DsurApprovalAuthorityTests.SafetyFactory>
{
    private const string ReportId = "{id}";

    private static readonly GatewayIdentity Compiler = TestSecurityContext.Identity("route-mm-compiler", "Sponsor Medical Monitor");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Each safety route under <c>/api/v1/safety</c> (other than the anonymous status probe) and the permission it needs.</summary>
    public static TheoryData<string, string, string> Routes => new()
    {
        { "POST", "/api/v1/safety/icsr/render", IcsrTemplateEndpoints.RenderPermission },
        { "POST", "/api/v1/safety/dsur/compile", DsurEndpoints.SafetyPermission },
        { "POST", "/api/v1/safety/dsur/{id}/approve", DsurEndpoints.ApprovePermission },
        { "PUT", "/api/v1/safety/dsur/{id}", DsurEndpoints.SafetyPermission },
        { "GET", "/api/v1/safety/dsur/{id}", DsurEndpoints.ReadPermission },
        { "GET", "/api/v1/safety/dsur", DsurEndpoints.ReadPermission },
        { "POST", "/api/v1/safety/periodic-reports/compile", DsurEndpoints.SafetyPermission },
        { "POST", "/api/v1/safety/periodic-reports/{id}/approve", DsurEndpoints.ApprovePermission },
        { "PUT", "/api/v1/safety/periodic-reports/{id}", DsurEndpoints.SafetyPermission },
        { "GET", "/api/v1/safety/periodic-reports/{id}", DsurEndpoints.ReadPermission },
        { "GET", "/api/v1/safety/periodic-reports", DsurEndpoints.ReadPermission },
    };

    /// <summary>
    /// An unsigned request gets 401; a signed caller with no safety permission (a CRA, or no role at all) gets 403, and
    /// on the approve routes so does the system administrator, who holds <c>safety_report:create</c> and
    /// <c>safety_report:read</c> but not <c>safety_report:approve</c>. No refused request compiles, changes or signs a
    /// report.
    /// </summary>
    [Theory]
    [MemberData(nameof(Routes))]
    [GxPRequirement("PRD-SYS-192")]
    public async Task EverySafetyRoute_Refuses401Unsigned_And403WithoutThePermission(string method, string route, string permission)
    {
        string id = await CompileDraftAsync();
        int reportsBefore = (await ReportsAsync()).Count;
        var lacking = new List<GatewayIdentity>
        {
            TestSecurityContext.Identity("route-cra", "CRA"),
            TestSecurityContext.Identity("route-no-role"),
        };
        if (permission == DsurEndpoints.ApprovePermission)
        {
            lacking.Add(TestSecurityContext.Identity("route-sysadmin", "sysadmin"));
        }

        using (var unsigned = factory.CreateClient())
        {
            using var response = await SendAsync(unsigned, method, route, id, signerForToken: "route-anonymous");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {route} without a signed caller");
        }

        foreach (var identity in lacking)
        {
            using var client = factory.CreateSignedClient(identity);
            using var response = await SendAsync(client, method, route, id, signerForToken: identity.UserId);
            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden, $"{method} {route} as [{string.Join(", ", identity.Roles)}] lacks {permission}");
        }

        var reports = await ReportsAsync();
        reports.Should().HaveCount(reportsBefore, "a refused compile writes nothing");
        var report = reports.Single(r => r.Id == id);
        report.Status.Should().Be("DRAFT");
        report.VersionIndex.Should().Be(1);
        report.CompoundName.Should().Be("Route-Compound");
        report.ApprovedByUserId.Should().BeNull();
    }

    /// <summary>
    /// The host maps exactly the routes in <see cref="Routes"/> under <c>/api/v1/safety</c>, besides the anonymous status
    /// probe, and each one requires the permission the table names.
    /// </summary>
    [Fact]
    public void TheTable_ListsEverySafetyRouteWithItsPermission()
    {
        var mapped = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && raw.StartsWith("/api/v1/safety", StringComparison.Ordinal))
            .Where(e => e.RoutePattern.RawText != "/api/v1/safety/status")
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (
                Method: m,
                Route: e.RoutePattern.RawText!,
                Permissions: string.Join(
                    ",",
                    e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                        .Select(a => a.Policy)
                        .Where(p => p is not null && p.StartsWith(PermissionPolicyProvider.PolicyPrefix, StringComparison.Ordinal))
                        .Select(p => p![PermissionPolicyProvider.PolicyPrefix.Length..])
                        .Distinct()))))
            .Select(r => $"{r.Method} {r.Route} {r.Permissions}")
            .ToList();

        var expected = Routes.Select(row => $"{row.Data.Item1} {row.Data.Item2} {row.Data.Item3}").ToList();

        mapped.Should().BeEquivalentTo(expected);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string route, string id, string signerForToken)
    {
        string path = route.Replace(ReportId, id, StringComparison.Ordinal);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "GET")
        {
            request.Content = JsonContent.Create(BodyFor(route));
        }

        if (route.EndsWith("/approve", StringComparison.Ordinal))
        {
            request.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(signerForToken, DsurEndpoints.ApproveAction(id), DsurEndpoints.ApproveSemanticAction));
        }

        return await client.SendAsync(request, Ct);
    }

    /// <summary>A valid body for each route, so a refusal can only come from authorization.</summary>
    private static object BodyFor(string route) => route switch
    {
        _ when route.EndsWith("/icsr/render", StringComparison.Ordinal) => IcsrFixture.CompleteReport(),
        _ when route.EndsWith("/compile", StringComparison.Ordinal) => new
        {
            compoundName = "Refused-Compound",
            studyId = "STUDY-ROUTE",
            periodStart = "2025-01-01T00:00:00Z",
            periodEnd = "2025-12-31T23:59:59Z",
            reasonForChange = "Refused compilation attempt",
        },
        _ when route.EndsWith("/approve", StringComparison.Ordinal) => new { reasonForChange = "Refused approval attempt" },
        _ => new { compoundName = "Refused-Rename", reasonForChange = "Refused update attempt" },
    };

    private async Task<string> CompileDraftAsync()
    {
        using var client = factory.CreateSignedClient(Compiler);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/safety/dsur/compile",
            new
            {
                compoundName = "Route-Compound",
                studyId = "STUDY-ROUTE",
                periodStart = "2025-01-01T00:00:00Z",
                periodEnd = "2025-12-31T23:59:59Z",
                reasonForChange = "Route authority fixture",
            },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PeriodicSafetyReportRecord>(Ct))!.Id;
    }

    private async Task<List<PeriodicSafetyReportRecord>> ReportsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SafetyDbContext>();
        return await db.PeriodicSafetyReports.AsNoTracking().ToListAsync(Ct);
    }
}
