using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cadence.Security.Authentication;

/// <summary>
/// Header names that carry the gateway-asserted caller identity to downstream services. The gateway strips
/// every one of these from the inbound browser request and re-issues them from the validated OIDC token, so a
/// client can never supply them directly.
/// @req:PRD-SYS-053
/// </summary>
public static class GatewayHeaders
{
    public const string UserId = "X-User-Id";
    public const string Roles = "X-User-Roles";
    public const string SiteId = "X-Site-Id";
    public const string SponsorId = "X-Sponsor-Id";
    public const string UnblindedAccess = "X-Unblinded-Access";
    public const string TenantId = "X-Tenant-Id";
    public const string ChangeReason = "X-Change-Reason";
    public const string SigToken = "X-Sig-Token";
    public const string Timestamp = "X-Timestamp";
    public const string Signature = "X-Gateway-Signature";

    /// <summary>
    /// Identity headers only the gateway may set. <see cref="ChangeReason"/> and <see cref="SigToken"/> are
    /// not in this list: they are user input the gateway forwards, but they are still covered by the signature.
    /// </summary>
    public static IReadOnlyList<string> GatewayAsserted { get; } =
    [
        UserId, Roles, SiteId, SponsorId, UnblindedAccess, TenantId, Timestamp, Signature,
        "X-User-Role", "X-User-Site", "X-Reason-For-Change",
    ];
}

/// <summary>
/// The caller identity the gateway asserts on every proxied request.
/// @req:PRD-SYS-053
/// </summary>
public sealed record GatewayIdentity(
    string UserId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> SiteIds,
    string? SponsorId = null,
    bool UnblindedAccess = false,
    string TenantId = GatewayIdentity.DefaultTenantId,
    string? ChangeReason = null,
    string? SigToken = null)
{
    public const string DefaultTenantId = "tenant_default";

    internal string RolesHeader => string.Join(',', Roles);

    internal string SiteIdsHeader => string.Join(',', SiteIds);

    /// <summary>
    /// Produces the full set of signed identity headers for this identity at <paramref name="issuedAt"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToSignedHeaders(DateTimeOffset issuedAt, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        string timestamp = issuedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        string signature = HmacSigner.GenerateGatewaySignature(
            userId: UserId,
            roles: RolesHeader,
            timestamp: timestamp,
            secret: secret,
            changeReason: NullIfBlank(ChangeReason),
            siteId: NullIfBlank(SiteIdsHeader),
            sponsorId: NullIfBlank(SponsorId),
            unblindedAccess: UnblindedAccess,
            tenantId: TenantId,
            sigToken: NullIfBlank(SigToken));

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GatewayHeaders.UserId] = UserId,
            [GatewayHeaders.Roles] = RolesHeader,
            [GatewayHeaders.TenantId] = TenantId,
            [GatewayHeaders.UnblindedAccess] = UnblindedAccess ? "true" : "false",
            [GatewayHeaders.Timestamp] = timestamp,
            [GatewayHeaders.Signature] = signature,
        };

        AddIfPresent(headers, GatewayHeaders.SiteId, SiteIdsHeader);
        AddIfPresent(headers, GatewayHeaders.SponsorId, SponsorId);
        AddIfPresent(headers, GatewayHeaders.ChangeReason, ChangeReason);
        AddIfPresent(headers, GatewayHeaders.SigToken, SigToken);

        return headers;
    }

    /// <summary>Encodes a configured shared secret the same way on both the signing and verifying side.</summary>
    public static byte[] EncodeSecret(string secret) => Encoding.UTF8.GetBytes(secret);

    internal static IReadOnlyList<string> SplitList(string? header) =>
        string.IsNullOrWhiteSpace(header)
            ? []
            : header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    internal static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void AddIfPresent(Dictionary<string, string> headers, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            headers[name] = value;
        }
    }
}
