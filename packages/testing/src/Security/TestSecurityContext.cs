using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace Cadence.Testing.Security;

/// <summary>
/// Shared gateway-identity helpers for tests: a fixed test secret, signed identity headers, an
/// <see cref="HttpClient"/> handler that signs every request the way the gateway does, and claims principals
/// shaped like the ones gateway-signature authentication produces.
/// </summary>
public static class TestSecurityContext
{
    /// <summary>Test-only gateway secret; long enough to pass the host's startup validation.</summary>
    public const string GatewaySecret = "cadence-test-gateway-secret-0123456789abcdef"; // pragma: allowlist secret

    /// <summary>Test-only execution demographics key (32 zero bytes); a valid AES-256 key for startup validation.</summary>
    public const string DemographicsKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // pragma: allowlist secret

    /// <summary>Test-only execution randomisation key (base64 of 32 bytes, #5607), distinct from the demographics key.</summary>
    public const string RandomizationKeyBase64 = "cmFuZG9taXphdGlvbi10ZXN0LWtleS0wMDAwMDAwMDA="; // pragma: allowlist secret

    /// <summary>Test-only SCE lock-token signing key (base64 of 32 ASCII bytes) for the interop host (#5382).</summary>
    public const string SceSigningKeyBase64 = "Y2FkZW5jZS10ZXN0LXNjZS1zaWduaW5nLWtleS0wMDE="; // pragma: allowlist secret

    /// <summary>Test-only imaging certificate and manifest signing key (base64 of 32 ASCII bytes) (#5399).</summary>
    public const string ImagingDeidSigningKeyBase64 = "Y2FkZW5jZS10ZXN0LWltYWdpbmctZGVpZC1zaWduLTA="; // pragma: allowlist secret

    /// <summary>Test-only imaging UID pseudonymization key (base64 of 32 ASCII bytes) (#5399).</summary>
    public const string ImagingDeidPseudonymizationKeyBase64 = "Y2FkZW5jZS10ZXN0LWltYWdpbmctcHNldWRvbnltLTE="; // pragma: allowlist secret

    /// <summary>
    /// The tenant code test identities carry, as a sign-in token names one. There is no fallback tenant (#5038), so a
    /// test of a caller without a tenant says so with <c>identity with { TenantId = null }</c>.
    /// </summary>
    public const string TenantCode = "tenant-test";

    /// <summary>
    /// The study assignment test identities carry: every study (ADR-2516). Study scope fails closed, so a test of a caller
    /// outside a study says so with <c>identity with { StudyIds = ["OTHER"] }</c> or <c>[]</c>.
    /// </summary>
    public static IReadOnlyList<string> AllStudies { get; } = [StudyAccessEvaluator.AllStudies];

    /// <summary>The identity signed onto requests when a test does not name one.</summary>
    public static GatewayIdentity DefaultIdentity { get; } = new(
        UserId: "test-user",
        Roles: ["Data Manager", "CRA", "Site Investigator"],
        SiteIds: [],
        TenantId: TenantCode,
        StudyIds: AllStudies);

    /// <summary>
    /// An identity for <paramref name="userId"/> with <paramref name="roles"/> in <see cref="TenantCode"/>, assigned to
    /// every study.
    /// </summary>
    public static GatewayIdentity Identity(string userId, params string[] roles) =>
        new(userId, roles, [], TenantId: TenantCode, StudyIds: AllStudies);

    /// <summary>Signed gateway headers for <paramref name="identity"/>, issued now unless a time is given.</summary>
    public static IReadOnlyDictionary<string, string> CreateHmacHeaders(
        GatewayIdentity identity,
        DateTimeOffset? issuedAt = null,
        string secret = GatewaySecret) =>
        identity.ToSignedHeaders(issuedAt ?? SystemClock.Instance.UtcNow, GatewayIdentity.EncodeSecret(secret));

    /// <summary>
    /// A fresh e-signature step-up token for <paramref name="userId"/>, bound to <paramref name="action"/> and
    /// <paramref name="semanticAction"/> (and to <paramref name="batchId"/> when one is given), as the gateway issues
    /// after re-authentication.
    /// </summary>
    public static string CreateSigToken(
        string userId,
        string action,
        string semanticAction,
        IReadOnlyList<string>? roles = null,
        DateTimeOffset? issuedAt = null,
        string secret = GatewaySecret,
        string? batchId = null) =>
        SigTokenIssuer.Issue(
            new SigTokenGrant(userId, userId, roles ?? [], action, semanticAction, batchId),
            issuedAt ?? SystemClock.Instance.UtcNow,
            GatewayIdentity.EncodeSecret(secret));

