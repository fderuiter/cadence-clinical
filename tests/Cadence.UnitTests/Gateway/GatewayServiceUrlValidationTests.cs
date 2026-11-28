using System;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;

using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Topology;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using Xunit;

namespace Cadence.UnitTests.Gateway;

/// <summary>
/// Checks that the gateway refuses to start when a service address is missing without explicit Development opt-in,
/// or is not an absolute http(s) URL or 'disabled' in any environment, and answers 503 Service Unavailable for disabled clusters.
/// </summary>
public sealed class GatewayServiceUrlValidationTests
{
    [Fact]
    [Trait("Category", "GxP_IQ")]
    [GxPRequirement("PRD-SYS-190")]
    public void HostStartup_OutsideDevelopment_WithoutServiceUrls_FailsFast()
    {
        using var gateway = CreateGateway(host => host.UseEnvironment("Production"));

        Action start = () => gateway.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("*_SERVICE_URL must be set*");
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    [GxPRequirement("PRD-SYS-190")]
    public void HostStartup_InDevelopment_WithoutAllowLocalhostFallbacks_FailsFast()
    {
        using var gateway = CreateGateway(host =>
        {
            host.UseEnvironment("Development");
            foreach (var svc in ServiceTopology.Services)
            {
                host.UseSetting(svc.UrlVariable, null);
            }
        });

        Action start = () => gateway.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{ServiceTopology.AllowLocalhostFallbacksKey}*");
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    [GxPRequirement("PRD-SYS-190")]
    public void HostStartup_InDevelopment_WithAllowLocalhostFallbacks_StartsCleanly()
    {
        using var gateway = CreateGateway(host => host
            .UseEnvironment("Development")
            .UseTestLocalhostFallbacks());

        using var client = gateway.CreateClient();
        client.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    [GxPRequirement("PRD-SYS-190")]
    public void HostStartup_WithARelativeServiceUrl_FailsFast()
    {
        using var gateway = CreateGateway(host => host
            .UseTestLocalhostFallbacks()
            .UseSetting("EXECUTION_SERVICE_URL", "execution:8080"));

        Action start = () => gateway.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("EXECUTION_SERVICE_URL must be an absolute http or https URL*");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-190")]
    public async Task DisabledServiceCluster_Returns503ServiceUnavailable_WithoutForwardingToLocalhost()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gateway = CreateGateway(host =>
        {
            host.UseEnvironment("Production");
            foreach (var svc in ServiceTopology.Services)
            {
                host.UseSetting(
                    svc.UrlVariable,
                    svc.Name == "imaging" ? ServiceTopology.DisabledServiceUrl : $"http://127.0.0.1:{svc.Port}");
            }

            host.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = GatewayAuthenticationTests.GatewayFixture.Issuer };
                    configuration.SigningKeys.Add(GatewayAuthenticationTests.GatewayFixture.SigningKey);
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.TokenValidationParameters.ValidateAudience = false;
                }));
        });

        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", GatewayAuthenticationTests.GatewayFixture.CreateToken("cra-01", ["CRA"]));

        using var response = await client.GetAsync("/api/v1/imaging/studies/test-uid", ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        string payload = await response.Content.ReadAsStringAsync(ct);
        payload.Should().ContainEquivalentOf("Service unavailable").And.Contain("disabled");
    }

    private static WebApplicationFactory<Cadence.Apps.Gateway.Program> CreateGateway(Action<IWebHostBuilder> configure) =>
        new WebApplicationFactory<Cadence.Apps.Gateway.Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("OIDC_AUTHORITY", GatewayAuthenticationTests.GatewayFixture.Issuer)
                .UseSetting("OIDC_AUDIENCE", "cadence-api")
                .UseTestGatewaySecret();
            configure(host);
        });
}
