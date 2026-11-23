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

    private string? _gatewaySecret;
    private byte[] _gatewaySecretBytes = [];

    /// <summary>The shared gateway secret. Bound from <c>GATEWAY_SECRET</c>; there is no default.</summary>
    public string? GatewaySecret
    {
        get => _gatewaySecret;
        set
        {
            _gatewaySecret = value;
            _gatewaySecretBytes = string.IsNullOrEmpty(value) ? [] : GatewayIdentity.EncodeSecret(value);
        }
    }

    /// <summary>Cached UTF-8 bytes of <see cref="GatewaySecret"/>, or empty when unset.</summary>
    public ReadOnlyMemory<byte> GatewaySecretBytes => _gatewaySecretBytes;

    /// <summary>How far a signed timestamp may drift from this service's clock before it is rejected.</summary>
    public TimeSpan AllowedClockSkew { get; set; } = TimeSpan.FromMinutes(2);

    internal byte[] GetGatewaySecretBytes() => _gatewaySecretBytes;
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
        string studyHeader = headers[GatewayHeaders.StudyIds].ToString();
        string? subjectCode = GatewayIdentity.NullIfBlank(headers[GatewayHeaders.SubjectCode].ToString());
        string sponsorId = headers[GatewayHeaders.SponsorId].ToString();
        string unblindedHeader = headers[GatewayHeaders.UnblindedAccess].ToString();
        bool unblindedAccess = string.Equals(unblindedHeader, "true", StringComparison.OrdinalIgnoreCase);
        // A blank tenant stays blank: the caller gets no tenant claim, never a fallback tenant (#5038).
        string? tenantId = GatewayIdentity.NullIfBlank(headers[GatewayHeaders.TenantId].ToString());

        string tenantReadOnlyHeader = headers[GatewayHeaders.TenantReadOnly].ToString();
        bool tenantReadOnly;
        if (string.Equals(tenantReadOnlyHeader, "false", StringComparison.OrdinalIgnoreCase))
        {
            tenantReadOnly = false;
        }
        else if (string.Equals(tenantReadOnlyHeader, "true", StringComparison.OrdinalIgnoreCase))
        {
            tenantReadOnly = true;
        }
        else if (string.IsNullOrEmpty(tenantReadOnlyHeader) && tenantId is null)
        {
            tenantReadOnly = false;
        }
        else
        {
            return Task.FromResult(AuthenticateResult.Fail(
                "Tenant-scoped signed requests must carry an explicit 'true' or 'false' X-Tenant-Read-Only header."));
        }

        string? rawProductFamiliesHeader = GatewayIdentity.NullIfBlank(headers[GatewayHeaders.TenantProductFamilies].ToString());
        IReadOnlyList<string> productFamilies = Array.Empty<string>();
        if (tenantId is not null)
        {
            if (rawProductFamiliesHeader is null
                || !GatewayIdentity.TryParseProductFamiliesHeader(rawProductFamiliesHeader, out productFamilies))
            {
                return Task.FromResult(AuthenticateResult.Fail(
                    "Tenant-scoped signed requests must carry a canonical X-Tenant-Product-Families header."));
            }
        }
        else if (rawProductFamiliesHeader is not null
            && !GatewayIdentity.TryParseProductFamiliesHeader(rawProductFamiliesHeader, out productFamilies))
        {
            return Task.FromResult(AuthenticateResult.Fail("X-Tenant-Product-Families contains unknown or non-canonical families."));
        }

        string? rawTenantScopingHeader = GatewayIdentity.NullIfBlank(headers[GatewayHeaders.TenantScoping].ToString());
        TenantScopingCaps? tenantScoping = null;
        if (rawTenantScopingHeader is not null
            && !TenantScopingCaps.TryParseHeader(rawTenantScopingHeader, out tenantScoping))
        {
            return Task.FromResult(AuthenticateResult.Fail("X-Tenant-Scoping is malformed."));
        }

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
            secret: Options.GetGatewaySecretBytes(),
            changeReason: GatewayIdentity.NullIfBlank(changeReason),
            siteId: GatewayIdentity.NullIfBlank(siteHeader),
            studyIds: GatewayIdentity.NullIfBlank(studyHeader),
            sponsorId: GatewayIdentity.NullIfBlank(sponsorId),
            unblindedAccess: unblindedAccess,
            tenantId: tenantId,
            sigToken: GatewayIdentity.NullIfBlank(sigToken),
            subjectCode: subjectCode,
            tenantReadOnly: tenantReadOnly,
            tenantProductFamilies: rawProductFamiliesHeader,
            tenantScoping: rawTenantScopingHeader);

        if (!verified)
        {
            return Task.FromResult(AuthenticateResult.Fail("Gateway signature does not match the asserted identity."));
        }

        var rawRoles = GatewayIdentity.SplitList(rolesHeader);
        var normalizedRoles = RoleNormalization.GetNormalizedRoles(rawRoles);
        var sites = GatewayIdentity.SplitList(siteHeader);
        var studies = GatewayIdentity.SplitList(studyHeader);

        Context.Items[PrincipalItemKey] = new Principal
        {
            UserId = userId,
            TenantId = tenantId,
            Roles = normalizedRoles,
            RawRoles = rawRoles,
            AssignedSites = sites,
            AssignedStudies = studies,
            UnblindedAccess = unblindedAccess,
            TenantReadOnly = tenantReadOnly,
            ProductFamilies = productFamilies,
            TenantScoping = tenantScoping,
            SponsorId = GatewayIdentity.NullIfBlank(sponsorId),
            SubjectCode = subjectCode,
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
        claims.AddRange(studies.Select(study => new Claim(CadenceClaimTypes.StudyIds, study)));
        claims.AddRange(productFamilies.Select(family => new Claim(CadenceClaimTypes.ProductFamily, family)));
        if (!string.IsNullOrWhiteSpace(sponsorId))
        {
            claims.Add(new Claim(CadenceClaimTypes.SponsorId, sponsorId));
        }

        if (subjectCode is not null)
        {
            claims.Add(new Claim(CadenceClaimTypes.SubjectCode, subjectCode));
        }

        if (unblindedAccess)
        {
            claims.Add(new Claim(CadenceClaimTypes.UnblindedAccess, "true"));
        }

        if (tenantReadOnly)
        {
            claims.Add(new Claim(CadenceClaimTypes.TenantReadOnly, "true"));
        }

        if (rawTenantScopingHeader is not null)
        {
            claims.Add(new Claim(CadenceClaimTypes.TenantScoping, rawTenantScopingHeader));
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

#pragma warning disable RS0030 // AuthenticationHandler owns its TimeProvider property for signed-header clock-skew checks.
        var drift = TimeProvider.GetUtcNow() - issuedAt;
#pragma warning restore RS0030
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
    public const string StudyIds = "study_ids";
    public const string SubjectCode = "subject_code";
    public const string SponsorId = "sponsor_id";
    public const string UnblindedAccess = "unblinded_access";
    public const string TenantReadOnly = "tenant_read_only";
    public const string ProductFamily = "product_family";
    public const string TenantScoping = "tenant_scoping";
}
