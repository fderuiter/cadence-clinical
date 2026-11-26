using System;
using System.Net;
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
        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
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
