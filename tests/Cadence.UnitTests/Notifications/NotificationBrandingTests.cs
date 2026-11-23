using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Notifications.Infrastructure;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Notifications;

/// <summary>
/// #4983: a notification's brand name and portal link come from configuration. A caller cannot point the
/// "Log In" button of an official-looking email somewhere else.
/// </summary>
public sealed class NotificationBrandingTests
{
    private const string ConfiguredPortal = "https://portal.cadence.test/signin";

    // The caller tries to set the branding fields the service used to accept.
    private static readonly object HostileRequest = new
    {
        studyId = "STUDY-1",
        eventId = "EVT-1",
        timestampUtc = "2026-09-30T00:00:00Z",
        payload = new { subjectId = "SUBJ-1", protocolVersion = "3.0" },
        portalUrl = "javascript:alert(document.cookie)",
        brandName = "Totally Official Sponsor",
    };

    [Fact]
    public async Task Render_UsesTheConfiguredPortalAndBrand_AndIgnoresTheCallers()
    {
        await using var factory = new NotificationsFactory(ConfiguredPortal, "Cadence Test Brand");

        string content = await RenderReconsentAsync(factory);

        content.Should().Contain($"href=\"{ConfiguredPortal}\"");
        content.Should().Contain("Cadence Test Brand");
        content.Should().NotContain("javascript:");
        content.Should().NotContain("Totally Official Sponsor");
    }

    [Fact]
    public async Task Render_WithoutAConfiguredPortal_CarriesNoPortalLink()
    {
        await using var factory = new NotificationsFactory(portalUrl: null, brandName: null);

        string content = await RenderReconsentAsync(factory);

        content.Should().NotContain("Log In to Subject Portal");
        content.Should().NotContain("href=");
        content.Should().Contain("Cadence Clinical Platform");
    }

    // Checked on the registration Program.cs uses rather than by starting the host: a WebApplicationFactory whose
    // host fails to start can surface an ObjectDisposedException instead of the startup error under a parallel run.
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://portal.cadence.test")]
    [InlineData("/relative/path")]
    [InlineData("ftp://portal.cadence.test")]
    public void StartupValidation_RefusesAPortalUrlThatIsNotHttps(string portalUrl)
    {
        using var provider = BrandingServices(portalUrl);

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{NotificationBrandingOptions.InvalidPortalUrlMessage}*");
    }

    [Fact]
    public void StartupValidation_RefusesAnEmptyBrandName()
    {
        using var provider = BrandingServices(portalUrl: null, brandName: " ");

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{NotificationBrandingOptions.MissingBrandNameMessage}*");
    }

    [Fact]
    public void StartupValidation_AcceptsAnHttpsPortal()
    {
        using var provider = BrandingServices(ConfiguredPortal);

        provider.GetRequiredService<IStartupValidator>().Validate();

        provider.GetRequiredService<IOptions<NotificationBrandingOptions>>().Value.PortalUrl.Should().Be(ConfiguredPortal);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://portal.cadence.test", true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("http://127.0.0.1:3000", true)]
    [InlineData("http://portal.cadence.test", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,hi", false)]
    [InlineData("//portal.cadence.test", false)]
    public void IsValidPortalUrl_AllowsHttpsOrLoopbackHttpOnly(string? value, bool expected) =>
        NotificationBrandingOptions.IsValidPortalUrl(value).Should().Be(expected);

    private static async Task<string> RenderReconsentAsync(NotificationsFactory factory)
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("notify-caller", "CRA"));

        var response = await client.PostAsJsonAsync(
            "/api/v1/notifications/render?templateName=reconsent_required", HostileRequest, Ct);

        string body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("content").GetString()!;
    }

    private static ServiceProvider BrandingServices(string? portalUrl, string? brandName = null)
    {
        var settings = new Dictionary<string, string?> { ["Notifications:PortalUrl"] = portalUrl };
        if (brandName is not null)
        {
            settings["Notifications:BrandName"] = brandName;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddNotificationBranding(configuration).BuildServiceProvider();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class NotificationsFactory(string? portalUrl, string? brandName)
        : WebApplicationFactory<Cadence.Apps.Notifications.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString().DisableBackgroundHostedServices();
            if (portalUrl is not null)
            {
                builder.UseSetting("Notifications:PortalUrl", portalUrl);
            }

            if (brandName is not null)
            {
                builder.UseSetting("Notifications:BrandName", brandName);
            }
        }
    }
}