    /// <summary>
    /// Gateway authentication options holding <paramref name="secret"/>, as the host binds them, for application
    /// services that key signature manifests with the gateway secret.
    /// </summary>
    public static IOptionsMonitor<GatewayAuthenticationOptions> GatewayOptions(string? secret = GatewaySecret) =>
        new FixedOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = secret });

    /// <summary>An authenticated principal carrying <c>sub</c> and role claims, as the gateway scheme emits.</summary>
    public static ClaimsPrincipal CreatePrincipal(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(CadenceClaimTypes.Subject, userId), new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, GatewayAuthenticationHandler.SchemeName, CadenceClaimTypes.Subject, ClaimTypes.Role));
    }

    /// <summary>
    /// An authenticated principal that also carries the signed study and site assignments (<c>study_ids</c>,
    /// <c>site_id</c>), as the gateway scheme emits them, for handlers that check scope from the claims (ADR-2516).
    /// </summary>
    public static ClaimsPrincipal CreateScopedPrincipal(
        string userId, IReadOnlyList<string> studyIds, IReadOnlyList<string> siteIds, params string[] roles)
    {
        var principal = CreatePrincipal(userId, roles);
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaims(studyIds.Select(study => new Claim(CadenceClaimTypes.StudyIds, study)));
        identity.AddClaims(siteIds.Select(site => new Claim(CadenceClaimTypes.SiteId, site)));
        return principal;
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
                ChangeReason = DecodedChangeReason(request) ?? identity.ChangeReason,
                SigToken = FirstHeader(request, GatewayHeaders.SigToken) ?? identity.SigToken,
            };

            foreach (var (name, value) in CreateHmacHeaders(signed))
            {
                request.Headers.Remove(name);
                request.Headers.TryAddWithoutValidation(name, value);
            }

            return base.SendAsync(request, cancellationToken);
        }

        // The gateway signs the decoded reason and re-encodes it on the forwarded header (ADR-2495).
        private static string? DecodedChangeReason(HttpRequestMessage request) =>
            ChangeReasonHeader.TryDecode(FirstHeader(request, GatewayHeaders.ChangeReason), out string? reason) ? reason : null;

        private static string? FirstHeader(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}

/// <summary>An options monitor that returns one fixed value for every name.</summary>
internal sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
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

    /// <summary>Supplies the test demographics encryption key so the execution host's startup validation passes.</summary>
    public static IWebHostBuilder UseTestDemographicsKey(this IWebHostBuilder builder) =>
        builder.UseSetting("DemographicsEncryption:KeyBase64", TestSecurityContext.DemographicsKeyBase64);

    /// <summary>Supplies the test randomisation key (<c>RANDOMIZATION_ALLOCATION_KEY</c>) so the execution host's startup validation passes.</summary>
    public static IWebHostBuilder UseTestRandomizationKey(this IWebHostBuilder builder) =>
        builder.UseSetting("RANDOMIZATION_ALLOCATION_KEY", TestSecurityContext.RandomizationKeyBase64);

    /// <summary>Supplies the test SCE signing key (<c>CADENCE_SCE_SIGNING_KEY</c>) so the interop host's startup validation passes.</summary>
    public static IWebHostBuilder UseTestSceSigningKey(this IWebHostBuilder builder) =>
        builder.UseSetting("CADENCE_SCE_SIGNING_KEY", TestSecurityContext.SceSigningKeyBase64);

    /// <summary>
    /// Supplies the test imaging de-identification keys (<c>CADENCE_IMAGING_DEID_SIGNING_KEY</c> and
    /// <c>CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY</c>) so the imaging host's startup validation passes.
    /// </summary>
    public static IWebHostBuilder UseTestImagingDeidentificationKeys(this IWebHostBuilder builder) =>
        builder
            .UseSetting("CADENCE_IMAGING_DEID_SIGNING_KEY", TestSecurityContext.ImagingDeidSigningKeyBase64)
            .UseSetting("CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY", TestSecurityContext.ImagingDeidPseudonymizationKeyBase64);
}
