using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using Xunit;

namespace Cadence.UnitTests.Gateway;

/// <summary>
/// The gateway accepts only tokens issued for the API audience the realm gives the web client's tokens, so a token a
/// caller obtains from another realm client (such as the password-grant e-signature client) is refused (#5037).
/// </summary>
public sealed class GatewayAudienceTests
{
    private const string Audience = "cadence-api";
    private const string ProxiedPath = "/api/v1/execution/subjects/s-1";

    /// <summary>Outside Development the gateway refuses to start without an audience, naming the setting.</summary>
    [Fact]
    [Trait("Category", "GxP_IQ")]
    [GxPRequirement("PRD-SYS-190")]
    public void HostStartup_OutsideDevelopment_WithoutOidcAudience_FailsFast()
    {
        using var gateway = new WebApplicationFactory<Cadence.Apps.Gateway.Program>().WithWebHostBuilder(host => host
            .UseEnvironment("Production")
            .UseSetting("OIDC_AUTHORITY", GatewayAuthenticationTests.GatewayFixture.Issuer)
            .UseTestGatewaySecret());

        Action start = () => gateway.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("OIDC_AUDIENCE must be set outside Development*");
    }

    /// <summary>A token without the API audience, as the e-signature client issues, is refused with 401.</summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [InlineData(null)]
    [InlineData("account")]
    [InlineData("cadence-esignature")]
    public async Task ProxiedRoute_WithATokenForAnotherAudience_Returns401(string? audience)
    {
        using var gateway = Gateway();
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(audience));

        var response = await client.GetAsync(new Uri(ProxiedPath, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A token with the API audience is let through to the proxy.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ProxiedRoute_WithTheApiAudience_IsAuthenticated()
    {
        using var gateway = Gateway();
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(Audience));

        var response = await client.GetAsync(new Uri(ProxiedPath, UriKind.Relative), TestContext.Current.CancellationToken);

        // Nothing listens downstream, so the proxy answers 502; what matters is that authentication passed.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
    }

    private static WebApplicationFactory<Cadence.Apps.Gateway.Program> Gateway() =>
        new WebApplicationFactory<Cadence.Apps.Gateway.Program>().WithWebHostBuilder(host => host
            .UseSetting("OIDC_AUTHORITY", GatewayAuthenticationTests.GatewayFixture.Issuer)
            .UseSetting("OIDC_AUDIENCE", Audience)
            .UseSetting("EXECUTION_SERVICE_URL", "http://127.0.0.1:9")
            .UseTestLocalhostFallbacks()
            .UseTestGatewaySecret()
            .ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = GatewayAuthenticationTests.GatewayFixture.Issuer };
                    configuration.SigningKeys.Add(GatewayAuthenticationTests.GatewayFixture.SigningKey);
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                })));

    private static string Token(string? audience) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = GatewayAuthenticationTests.GatewayFixture.Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", "pi-01")]),
            Claims = new Dictionary<string, object> { ["realm_access"] = new Dictionary<string, object> { ["roles"] = new[] { "PI" } } },
            Expires = SystemClock.Instance.UtcNow.UtcDateTime.AddMinutes(5),
            SigningCredentials = new SigningCredentials(GatewayAuthenticationTests.GatewayFixture.SigningKey, SecurityAlgorithms.HmacSha256),
        });
}
