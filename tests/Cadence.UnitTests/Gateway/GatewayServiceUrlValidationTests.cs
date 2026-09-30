using System;

using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Cadence.UnitTests.Gateway;

/// <summary>
/// Checks that the gateway refuses to start when a service address is missing outside Development, or is not
/// an absolute http(s) URL in any environment, instead of forwarding to a localhost default.
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
    public void HostStartup_WithARelativeServiceUrl_FailsFast()
    {
        using var gateway = CreateGateway(host => host.UseSetting("EXECUTION_SERVICE_URL", "execution:8080"));

        Action start = () => gateway.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("EXECUTION_SERVICE_URL must be an absolute http or https URL*");
    }

    private static WebApplicationFactory<Cadence.Apps.Gateway.Program> CreateGateway(Action<IWebHostBuilder> configure) =>
        new WebApplicationFactory<Cadence.Apps.Gateway.Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("OIDC_AUTHORITY", "https://keycloak.test/realms/cadence")
                .UseSetting("OIDC_AUDIENCE", "cadence-api")
                .UseTestGatewaySecret();
            configure(host);
        });
}
