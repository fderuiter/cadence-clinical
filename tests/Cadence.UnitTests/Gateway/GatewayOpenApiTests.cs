using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Gateway;
using Cadence.Kernel.Time;
using Cadence.Telemetry;
using Cadence.Testing.Security;
using Cadence.Topology;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using Xunit;

using Yarp.ReverseProxy.Configuration;

namespace Cadence.UnitTests.Gateway;

/// <summary>
/// Verifies that the API gateway serves unified (<c>/openapi/cdisc.json</c>) and per-contract
/// (<c>/openapi/{contract}.json</c>) OpenAPI documents plus Scalar UI in <c>Development</c>, rewrites <c>servers</c>
/// from trusted forwarded headers, ignores untrusted spoofing, hides docs outside <c>Development</c>, and leaves
/// YARP <c>/api/v1/*</c> routes untouched.
/// </summary>
public sealed class GatewayOpenApiTests
{
    [Fact]
    public async Task DevelopmentGateway_ServesUnifiedPerContractAndHostOpenApiAndScalar_WithoutAlteringYarpRoutes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var gateway = GatewayResilienceTests.CreateGateway("http://127.0.0.1:1");
        using var client = gateway.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://gateway.cadence.local:8000"),
        });

        foreach (string openApiPath in new[]
                 {
                     "/openapi/cdisc.json",
                     "/openapi/kernel.json",
                     "/openapi/execution_forms.json",
                     "/openapi/tickets.json",
                     "/openapi/v1.json",
                 })
        {
            using var response = await client.GetAsync(openApiPath, ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"'{openApiPath}' must be served in Development");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var urls = doc.RootElement.GetProperty("servers").EnumerateArray().Select(s => s.GetProperty("url").GetString()).ToList();
            urls.Should().Equal("http://gateway.cadence.local:8000");
        }

        foreach (string scalarPath in new[] { "/scalar/cdisc", "/scalar/kernel", "/scalar/v1" })
        {
            using var scalarResponse = await client.GetAsync(scalarPath, ct);
            scalarResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"'{scalarPath}' must render Scalar in Development");
        }

        var yarpRoutes = gateway.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().Routes;
        yarpRoutes.Select(r => r.Match.Path)
            .Should().OnlyContain(p => p != null && p.StartsWith("/api/v1/", StringComparison.Ordinal) && p.EndsWith("/{**catch-all}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DevelopmentGateway_RewritesOpenApiServersFromTrustedProxyAndIgnoresUntrustedSpoofing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var gateway = GatewayResilienceTests.CreateGateway(
            "http://127.0.0.1:1",
            (CadenceHttpHardening.TrustedProxiesKey, "10.0.0.0/8"));

        foreach (string documentPath in new[] { "/openapi/cdisc.json", "/openapi/tickets.json", "/openapi/v1.json" })
        {
            var trusted = await gateway.Server.SendAsync(http =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse("10.10.0.5");
                http.Request.Method = HttpMethods.Get;
                http.Request.Scheme = "http";
                http.Request.Host = new HostString("gateway-internal:8080");
                http.Request.Path = documentPath;
                http.Request.Headers["X-Forwarded-Proto"] = "https";
                http.Request.Headers["X-Forwarded-Host"] = "api.example.org";
                http.Request.Headers["X-Forwarded-Prefix"] = "/edge";
            }, ct);

            trusted.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
            using var trustedDoc = await JsonDocument.ParseAsync(trusted.Response.Body, cancellationToken: ct);
            trustedDoc.RootElement.GetProperty("servers")[0].GetProperty("url").GetString()
                .Should().Be("https://api.example.org/edge");

            var untrusted = await gateway.Server.SendAsync(http =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.99");
                http.Request.Method = HttpMethods.Get;
                http.Request.Scheme = "http";
                http.Request.Host = new HostString("gateway-internal:8080");
                http.Request.Path = documentPath;
                http.Request.Headers["X-Forwarded-Proto"] = "https";
                http.Request.Headers["X-Forwarded-Host"] = "spoof.example.org";
                http.Request.Headers["X-Forwarded-Prefix"] = "/spoof";
            }, ct);

            untrusted.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
            using var untrustedDoc = await JsonDocument.ParseAsync(untrusted.Response.Body, cancellationToken: ct);
            untrustedDoc.RootElement.GetProperty("servers")[0].GetProperty("url").GetString()
                .Should().Be("http://gateway-internal:8080");
        }
    }

    [Fact]
    public async Task ProductionGateway_DoesNotExposeOpenApiOrScalarRoutes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var gateway = new WebApplicationFactory<Cadence.Apps.Gateway.Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production")
                .UseSetting("OIDC_AUTHORITY", GatewayAuthenticationTests.GatewayFixture.Issuer)
                .UseSetting("OIDC_AUDIENCE", "cadence-api")
                .UseTestGatewaySecret();

            foreach (CadenceService service in ServiceTopology.Services)
            {
                host.UseSetting(service.UrlVariable, $"http://127.0.0.1:{service.Port}");
            }

            host.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = GatewayAuthenticationTests.GatewayFixture.Issuer };
                    configuration.SigningKeys.Add(GatewayAuthenticationTests.GatewayFixture.SigningKey);
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                }));
        });

        using var anonClient = gateway.CreateClient();
        using var authClient = gateway.CreateClient();
        string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = GatewayAuthenticationTests.GatewayFixture.Issuer,
            Audience = "cadence-api",
            Subject = new ClaimsIdentity([new Claim("sub", "cra-01")]),
            Claims = new Dictionary<string, object>
            {
                ["realm_access"] = new Dictionary<string, object> { ["roles"] = new[] { "CRA" } },
            },
            Expires = SystemClock.Instance.UtcNow.UtcDateTime.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                GatewayAuthenticationTests.GatewayFixture.SigningKey,
                SecurityAlgorithms.HmacSha256),
        });
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        foreach (string path in new[] { "/openapi/cdisc.json", "/openapi/tickets.json", "/openapi/v1.json", "/scalar/cdisc" })
        {
            using var anonResponse = await anonClient.GetAsync(path, ct);
            anonResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            using var authResponse = await authClient.GetAsync(path, ct);
            authResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
