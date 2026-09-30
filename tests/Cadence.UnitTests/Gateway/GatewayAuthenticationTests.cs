using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
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
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using Xunit;

namespace Cadence.UnitTests.Gateway;

/// <summary>
/// Runs the real gateway in front of a header-echoing downstream service and checks that it only forwards
/// callers holding a valid OIDC token, replaces any identity headers the client sent with ones derived from
/// that token, and signs them so downstream services can verify them.
/// </summary>
public sealed class GatewayAuthenticationTests(GatewayAuthenticationTests.GatewayFixture fixture)
    : IClassFixture<GatewayAuthenticationTests.GatewayFixture>
{
    private const string ProxiedPath = "/api/v1/queries/q-1";

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task Healthz_WithoutToken_IsAnonymous()
    {
        using var client = fixture.Gateway.CreateClient();

        var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ProxiedRoute_WithoutToken_Returns401AndIsNotForwarded()
    {
        using var client = fixture.Gateway.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxiedPath);
        request.Headers.Add(GatewayHeaders.UserId, "attacker");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ProxiedRoute_WithTokenFromUntrustedKey_Returns401()
    {
        using var client = fixture.Gateway.CreateClient();
        var untrusted = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("an-entirely-different-signing-key-0123456789")) { KeyId = "trusted" };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GatewayFixture.CreateToken("cra-01", ["CRA"], untrusted));

        var response = await client.GetAsync(ProxiedPath, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ProxiedRoute_WithValidToken_ForwardsSignedIdentityFromTokenOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.Gateway.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxiedPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GatewayFixture.CreateToken("cra-01", ["CRA", "Monitor"], tenant: "tenant-a"));
        request.Headers.Add(GatewayHeaders.UserId, "someone-else");
        request.Headers.Add(GatewayHeaders.TenantId, "tenant-b");
        request.Headers.Add(GatewayHeaders.Roles, "Site Investigator");
        request.Headers.Add("X-User-Role", "admin");
        request.Headers.Add(GatewayHeaders.Signature, "deadbeef");
        request.Headers.Add(GatewayHeaders.ChangeReason, "Transcription error");

        var response = await client.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, response.Headers.WwwAuthenticate.ToString());
        var echoed = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(ct);
        var headers = new Dictionary<string, string>(echoed!, StringComparer.OrdinalIgnoreCase);

        headers[GatewayHeaders.UserId].Should().Be("cra-01");
        headers[GatewayHeaders.Roles].Should().Be("CRA,Monitor");
        headers[GatewayHeaders.TenantId].Should().Be("tenant-a");
        headers[GatewayHeaders.ChangeReason].Should().Be("Transcription error");
        headers.Should().NotContainKey("X-User-Role");
        headers.Should().NotContainKey("Authorization");

        HmacSigner.VerifyGatewaySignature(
            userId: headers[GatewayHeaders.UserId],
            roles: headers[GatewayHeaders.Roles],
            timestamp: headers[GatewayHeaders.Timestamp],
            signature: headers[GatewayHeaders.Signature],
            secret: GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret),
            changeReason: headers[GatewayHeaders.ChangeReason],
            tenantId: headers[GatewayHeaders.TenantId]).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ProxiedRoute_WithEncodedNonLatinReason_ForwardsItEncodedAndSignsTheTypedText()
    {
        // #4895, ADR-2495: the browser sends RFC 8187 (lowercase hex here), the gateway signs what was typed
        // and forwards the canonical ASCII form the downstream decodes back to the same text.
        const string typed = "IRB approval — 治験";
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.Gateway.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxiedPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GatewayFixture.CreateToken("cra-01", ["CRA"]));
        request.Headers.Add(GatewayHeaders.ChangeReason, "utf-8''IRB%20approval%20%e2%80%94%20%e6%b2%bb%e9%a8%93");

        var response = await client.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var echoed = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(ct);
        var headers = new Dictionary<string, string>(echoed!, StringComparer.OrdinalIgnoreCase);
        headers[GatewayHeaders.ChangeReason].Should().Be(ChangeReasonHeader.Encode(typed));

        HmacSigner.VerifyGatewaySignature(
            userId: headers[GatewayHeaders.UserId],
            roles: headers[GatewayHeaders.Roles],
            timestamp: headers[GatewayHeaders.Timestamp],
            signature: headers[GatewayHeaders.Signature],
            secret: GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret),
            changeReason: typed).Should().BeTrue();
    }

    /// <summary>
    /// A token that names no tenant forwards no tenant, and a tenant header the caller adds is dropped: there is no
    /// fallback tenant, so a kernel host refuses the caller (#5038).
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ProxiedRoute_WithATokenNamingNoTenant_ForwardsNoTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.Gateway.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxiedPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GatewayFixture.CreateToken("cra-01", ["CRA"]));
        request.Headers.Add(GatewayHeaders.TenantId, "tenant-b");

        var response = await client.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var echoed = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(ct);
        var headers = new Dictionary<string, string>(echoed!, StringComparer.OrdinalIgnoreCase);
        headers.Should().NotContainKey(GatewayHeaders.TenantId);
        HmacSigner.VerifyGatewaySignature(
            userId: headers[GatewayHeaders.UserId],
            roles: headers[GatewayHeaders.Roles],
            timestamp: headers[GatewayHeaders.Timestamp],
            signature: headers[GatewayHeaders.Signature],
            secret: GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret)).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ProxiedRoute_WithMalformedEncodedReason_Returns400AndIsNotForwarded()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.Gateway.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxiedPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GatewayFixture.CreateToken("cra-01", ["CRA"]));
        request.Headers.Add(GatewayHeaders.ChangeReason, "UTF-8''bad%ZZ");

        var response = await client.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("RFC 8187");
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public void HostStartup_WithoutOidcAuthority_FailsFast()
    {
        using var unconfigured = new WebApplicationFactory<Cadence.Apps.Gateway.Program>()
            .WithWebHostBuilder(builder => builder
                .UseSetting("OIDC_AUTHORITY", string.Empty)
                .UseTestGatewaySecret());

        Action start = () => unconfigured.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("*OIDC_AUTHORITY*");
    }

    /// <summary>
    /// The gateway under test plus a real Kestrel downstream that answers every request with the headers it
    /// received, standing in for the execution service.
    /// </summary>
    public sealed class GatewayFixture : IAsyncLifetime
    {
        public const string Issuer = "https://keycloak.test/realms/cadence";

        public static readonly SymmetricSecurityKey SigningKey =
            new(Encoding.UTF8.GetBytes("cadence-gateway-test-token-signing-key-0123456789")) { KeyId = "trusted" };

        private WebApplication? _downstream;

        public WebApplicationFactory<Cadence.Apps.Gateway.Program> Gateway { get; private set; } = null!;

        public static string CreateToken(string subject, IReadOnlyList<string> roles, SecurityKey? key = null, string? tenant = null)
        {
            var claims = new Dictionary<string, object> { ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles } };
            if (tenant is not null)
            {
                claims["tenant_id"] = tenant;
            }

            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Subject = new ClaimsIdentity([new Claim("sub", subject)]),
                Claims = claims,
                Expires = SystemClock.Instance.UtcNow.UtcDateTime.AddMinutes(5),
                SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.HmacSha256),
            });
        }

        public async ValueTask InitializeAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            _downstream = builder.Build();
            _downstream.Map("/{**path}", (HttpContext context) =>
                TypedResults.Ok(context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())));
            await _downstream.StartAsync();
            string downstreamUrl = _downstream.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();

            Gateway = new WebApplicationFactory<Cadence.Apps.Gateway.Program>().WithWebHostBuilder(host => host
                .UseSetting("OIDC_AUTHORITY", Issuer)
                .UseSetting("EXECUTION_SERVICE_URL", downstreamUrl)
                .UseTestGatewaySecret()
                .ConfigureTestServices(services =>
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        // Validate against a local key instead of fetching Keycloak's discovery document.
                        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                        configuration.SigningKeys.Add(SigningKey);
                        options.Configuration = configuration;
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    })));
        }

        public async ValueTask DisposeAsync()
        {
            await Gateway.DisposeAsync();
            if (_downstream is not null)
            {
                await _downstream.DisposeAsync();
            }
        }
    }
}
