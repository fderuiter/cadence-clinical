using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cadence.Testing.Security;

/// <summary>
/// Shared gateway-identity helpers for tests: a fixed test secret, signed identity headers, an
/// <see cref="HttpClient"/> handler that signs every request the way the gateway does, and claims principals
/// shaped like the ones gateway-signature authentication produces.
/// </summary>
public static class TestSecurityContext
{
    /// <summary>Test-only gateway secret; long enough to pass the host's startup validation.</summary>
    public const string GatewaySecret = "cadence-test-gateway-secret-0123456789abcdef";

    /// <summary>The identity signed onto requests when a test does not name one.</summary>
    public static GatewayIdentity DefaultIdentity { get; } = new(
        UserId: "test-user",
        Roles: ["Data Manager", "CRA", "Site Investigator"],
        SiteIds: []);

    public static GatewayIdentity Identity(string userId, params string[] roles) => new(userId, roles, []);

    /// <summary>Signed gateway headers for <paramref name="identity"/>, issued now unless a time is given.</summary>
    public static IReadOnlyDictionary<string, string> CreateHmacHeaders(
        GatewayIdentity identity,
        DateTimeOffset? issuedAt = null,
        string secret = GatewaySecret) =>
        identity.ToSignedHeaders(issuedAt ?? DateTimeOffset.UtcNow, GatewayIdentity.EncodeSecret(secret));

    /// <summary>An authenticated principal carrying <c>sub</c> and role claims, as the gateway scheme emits.</summary>
    public static ClaimsPrincipal CreatePrincipal(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(CadenceClaimTypes.Subject, userId), new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, GatewayAuthenticationHandler.SchemeName, CadenceClaimTypes.Subject, ClaimTypes.Role));
    }

    /// <summary>
    /// Signs each outgoing request as <paramref name="identity"/>, carrying over the request's own
    /// <c>X-Change-Reason</c> and <c>X-Sig-Token</c> headers into the signature exactly as the gateway does.
    /// </summary>
    public sealed class GatewaySigningHandler(GatewayIdentity identity) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var signed = identity with
            {
                ChangeReason = FirstHeader(request, GatewayHeaders.ChangeReason) ?? identity.ChangeReason,
                SigToken = FirstHeader(request, GatewayHeaders.SigToken) ?? identity.SigToken,
            };

            foreach (var (name, value) in CreateHmacHeaders(signed))
            {
                request.Headers.Remove(name);
                request.Headers.TryAddWithoutValidation(name, value);
            }

            return base.SendAsync(request, cancellationToken);
        }

        private static string? FirstHeader(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> helpers for hosts that run gateway-signature authentication.
/// </summary>
public static class SignedClientExtensions
{
    /// <summary>
    /// Creates a client whose requests are signed as <paramref name="identity"/> (or
    /// <see cref="TestSecurityContext.DefaultIdentity"/>), the way the gateway forwards an authenticated caller.
    /// </summary>
    public static HttpClient CreateSignedClient<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        GatewayIdentity? identity = null)
        where TEntryPoint : class =>
        factory.CreateDefaultClient(new TestSecurityContext.GatewaySigningHandler(identity ?? TestSecurityContext.DefaultIdentity));

    /// <summary>Supplies the test gateway secret so the host's startup validation passes.</summary>
    public static IWebHostBuilder UseTestGatewaySecret(this IWebHostBuilder builder) =>
        builder.UseSetting("GATEWAY_SECRET", TestSecurityContext.GatewaySecret);
}
