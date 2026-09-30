using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Security.Authentication;

/// <summary>
/// Options for the <see cref="GatewayAuthenticationHandler"/> scheme.
/// </summary>
public sealed class GatewayAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>HMAC-SHA256 key length floor: one full hash block of entropy.</summary>
    public const int MinimumSecretLength = 32;

    /// <summary>The shared gateway secret. Bound from <c>GATEWAY_SECRET</c>; there is no default.</summary>
    public string? GatewaySecret { get; set; }

    /// <summary>How far a signed timestamp may drift from this service's clock before it is rejected.</summary>
    public TimeSpan AllowedClockSkew { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Authenticates a request from the identity headers the gateway signs. A request with no signature is
/// anonymous (so the fallback authorization policy answers 401); a request with a signature that does not
/// verify, or whose timestamp is stale, fails authentication outright. Nothing is trusted from an unsigned
/// header.
/// @req:PRD-SYS-053
/// </summary>
public sealed class GatewayAuthenticationHandler(
    IOptionsMonitor<GatewayAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<GatewayAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "CadenceGateway";

    /// <summary>The <see cref="HttpContext.Items"/> key the resolved <see cref="Principal"/> is stored under.</summary>
    public const string PrincipalItemKey = "Principal";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers;
        string signature = headers[GatewayHeaders.Signature].ToString();
        if (string.IsNullOrWhiteSpace(signature))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string userId = headers[GatewayHeaders.UserId].ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Signed request carries no user identity."));
        }

        string timestamp = headers[GatewayHeaders.Timestamp].ToString();
        if (!IsFresh(timestamp))
        {
            return Task.FromResult(AuthenticateResult.Fail("Gateway signature timestamp is missing or outside the allowed clock skew."));
        }

        string rolesHeader = headers[GatewayHeaders.Roles].ToString();
        string siteHeader = headers[GatewayHeaders.SiteId].ToString();
        string sponsorId = headers[GatewayHeaders.SponsorId].ToString();
        string unblindedHeader = headers[GatewayHeaders.UnblindedAccess].ToString();
        bool unblindedAccess = string.Equals(unblindedHeader, "true", StringComparison.OrdinalIgnoreCase);
        // A blank tenant stays blank: the caller gets no tenant claim, never a fallback tenant (#5038).
        string? tenantId = GatewayIdentity.NullIfBlank(headers[GatewayHeaders.TenantId].ToString());
        if (!ChangeReasonHeader.TryDecode(headers[GatewayHeaders.ChangeReason].ToString(), out string? changeReason))
        {
            return Task.FromResult(AuthenticateResult.Fail("X-Change-Reason is not a valid RFC 8187 UTF-8 value."));
        }

        string sigToken = headers[GatewayHeaders.SigToken].ToString();

        bool verified = HmacSigner.VerifyGatewaySignature(
            userId: userId,
            roles: rolesHeader,
            timestamp: timestamp,
            signature: signature,
            secret: GatewayIdentity.EncodeSecret(Options.GatewaySecret ?? string.Empty),
            changeReason: GatewayIdentity.NullIfBlank(changeReason),
            siteId: GatewayIdentity.NullIfBlank(siteHeader),
            sponsorId: GatewayIdentity.NullIfBlank(sponsorId),
            unblindedAccess: unblindedAccess,
            tenantId: tenantId,
            sigToken: GatewayIdentity.NullIfBlank(sigToken));

        if (!verified)
        {
            return Task.FromResult(AuthenticateResult.Fail("Gateway signature does not match the asserted identity."));
        }

        var rawRoles = GatewayIdentity.SplitList(rolesHeader);
        var normalizedRoles = RoleNormalization.GetNormalizedRoles(rawRoles);
        var sites = GatewayIdentity.SplitList(siteHeader);

        Context.Items[PrincipalItemKey] = new Principal
        {
            UserId = userId,
            Roles = normalizedRoles,
            RawRoles = rawRoles,
            AssignedSites = sites,
            UnblindedAccess = unblindedAccess,
            SponsorId = GatewayIdentity.NullIfBlank(sponsorId),
            ChangeReason = GatewayIdentity.NullIfBlank(changeReason),
        };

        var claims = new List<Claim>
        {
            new(CadenceClaimTypes.Subject, userId),
            new(ClaimTypes.NameIdentifier, userId),
        };
        if (tenantId is not null)
        {
            claims.Add(new Claim(CadenceClaimTypes.TenantId, tenantId));
        }

        claims.AddRange(normalizedRoles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(sites.Select(site => new Claim(CadenceClaimTypes.SiteId, site)));
        if (!string.IsNullOrWhiteSpace(sponsorId))
        {
            claims.Add(new Claim(CadenceClaimTypes.SponsorId, sponsorId));
        }

        if (unblindedAccess)
        {
            claims.Add(new Claim(CadenceClaimTypes.UnblindedAccess, "true"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, CadenceClaimTypes.Subject, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private bool IsFresh(string timestamp)
    {
        if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var issuedAt))
        {
            return false;
        }

        var drift = TimeProvider.GetUtcNow() - issuedAt;
        return drift.Duration() <= Options.AllowedClockSkew;
    }
}

/// <summary>
/// Claim types the gateway scheme emits alongside the standard <see cref="ClaimTypes.Role"/> claims.
/// </summary>
public static class CadenceClaimTypes
{
    public const string Subject = "sub";
    public const string TenantId = "tenant_id";
    public const string SiteId = "site_id";
    public const string SponsorId = "sponsor_id";
    public const string UnblindedAccess = "unblinded_access";
}
