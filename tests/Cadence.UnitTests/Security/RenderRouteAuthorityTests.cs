using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Cadence.UnitTests.Security;

/// <summary>
/// The template render routes produce regulatory and alert documents, so each needs a permission from the matrix
/// (#4960, ADR-2511): an ICSR needs <c>safety_report:create</c>, a notification <c>notification:send</c> and a
/// visit letter <c>ctms_monitoring_visit:create</c>. Any other signed caller gets 403.
/// </summary>
public sealed class RenderRouteAuthorityTests(
    RenderRouteAuthorityTests.SafetyFactory safety,
    RenderRouteAuthorityTests.NotificationsFactory notifications,
    RenderRouteAuthorityTests.CtmsFactory ctms)
    : IClassFixture<RenderRouteAuthorityTests.SafetyFactory>,
      IClassFixture<RenderRouteAuthorityTests.NotificationsFactory>,
      IClassFixture<RenderRouteAuthorityTests.CtmsFactory>
{
    // A complete ICH E2B(R3) report, so a refusal can only come from authorization (#5573).
    private static object Icsr => Cadence.UnitTests.Safety.IcsrFixture.CompleteReport();

    private static readonly object Notification = new
    {
        studyId = "CAD-101",
        eventId = "EVT-1",
        timestampUtc = "2026-09-30T06:00:00Z",
        payload = new { subjectId = "017-0001", adverseEventDescription = "Headache" },
    };

    private static readonly object Letter = new { craId = "cra-1", createdAt = "2026-09-30", studyId = "CAD-101", siteId = "017" };

    [Theory]
    [InlineData("Sponsor Medical Monitor", HttpStatusCode.OK)]
    [InlineData("sysadmin", HttpStatusCode.OK)]
    [InlineData("CRA", HttpStatusCode.Forbidden)]
    [InlineData("Site Investigator", HttpStatusCode.Forbidden)]
    [InlineData("Subject", HttpStatusCode.Forbidden)]
    public async Task IcsrRender_NeedsSafetyReportCreate(string role, HttpStatusCode expected)
    {
        using var client = safety.CreateSignedClient(TestSecurityContext.Identity("safety-caller", role));

        var response = await client.PostAsJsonAsync("/api/v1/safety/icsr/render", Icsr, Ct);

        await ShouldBeAsync(response, expected);
    }

    [Theory]
    [InlineData("Data Manager", HttpStatusCode.OK)]
    [InlineData("CRA", HttpStatusCode.OK)]
    [InlineData("Sponsor Medical Monitor", HttpStatusCode.OK)]
    [InlineData("Site Investigator", HttpStatusCode.Forbidden)]
    [InlineData("Auditor", HttpStatusCode.Forbidden)]
    [InlineData("Subject", HttpStatusCode.Forbidden)]
    public async Task NotificationRender_NeedsNotificationSend(string role, HttpStatusCode expected)
    {
        using var client = notifications.CreateSignedClient(TestSecurityContext.Identity("notify-caller", role));

        var response = await client.PostAsJsonAsync(
            "/api/v1/notifications/render?templateName=sae_reconciliation", Notification, Ct);

        await ShouldBeAsync(response, expected);
    }

    [Theory]
    [InlineData("CRA", HttpStatusCode.OK)]
    [InlineData("Data Manager", HttpStatusCode.OK)]
    [InlineData("Site Investigator", HttpStatusCode.Forbidden)]
    [InlineData("Auditor", HttpStatusCode.Forbidden)]
    [InlineData("Subject", HttpStatusCode.Forbidden)]
    public async Task LetterRender_NeedsMonitoringVisitCreate(string role, HttpStatusCode expected)
    {
        using var client = ctms.CreateSignedClient(TestSecurityContext.Identity("ctms-caller", role));

        var response = await client.PostAsJsonAsync(
            "/api/v1/ctms/letters/render?templateName=confirmation_letter", Letter, Ct);

        await ShouldBeAsync(response, expected);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task ShouldBeAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));

    /// <summary>The Safety service with the test gateway secret.</summary>
    public sealed class SafetyFactory : WebApplicationFactory<Cadence.Apps.Safety.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
    }

    /// <summary>The Notifications service with the test gateway secret.</summary>
    public sealed class NotificationsFactory : WebApplicationFactory<Cadence.Apps.Notifications.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
    }

    /// <summary>The CTMS service with the test gateway secret.</summary>
    public sealed class CtmsFactory : WebApplicationFactory<Cadence.Apps.Ctms.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
    }
}
