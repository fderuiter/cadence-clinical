using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Gateway;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using Xunit;

using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Health;

namespace Cadence.UnitTests.Gateway;

/// <summary>
/// Checks that the gateway bounds how long it waits on a service and stops forwarding to a service whose
/// readiness probe fails, answering 503 at once instead, until the service is ready again.
/// </summary>
public sealed class GatewayResilienceTests
{
    private const string ProxiedPath = "/api/v1/queries/q-1";

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-191")]
    public async Task EveryCluster_HasTimeoutAndActiveAndPassiveHealthChecks()
    {
        await using var gateway = CreateGateway("http://127.0.0.1:1");

        var clusters = gateway.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().Clusters;

        clusters.Should().HaveCount(18);
        clusters.Should().AllSatisfy(cluster =>
        {
            cluster.HttpRequest!.ActivityTimeout.Should().Be(TimeSpan.FromSeconds(GatewayClusters.DefaultProxyTimeoutSeconds));
            cluster.HealthCheck!.AvailableDestinationsPolicy.Should().Be(HealthCheckConstants.AvailableDestinations.HealthyAndUnknown);
            cluster.HealthCheck.Active!.Enabled.Should().BeTrue();
            cluster.HealthCheck.Active.Path.Should().Be("/healthz/ready");
            cluster.HealthCheck.Passive!.Enabled.Should().BeTrue();
        });
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-191")]
    public async Task ServiceThatIsNotReady_Gets503WithoutForwarding_UntilItIsReadyAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        bool ready = false;
        int forwarded = 0;
        await using var downstream = await StartDownstreamAsync(app =>
        {
            app.MapGet(GatewayClusters.ReadinessPath, () => ready ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
            app.MapGet(ProxiedPath, () =>
            {
                Interlocked.Increment(ref forwarded);
                return Results.Ok();
            });
        });
        await using var gateway = CreateGateway(DownstreamUrl(downstream), ("GATEWAY_HEALTH_CHECK_INTERVAL_SECONDS", "1"));
        using var client = AuthenticatedClient(gateway);

        await WaitForStatusAsync(client, HttpStatusCode.ServiceUnavailable, ct);
        int forwardedWhileUnhealthy = Volatile.Read(ref forwarded);
        (await client.GetAsync(ProxiedPath, ct)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        Volatile.Read(ref forwarded).Should().Be(forwardedWhileUnhealthy, "an unhealthy service must not receive requests");

        ready = true;
        await WaitForStatusAsync(client, HttpStatusCode.OK, ct);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-191")]
    public async Task ServiceThatStopsResponding_Gets504AfterTheProxyTimeout()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var downstream = await StartDownstreamAsync(app =>
        {
            app.MapGet(GatewayClusters.ReadinessPath, () => Results.Ok());
            app.MapGet(ProxiedPath, async (CancellationToken requestAborted) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), requestAborted);
                return Results.Ok();
            });
        });
        await using var gateway = CreateGateway(DownstreamUrl(downstream), ("GATEWAY_PROXY_TIMEOUT_SECONDS", "1"));
        using var client = AuthenticatedClient(gateway);

        var elapsed = Stopwatch.StartNew();
        var response = await client.GetAsync(ProxiedPath, ct);

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    internal static WebApplicationFactory<Cadence.Apps.Gateway.Program> CreateGateway(
        string executionUrl, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Cadence.Apps.Gateway.Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("OIDC_AUTHORITY", GatewayAuthenticationTests.GatewayFixture.Issuer)
                .UseSetting("EXECUTION_SERVICE_URL", executionUrl)
                .UseTestGatewaySecret()
                .ConfigureTestServices(services =>
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        var configuration = new OpenIdConnectConfiguration { Issuer = GatewayAuthenticationTests.GatewayFixture.Issuer };
                        configuration.SigningKeys.Add(GatewayAuthenticationTests.GatewayFixture.SigningKey);
                        options.Configuration = configuration;
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    }));
            foreach (var (key, value) in settings)
            {
                host.UseSetting(key, value);
            }
        });

    internal static HttpClient AuthenticatedClient(WebApplicationFactory<Cadence.Apps.Gateway.Program> gateway, string subject = "cra-01")
    {
        var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", GatewayAuthenticationTests.GatewayFixture.CreateToken(subject, ["CRA"]));
        return client;
    }

    private static async Task<WebApplication> StartDownstreamAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        map(app);
        await app.StartAsync();
        return app;
    }

    private static string DownstreamUrl(WebApplication downstream) =>
        downstream.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

    private static async Task WaitForStatusAsync(HttpClient client, HttpStatusCode expected, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        HttpStatusCode last;
        do
        {
            last = (await client.GetAsync(ProxiedPath, ct)).StatusCode;
            if (last == expected)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15));

        last.Should().Be(expected, "the gateway should reflect the service's readiness within a few probe intervals");
    }
}
