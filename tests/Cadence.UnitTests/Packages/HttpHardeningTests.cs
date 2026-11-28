using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Telemetry;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.UnitTests.TestSupport;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Checks the shared HTTP hardening: forwarded headers are honoured only from configured proxies, and
/// HTTPS responses outside Development carry <c>Strict-Transport-Security</c>.
/// </summary>
public sealed class HttpHardeningTests
{
    private const string HstsHeader = "Strict-Transport-Security";

    [Fact]
    [GxPRequirement("PRD-SYS-189")]
    public void TrustedProxies_AcceptAddressesAndCidrRanges()
    {
        var options = new ForwardedHeadersOptions();

        CadenceHttpHardening.ConfigureForwardedHeaders(options, " 10.0.0.0/8, 192.0.2.10 ,fd00::/8");

        options.KnownIPNetworks.Should().Contain(System.Net.IPNetwork.Parse("10.0.0.0/8"));
        options.KnownIPNetworks.Should().Contain(System.Net.IPNetwork.Parse("fd00::/8"));
        options.KnownProxies.Should().Contain(IPAddress.Parse("192.0.2.10"));
        options.ForwardedHeaders.Should().Be(
            ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost
            | ForwardedHeaders.XForwardedPrefix);
        options.ForwardLimit.Should().BeNull();
    }

    [Theory]
    [InlineData("gateway")]
    [InlineData("10.0.0.0/33")]
    [GxPRequirement("PRD-SYS-189")]
    public void TrustedProxies_WithAnInvalidEntry_Throw(string entry)
    {
        Action configure = () => CadenceHttpHardening.ConfigureForwardedHeaders(new ForwardedHeadersOptions(), entry);

        configure.Should().Throw<InvalidOperationException>().WithMessage($"*{CadenceHttpHardening.TrustedProxiesKey}*");
    }

    [Theory]
    [Trait("Category", "GxP_OQ")]
    [InlineData("10.1.2.3", true)]
    [InlineData("192.0.2.1", false)]
    [GxPRequirement("PRD-SYS-189")]
    public async Task ForwardedHttps_FromATrustedProxyOnly_GetsHsts(string proxyAddress, bool expectHsts)
    {
        await using var service = CreateService("Production", ("CADENCE_TRUSTED_PROXIES", "10.0.0.0/8"));

        var context = await service.Server.SendAsync(http =>
        {
            http.Connection.RemoteIpAddress = IPAddress.Parse(proxyAddress);
            http.Request.Method = HttpMethods.Get;
            http.Request.Scheme = "http";
            http.Request.Host = new HostString("cadence.example.org");
            http.Request.Path = "/healthz/live";
            http.Request.Headers["X-Forwarded-Proto"] = "https";
        }, TestContext.Current.CancellationToken);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Headers.ContainsKey(HstsHeader).Should().Be(expectHsts);
    }

