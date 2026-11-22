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
    public const string StudyIds = "X-Study-Ids";
    public const string SponsorId = "X-Sponsor-Id";
    public const string UnblindedAccess = "X-Unblinded-Access";
    public const string TenantId = "X-Tenant-Id";
    public const string TenantReadOnly = "X-Tenant-Read-Only";
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
        UserId, Roles, SiteId, StudyIds, SponsorId, UnblindedAccess, TenantId, TenantReadOnly, Timestamp, Signature,
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
    string? TenantId = null,
    string? ChangeReason = null,
    string? SigToken = null,
    IReadOnlyList<string>? StudyIds = null,
    bool TenantReadOnly = false)
{
    // ChangeReason is the decoded text the user typed, never its header encoding (see ChangeReasonHeader).
    // TenantId is the tenant code the sign-in token names, or null when it names none: there is no fallback tenant
    // (#5038). A kernel host refuses a caller without one.

    internal string RolesHeader => FormatList(Roles);

    internal string SiteIdsHeader => FormatList(SiteIds);

    // The studies the caller is assigned to, or "*" for every study (ADR-2516). Study scope fails closed without one.
    internal string StudyIdsHeader => FormatList(StudyIds);

    /// <summary>
    /// Produces the full set of signed identity headers for this identity at <paramref name="issuedAt"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToSignedHeaders(DateTimeOffset issuedAt, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        string timestamp = issuedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        string rolesHeader = RolesHeader;
        string? siteIdsHeader = NullIfBlank(SiteIdsHeader);
        string? studyIdsHeader = NullIfBlank(StudyIdsHeader);
        string? sponsorId = NullIfBlank(SponsorId);
        string? tenantId = NullIfBlank(TenantId);
        string? changeReason = NullIfBlank(ChangeReason);
        string? sigToken = NullIfBlank(SigToken);

        string signature = HmacSigner.GenerateGatewaySignature(
            userId: UserId,
            roles: rolesHeader,
            timestamp: timestamp,
            secret: secret,
            changeReason: changeReason,
            siteId: siteIdsHeader,
            studyIds: studyIdsHeader,
            sponsorId: sponsorId,
            unblindedAccess: UnblindedAccess,
            tenantId: tenantId,
            sigToken: sigToken,
            tenantReadOnly: TenantReadOnly);

        var headers = new Dictionary<string, string>(capacity: 12, StringComparer.OrdinalIgnoreCase)
        {
            [GatewayHeaders.UserId] = UserId,
            [GatewayHeaders.Roles] = rolesHeader,
            [GatewayHeaders.UnblindedAccess] = UnblindedAccess ? "true" : "false",
            [GatewayHeaders.TenantReadOnly] = TenantReadOnly ? "true" : "false",
            [GatewayHeaders.Timestamp] = timestamp,
            [GatewayHeaders.Signature] = signature,
        };

        AddIfPresent(headers, GatewayHeaders.TenantId, tenantId);
        AddIfPresent(headers, GatewayHeaders.SiteId, siteIdsHeader);
        AddIfPresent(headers, GatewayHeaders.StudyIds, studyIdsHeader);
        AddIfPresent(headers, GatewayHeaders.SponsorId, sponsorId);
        // The signature covers the reason as typed; the header carries it in its ASCII wire form (ADR-2495).
        if (changeReason is not null)
        {
            headers[GatewayHeaders.ChangeReason] = ChangeReasonHeader.Encode(changeReason);
        }

        AddIfPresent(headers, GatewayHeaders.SigToken, sigToken);

        return headers;
    }

    /// <summary>Encodes a configured shared secret the same way on both the signing and verifying side.</summary>
    public static byte[] EncodeSecret(string secret) => Encoding.UTF8.GetBytes(secret);

    internal static IReadOnlyList<string> SplitList(string? header) =>
        string.IsNullOrWhiteSpace(header)
            ? []
            : header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    internal static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FormatList(IReadOnlyList<string>? values) => values switch
    {
        null or { Count: 0 } => string.Empty,
        { Count: 1 } => values[0] ?? string.Empty,
        _ => string.Join(',', values),
    };

    private static void AddIfPresent(Dictionary<string, string> headers, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            headers[name] = value;
        }
    }
}
