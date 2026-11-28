using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Gateway.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using Xunit;

namespace Cadence.UnitTests.Gateway;

/// <summary>
/// The gateway's e-signature step-up endpoint: a signed-in user re-enters their password and gets a
/// single-use token for one action, which a signature-gated service then accepts.
/// </summary>
public sealed class SignatureTokenEndpointTests(SignatureTokenEndpointTests.Fixture fixture)
    : IClassFixture<SignatureTokenEndpointTests.Fixture>
{
    private const string Action = "/api/v1/execution/forms/F-9/approve";
    private const string SemanticAction = "execution.form.approve";

    /// <summary>
    /// Only a signed-in caller can ask for a signature token.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SignatureVerification_WithoutSignIn_Returns401()
    {
        using var client = fixture.Gateway.CreateClient();

        var response = await client.PostAsJsonAsync(SignatureTokenEndpoints.Path, Body("pi-password"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The right password yields an uncached token bound to the caller and the action, which verifies
    /// downstream exactly once. The password is checked for the caller's own username.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SignatureVerification_WithTheCallersPassword_IssuesAVerifiableSingleUseToken()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.ClientFor("pi-sub-1", "dr.pi", "Site Investigator");

        var response = await client.PostAsJsonAsync(SignatureTokenEndpoints.Path, Body("pi-password", username: "DR.PI"), ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var issued = await response.Content.ReadFromJsonAsync<SignatureTokenResponse>(ct);
        issued!.ExpiresIn.Should().Be(60);
        fixture.Credentials.Checked.Should().Contain(("dr.pi", "pi-password", "123456"));

        var verifier = new SigTokenVerifier();
        var binding = new SigTokenBinding(Action, SemanticAction);
        byte[] secret = GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret);
        var token = await verifier.VerifyAndConsumeAsync(issued.SigToken, "pi-sub-1", binding, secret, TestContext.Current.CancellationToken);
        token.Claims.First(c => c.Type == SigTokenClaims.Username).Value.Should().Be("dr.pi");
        token.Claims.Where(c => c.Type == SigTokenClaims.Roles).Select(c => c.Value).Should().Equal("Site Investigator");
        issued.SigToken.Should().NotContain("pi-password");
        await verifier.Awaiting(v => v.VerifyAndConsumeAsync(issued.SigToken, "pi-sub-1", binding, secret, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    /// <summary>
    /// A failed credential check never produces a token.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SignatureVerification_WithAWrongPassword_Returns401WithoutAToken()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.ClientFor("pi-sub-2", "dr.two", "Site Investigator");

        var response = await client.PostAsJsonAsync(SignatureTokenEndpoints.Path, Body("wrong"), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        string body = await response.Content.ReadAsStringAsync(ct);
        body.Should().Contain("INVALID_CREDENTIALS").And.NotContain("sig_token");
    }

    /// <summary>
    /// Auditors cannot obtain signature tokens, whatever password they enter.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SignatureVerification_ForAnAuditor_Returns403WithoutCheckingCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.ClientFor("aud-sub", "auditor.one", "Auditor");

        var response = await client.PostAsJsonAsync(SignatureTokenEndpoints.Path, Body("auditor-password"), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Credentials.Checked.Should().NotContain(c => c.Username == "auditor.one");
    }

    /// <summary>
    /// A signer cannot re-authenticate as someone else from their own session.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SignatureVerification_NamingAnotherUser_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.ClientFor("pi-sub-3", "dr.three", "Site Investigator");

        var response = await client.PostAsJsonAsync(SignatureTokenEndpoints.Path, Body("pi-password", username: "dr.pi"), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Credentials.Checked.Should().NotContain(c => c.Username == "dr.three");
    }

    /// <summary>
    /// Every token names the concrete API path and the namespaced meaning it signs.
    /// @req:SRS-SYS-005
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    [InlineData(null, SemanticAction)]
    [InlineData("/somewhere/else", SemanticAction)]
    [InlineData("/api/v1/execution/../admin", SemanticAction)]
    [InlineData(Action, null)]
    [InlineData(Action, "Approve Everything")]
    public async Task SignatureVerification_WithoutABoundAction_Returns400(string? action, string? semanticAction)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.ClientFor("pi-sub-4", "dr.four", "Site Investigator");

        var response = await client.PostAsJsonAsync(
            SignatureTokenEndpoints.Path,
            new { password = RecordingCredentials.Password, action, semantic_action = semanticAction },
            ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Password guessing through the step-up endpoint is capped per signer.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SignatureVerification_RepeatedAttempts_AreRateLimitedPerSigner()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.ClientFor("pi-sub-5", "dr.five", "Site Investigator");

        var statuses = new List<HttpStatusCode>();
        for (int attempt = 0; attempt <= SignatureTokenEndpoints.AttemptsPerMinute; attempt++)
        {
            using var response = await client.PostAsJsonAsync(SignatureTokenEndpoints.Path, Body("wrong"), ct);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(SignatureTokenEndpoints.AttemptsPerMinute).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        statuses[^1].Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Credentials are checked on the internal discovery host when one is configured.
    /// @req:SRS-SYS-005
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_IQ")]
    [GxPRequirement("SRS-SYS-005")]
    [InlineData(null, null, "https://id.example/realms/cadence", "https://id.example/realms/cadence/protocol/openid-connect/token")]
    [InlineData(null, "http://keycloak:8080/realms/cadence/.well-known/openid-configuration", "https://id.example/realms/cadence", "http://keycloak:8080/realms/cadence/protocol/openid-connect/token")]
    [InlineData("https://idp.internal/token", "http://keycloak:8080/realms/cadence/.well-known/openid-configuration", "https://id.example/realms/cadence", "https://idp.internal/token")]
    public void TokenEndpoint_IsDerivedFromTheOidcSettings(string? explicitEndpoint, string? metadataAddress, string authority, string expected)
    {
        SignatureCredentialOptions.ResolveTokenEndpoint(explicitEndpoint, metadataAddress, authority)
            .Should().Be(new Uri(expected));
    }

    /// <summary>
    /// The password goes only to the token endpoint, as a direct grant on the signature client; the session
    /// that grant opens is logged out straight away.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task KeycloakVerifier_UsesADirectGrantThenEndsThatSession()
    {
        var idp = new FakeKeycloak(HttpStatusCode.OK, $$"""{"access_token":"{{AccessToken("kc-sub")}}","refresh_token":"rt-1"}""");
        var verifier = Verifier(idp);

        var check = await verifier.VerifyAsync("dr.pi", "s3cret", "654321", TestContext.Current.CancellationToken);

        check.Should().Be(new CredentialCheck(CredentialCheckOutcome.Verified, "kc-sub"));
        idp.Requests.Should().HaveCount(2);
        idp.Requests[0].Path.Should().Be("/realms/cadence/protocol/openid-connect/token");
        idp.Requests[0].Form.Should().Contain(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "cadence-esignature",
            ["username"] = "dr.pi",
            ["password"] = "s3cret",
            ["totp"] = "654321",
        });
        idp.Requests[1].Path.Should().Be("/realms/cadence/protocol/openid-connect/logout");
        idp.Requests[1].Form.Should().Contain("refresh_token", "rt-1").And.NotContainKey("password");
    }

    /// <summary>
    /// A wrong password is a rejection; a misconfigured client or an unreachable provider is not the
    /// signer's fault and is reported as unavailable.
    /// @req:SRS-SYS-005
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_grant"}""", CredentialCheckOutcome.Rejected)]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""", CredentialCheckOutcome.Rejected)]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""", CredentialCheckOutcome.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", CredentialCheckOutcome.Unavailable)]
    public async Task KeycloakVerifier_MapsIdentityProviderAnswers(HttpStatusCode status, string body, CredentialCheckOutcome expected)
    {
        var check = await Verifier(new FakeKeycloak(status, body)).VerifyAsync("dr.pi", "pw", null, TestContext.Current.CancellationToken);

        check.Outcome.Should().Be(expected);
        check.Subject.Should().BeNull();
    }

    private static object Body(string password, string? username = null) =>
        new { username, password, totp = "123456", action = Action, semantic_action = SemanticAction };

    private static KeycloakCredentialVerifier Verifier(FakeKeycloak idp) =>
        new(
            new HttpClient(idp),
            Options.Create(new SignatureCredentialOptions { TokenEndpoint = new Uri("http://keycloak:8080/realms/cadence/protocol/openid-connect/token") }),
            NullLogger<KeycloakCredentialVerifier>.Instance);

    private static string AccessToken(string subject) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", subject)]),
            SigningCredentials = new SigningCredentials(Fixture.SigningKey, SecurityAlgorithms.HmacSha256),
        });

    private sealed class FakeKeycloak(HttpStatusCode tokenStatus, string tokenBody) : HttpMessageHandler
    {
        public List<(string Path, Dictionary<string, string> Form)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string content = await request.Content!.ReadAsStringAsync(cancellationToken);
            var form = content.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p[1]));
            Requests.Add((request.RequestUri!.AbsolutePath, form));
            return request.RequestUri.AbsolutePath.EndsWith("/logout", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(tokenStatus) { Content = new StringContent(tokenBody, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>Accepts "pi-password" for every user, answering with that user's subject.</summary>
    public sealed class RecordingCredentials : ICredentialVerifier
    {
        public const string Password = "pi-password"; // pragma: allowlist secret

        private readonly ConcurrentQueue<(string Username, string Password, string? Totp)> _checked = new();

        public ConcurrentDictionary<string, string> Subjects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<(string Username, string Password, string? Totp)> Checked => _checked.ToArray();

        public Task<CredentialCheck> VerifyAsync(string username, string password, string? oneTimeCode, CancellationToken cancellationToken)
        {
            _checked.Enqueue((username, password, oneTimeCode));
            return Task.FromResult(password == Password && Subjects.TryGetValue(username, out var subject)
                ? new CredentialCheck(CredentialCheckOutcome.Verified, subject)
                : CredentialCheck.Rejected);
        }
    }

    /// <summary>The gateway with a local OIDC signing key and recorded credential checks.</summary>
    public sealed class Fixture : IAsyncLifetime
    {
        public const string Issuer = "https://keycloak.test/realms/cadence";

        public static readonly SymmetricSecurityKey SigningKey =
            new(Encoding.UTF8.GetBytes("cadence-gateway-test-token-signing-key-0123456789")) { KeyId = "trusted" };

        public RecordingCredentials Credentials { get; } = new();

        public WebApplicationFactory<Cadence.Apps.Gateway.Program> Gateway { get; private set; } = null!;

        public HttpClient ClientFor(string subject, string username, params string[] roles)
        {
            Credentials.Subjects[username] = subject;
            var client = Gateway.CreateClient();
            string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("preferred_username", username)]),
                Claims = new Dictionary<string, object> { ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles } },
                Expires = SystemClock.Instance.UtcNow.UtcDateTime.AddMinutes(5),
                SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        public ValueTask InitializeAsync()
        {
            Gateway = new WebApplicationFactory<Cadence.Apps.Gateway.Program>().WithWebHostBuilder(host => host
                .UseSetting("OIDC_AUTHORITY", Issuer)
                .UseTestLocalhostFallbacks()
                .UseTestGatewaySecret()
                .ConfigureTestServices(services =>
                {
                    services.AddSingleton<ICredentialVerifier>(Credentials);
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                        configuration.SigningKeys.Add(SigningKey);
                        options.Configuration = configuration;
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    });
                }));
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync() => await Gateway.DisposeAsync();
    }
}
