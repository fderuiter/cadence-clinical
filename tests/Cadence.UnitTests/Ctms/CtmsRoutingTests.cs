using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Ctms;

public sealed class CtmsRoutingTests : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly TestServer _server;
    private readonly CtmsDbContext _db;

    public CtmsRoutingTests()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        _db = new CtmsDbContext(options);

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseTestGatewaySecret();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IClock>(new FakeClock(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero)));
                    services.AddSingleton(_db);
                    services.AddCadenceSecurity();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseCadenceSecurity();
                    app.UseEndpoints(endpoints =>
                    {
                        var ctmsApi = endpoints.MapGroup("/api/v1/ctms");
                        ctmsApi.MapMonitoringVisitEndpoints();
                        ctmsApi.MapSiteMilestoneEndpoints();
                        ctmsApi.MapDoaEndpoints();
                    });
                });
            });

        _host = builder.Start();
        _server = _host.GetTestServer();
    }

    private HttpClient CreateClient(
        string role = "sysadmin",
        string userId = "admin-1",
        string[]? studyIds = null,
        string[]? siteIds = null)
    {
        var normalized = RoleNormalization.GetNormalizedRoles([role]);
        bool siteScoped = normalized.Any(SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains);
        var identity = new GatewayIdentity(
            UserId: userId,
            Roles: [role],
            SiteIds: siteIds ?? (siteScoped ? ["SITE-1"] : []),
            TenantId: TestSecurityContext.TenantCode,
            StudyIds: studyIds ?? ["*"]);
        var handler = new TestSecurityContext.GatewaySigningHandler(identity)
        {
            InnerHandler = _server.CreateHandler()
        };
        return new HttpClient(handler)
        {
            BaseAddress = _server.BaseAddress
        };
    }

    [Fact]
    public async Task GetMonitoringVisits_ReturnsOk()
    {
        using var client = CreateClient("CRA");

        var response = await client.GetAsync("/api/v1/ctms/monitoring-visits?study_id=STUDY-1&site_id=SITE-1", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<List<MonitoringVisitResponseDto>>(TestContext.Current.CancellationToken);
        content.Should().NotBeNull();
    }

    [Fact]
    public async Task ScheduleMonitoringVisit_ThroughHttp_Returns201Created()
    {
        using var client = CreateClient("CRA");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ctms/monitoring-visits")
        {
            Content = JsonContent.Create(new
            {
                study_id = "STUDY-1",
                site_id = "SITE-1",
                cra_id = "cra_user",
                visit_type = "IMV",
                scheduled_date = "2026-12-01T09:00:00Z"
            })
        };
        request.Headers.Add("X-Change-Reason", "Scheduled via HTTP route");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("status").GetString().Should().Be("SCHEDULED");
        body.GetProperty("study_id").GetString().Should().Be("STUDY-1");
        body.GetProperty("site_id").GetString().Should().Be("SITE-1");
    }

    [Fact]
    public async Task CompleteAndSignOffMonitoringVisit_ThroughHttp_TransitionsStatus()
    {
        using var craClient = CreateClient("CRA");
        // 1. Schedule
        using var schedReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ctms/monitoring-visits")
        {
            Content = JsonContent.Create(new
            {
                study_id = "STUDY-1",
                site_id = "SITE-1",
                cra_id = "cra_user",
                visit_type = "IMV",
                scheduled_date = "2026-11-01T09:00:00Z"
            })
        };
        schedReq.Headers.Add("X-Change-Reason", "Initial schedule");
        var schedRes = await craClient.SendAsync(schedReq, TestContext.Current.CancellationToken);
        schedRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var visitDoc = await schedRes.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var visitId = visitDoc.GetProperty("id").GetString()!;

        // 2. Complete
        using var compReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ctms/monitoring-visits/{visitId}/complete")
        {
            Content = JsonContent.Create(new
            {
                actual_date = "2026-11-02T16:00:00Z",
                findings = new[]
                {
                    new { text = "Minor deviation", severity = "MINOR", resolution_status = "OPEN" }
                }
            })
        };
        compReq.Headers.Add("X-Change-Reason", "Completed IMV visit");
        var compRes = await craClient.SendAsync(compReq, TestContext.Current.CancellationToken);
        compRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var compDoc = await compRes.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        compDoc.GetProperty("status").GetString().Should().Be("COMPLETED");

        // 3. Sign-off
        using var monitorClient = CreateClient("monitor", "manager-1");
        using var signReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ctms/monitoring-visits/{visitId}/sign-off")
        {
            Content = JsonContent.Create(new { })
        };
        signReq.Headers.Add("X-Change-Reason", "Trip report endorsed");
        var signRes = await monitorClient.SendAsync(signReq, TestContext.Current.CancellationToken);
        signRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var signDoc = await signRes.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        signDoc.GetProperty("status").GetString().Should().Be("SIGNED_OFF");
        signDoc.GetProperty("signer").GetString().Should().Be("manager-1");
    }

    [Fact]
    public async Task SiteMilestoneLifecycle_ThroughHttp_Succeeds()
    {
        using var client = CreateClient("sysadmin");

        // 1. Create milestone
        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ctms/site-milestones")
        {
            Content = JsonContent.Create(new
            {
                site_id = "SITE-1",
                study_id = "STUDY-1",
                milestone_type = "SITE_ACTIVATION",
                planned_date = "2026-11-20T00:00:00Z"
            })
        };
        createReq.Headers.Add("X-Change-Reason", "Planned activation");
        var createRes = await client.SendAsync(createReq, TestContext.Current.CancellationToken);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var msDoc = await createRes.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var msId = msDoc.GetProperty("id").GetString()!;
        msDoc.GetProperty("status").GetString().Should().Be("PLANNED");

        // 2. List milestones
        var listRes = await client.GetAsync("/api/v1/ctms/site-milestones?study_id=STUDY-1&site_id=SITE-1", TestContext.Current.CancellationToken);
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Update milestone to ACHIEVED
        using var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/ctms/site-milestones/{msId}")
        {
            Content = JsonContent.Create(new
            {
                status = "ACHIEVED",
                actual_date = "2026-11-15T12:00:00Z"
            })
        };
        updateReq.Headers.Add("X-Change-Reason", "Site activated on time");
        var updateRes = await client.SendAsync(updateReq, TestContext.Current.CancellationToken);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedDoc = await updateRes.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        updatedDoc.GetProperty("status").GetString().Should().Be("ACHIEVED");
    }

    [Fact]
    public async Task CrossScopeRequests_ThroughHttp_Return403WithForbiddenScopeCode()
    {
        using var siteScopedCra = CreateClient("CRA", "cra-scoped", studyIds: ["STUDY-1"], siteIds: ["SITE-1"]);

        var crossSiteVisits = await siteScopedCra.GetAsync("/api/v1/ctms/monitoring-visits?study_id=STUDY-1&site_id=SITE-2", TestContext.Current.CancellationToken);
        crossSiteVisits.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var visitsDoc = await crossSiteVisits.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        visitsDoc.GetProperty("code").GetString().Should().Be(CtmsScopeGuard.ForbiddenCode);

        var crossStudyMilestones = await siteScopedCra.GetAsync("/api/v1/ctms/site-milestones?study_id=STUDY-2&site_id=SITE-1", TestContext.Current.CancellationToken);
        crossStudyMilestones.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var crossSiteDoa = await siteScopedCra.GetAsync("/api/v1/ctms/doa/sites/SITE-2/log", TestContext.Current.CancellationToken);
        crossSiteDoa.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public async ValueTask DisposeAsync()
    {
        _server.Dispose();
        _host.Dispose();
        await _db.DisposeAsync();
    }
}