    [Theory]
    [Trait("Category", "GxP_OQ")]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    [GxPRequirement("PRD-SYS-189")]
    public async Task HttpsResponses_CarryHsts_OutsideDevelopment(string environment, bool expectHsts)
    {
        await using var service = CreateService(environment);
        using var client = service.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://cadence.example.org"),
        });

        using var response = await client.GetAsync("/healthz/live", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains(HstsHeader).Should().Be(expectHsts);
        if (expectHsts)
        {
            string.Join(",", response.Headers.GetValues(HstsHeader)).Should().Contain("max-age=31536000");
        }
    }

    [Fact]
    public void AllowedHosts_AcceptHostnamesWildcardsAndIpLiterals()
    {
        var options = new ForwardedHeadersOptions();

        CadenceHttpHardening.ConfigureForwardedHeaders(
            options,
            "10.0.0.0/8",
            " api.example.org, *.internal.example.org ; 192.0.2.25 , [2001:db8::1] ");

        options.AllowedHosts.Should().Equal(
            "api.example.org",
            "*.internal.example.org",
            "192.0.2.25",
            "[2001:db8::1]");
    }

    [Theory]
    [InlineData("https://api.example.org")]
    [InlineData("api.example.org:8443")]
    [InlineData("api.example.org/edge")]
    [InlineData("*.*.example.org")]
    [InlineData("bad host.example.org")]
    public void AllowedHosts_WithInvalidEntry_Throw(string entry)
    {
        Action configure = () => CadenceHttpHardening.ConfigureForwardedHeaders(
            new ForwardedHeadersOptions(),
            trustedProxies: null,
            allowedHosts: entry);

        configure.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{CadenceHttpHardening.AllowedHostsKey}*");
    }

    [Theory]
    [InlineData("api.example.org", "https://api.example.org/edge")]
    [InlineData("portal.trusted.example", "https://portal.trusted.example/edge")]
    [InlineData("evil.attacker.org", "https://internal.cadence.local/edge")]
    public async Task ForwardedHost_WhenRestrictedByAllowedHosts_HonoursAllowlistedHostsOnly(
        string forwardedHost,
        string expectedServerUrl)
    {
        await using var service = CreateService(
            "Development",
            (CadenceHttpHardening.TrustedProxiesKey, "10.0.0.0/8"),
            (CadenceHttpHardening.AllowedHostsKey, "api.example.org,*.trusted.example"));

        var context = await service.Server.SendAsync(http =>
        {
            http.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
            http.Request.Method = HttpMethods.Get;
            http.Request.Scheme = "http";
            http.Request.Host = new HostString("internal.cadence.local");
            http.Request.Path = "/openapi/v1.json";
            http.Request.Headers["X-Forwarded-Proto"] = "https";
            http.Request.Headers["X-Forwarded-Host"] = forwardedHost;
            http.Request.Headers["X-Forwarded-Prefix"] = "/edge";
        }, TestContext.Current.CancellationToken);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        using var doc = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        string? actualUrl = doc.RootElement.GetProperty("servers")[0].GetProperty("url").GetString();
        actualUrl.Should().Be(expectedServerUrl);
    }

    [Fact]
    public async Task HttpsRedirection_WhenEnabledOutsideDevelopment_RedirectsPlainHttpApiRequestsAndExemptsProbes()
    {
        await using var service = CreateStatelessService(
            "Production",
            (CadenceHttpHardening.HttpsRedirectEnabledKey, "true"));
        using var client = service.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://cadence.example.org"),
            AllowAutoRedirect = false,
        });

        using var apiResponse = await client.GetAsync("/api/v1/studies?page=1", TestContext.Current.CancellationToken);
        ((int)apiResponse.StatusCode).Should().BeOneOf(
            StatusCodes.Status307TemporaryRedirect,
            StatusCodes.Status308PermanentRedirect);
        apiResponse.Headers.Location.Should().Be(new Uri("https://cadence.example.org/api/v1/studies?page=1"));

        foreach (string probePath in new[] { "/healthz", "/healthz/live", "/healthz/ready" })
        {
            using var probeResponse = await client.GetAsync(probePath, TestContext.Current.CancellationToken);
            probeResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"probe '{probePath}' must remain reachable over plain HTTP");
        }
    }

    [Fact]
    public async Task HttpsRedirection_WithCustomHttpsPortAndTrustedForwardedHttps_HandlesRedirectsAndAvoidsLoops()
    {
        await using var service = CreateStatelessService(
            "Production",
            (CadenceHttpHardening.TrustedProxiesKey, "10.0.0.0/8"),
            (CadenceHttpHardening.HttpsRedirectEnabledKey, "true"),
            (CadenceHttpHardening.HttpsPortKey, "8443"));

        using var plainClient = service.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://cadence.example.org"),
            AllowAutoRedirect = false,
        });
        using var redirected = await plainClient.GetAsync("/api/v1/studies", TestContext.Current.CancellationToken);
        redirected.StatusCode.Should().Be(HttpStatusCode.TemporaryRedirect);
        redirected.Headers.Location.Should().Be(new Uri("https://cadence.example.org:8443/api/v1/studies"));

        var signedHeaders = TestSecurityContext.CreateHmacHeaders(TestSecurityContext.DefaultIdentity);
        var proxiedHttps = await service.Server.SendAsync(http =>
        {
            http.Connection.RemoteIpAddress = IPAddress.Parse("10.2.3.4");
            http.Request.Method = HttpMethods.Get;
            http.Request.Scheme = "http";
            http.Request.Host = new HostString("cadence.example.org");
            http.Request.Path = "/api/v1/studies";
            http.Request.Headers["X-Forwarded-Proto"] = "https";
            foreach (var (name, value) in signedHeaders)
            {
                http.Request.Headers[name] = value;
            }
        }, TestContext.Current.CancellationToken);

        proxiedHttps.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        proxiedHttps.Response.Headers.ContainsKey(HstsHeader).Should().BeTrue();
    }

    [Theory]
    [InlineData("Production", "false")]
    [InlineData("Development", "true")]
    public async Task HttpsRedirection_WhenDisabledOrInDevelopment_DoesNotRedirectPlainHttp(
        string environment,
        string redirectEnabled)
    {
        await using var service = CreateStatelessService(
            environment,
            (CadenceHttpHardening.HttpsRedirectEnabledKey, redirectEnabled));
        using var client = service.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://cadence.example.org"),
            AllowAutoRedirect = false,
        });
        foreach (var (name, value) in TestSecurityContext.CreateHmacHeaders(TestSecurityContext.DefaultIdentity))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
        }

        using var response = await client.GetAsync("/api/v1/studies", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(CadenceHttpHardening.HttpsRedirectEnabledKey, "yes")]
    [InlineData(CadenceHttpHardening.HttpsPortKey, "70000")]
    [InlineData(CadenceHttpHardening.OpenApiPublicOriginKey, "ftp://api.example.org")]
    [InlineData(CadenceHttpHardening.OpenApiPublicOriginKey, "https://api.example.org?bad=1")]
    public void InvalidHardeningConfiguration_FailsStartup(string key, string badValue)
    {
        Action start = () =>
        {
            using var service = CreateStatelessService("Production", (key, badValue));
            _ = service.Services;
        };

        start.Should().Throw<InvalidOperationException>().WithMessage($"*{key}*");
    }

    private static WebApplicationFactory<Cadence.Apps.Designer.Program> CreateStatelessService(
        string environment, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Cadence.Apps.Designer.Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment(environment).UseTestGatewaySecret();
            foreach (var (key, value) in settings)
            {
                host.UseSetting(key, value);
            }

            host.ConfigureTestServices(services =>
            {
                services.RemoveBackgroundHostedServices();
                services.AddHealthChecks()
                    .AddCheck("ready-probe", () => HealthCheckResult.Healthy(), tags: [CadenceTelemetryExtensions.ReadinessTag]);
            });
        });

    private static WebApplicationFactory<Cadence.Apps.Tickets.Program> CreateService(
        string environment, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Cadence.Apps.Tickets.Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment(environment).UseTestGatewaySecret().UseTestPostgresConnectionString();
            foreach (var (key, value) in settings)
            {
                host.UseSetting(key, value);
            }

            host.ConfigureTestServices(services => services.RemoveBackgroundHostedServices());
        });
}
