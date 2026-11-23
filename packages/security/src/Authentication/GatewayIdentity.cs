using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Cadence.Kernel.Tenancy;

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
    public const string TenantProductFamilies = "X-Tenant-Product-Families";
    public const string TenantScoping = "X-Tenant-Scoping";
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
        UserId, Roles, SiteId, StudyIds, SponsorId, UnblindedAccess, TenantId, TenantReadOnly,
        TenantProductFamilies, TenantScoping, Timestamp, Signature,
        "X-User-Role", "X-User-Site", "X-Reason-For-Change",
    ];
}

/// <summary>
/// Contracted study-scoping caps carried on <see cref="GatewayHeaders.TenantScoping"/> when a workspace subscription
/// has been activated against a signed commercial quote.
/// </summary>
public sealed record TenantScopingCaps(
    string StudyPhase,
    int StudyDurationMonths,
    IReadOnlyList<string> Countries,
    int SiteCount,
    int ParticipantCount,
    int ActiveStudyCount)
{
    /// <summary>Converts a kernel <see cref="SubscriptionScoping"/> value into wire-transportable caps.</summary>
    public static TenantScopingCaps FromSubscriptionScoping(SubscriptionScoping scoping)
    {
        ArgumentNullException.ThrowIfNull(scoping);
        return new TenantScopingCaps(
            scoping.StudyPhase,
            scoping.StudyDurationMonths,
            scoping.Countries,
            scoping.SiteCount,
            scoping.ParticipantCount,
            scoping.ActiveStudyCount);
    }

    /// <summary>Formats these caps into the deterministic canonical wire representation covered by the HMAC signature.</summary>
    public string ToHeaderValue()
    {
        if (string.IsNullOrWhiteSpace(StudyPhase))
        {
            throw new ArgumentException("StudyPhase must be non-empty.", nameof(StudyPhase));
        }

        if (StudyDurationMonths < 1 || SiteCount < 1 || ParticipantCount < 1 || ActiveStudyCount < 1)
        {
            throw new ArgumentException("Quantitative scoping caps must all be positive integers.");
        }

        if (Countries is null || Countries.Count == 0)
        {
            throw new ArgumentException("At least one participating country is required.", nameof(Countries));
        }

        var normalizedCountries = Countries
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Select(static c => c.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static c => c, StringComparer.Ordinal)
            .ToArray();

        if (normalizedCountries.Length == 0 || normalizedCountries.Any(static c => c.Length is < 2 or > 64))
        {
            throw new ArgumentException("Countries must contain entries between 2 and 64 characters.", nameof(Countries));
        }

        string escapedCountries = string.Join(',', normalizedCountries.Select(Uri.EscapeDataString));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"v1;phase={Uri.EscapeDataString(StudyPhase.Trim())};months={StudyDurationMonths};sites={SiteCount};participants={ParticipantCount};studies={ActiveStudyCount};countries={escapedCountries}");
    }

    /// <summary>Parses and canonically validates a <see cref="GatewayHeaders.TenantScoping"/> header value.</summary>
    public static bool TryParseHeader(string? raw, out TenantScopingCaps? scoping)
    {
        scoping = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string[] segments = raw.Split(';');
        if (segments.Length != 7 || !string.Equals(segments[0], "v1", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadKeyValue(segments[1], "phase", out string? rawPhase)
            || !TryReadKeyValue(segments[2], "months", out string? rawMonths)
            || !TryReadKeyValue(segments[3], "sites", out string? rawSites)
            || !TryReadKeyValue(segments[4], "participants", out string? rawParticipants)
            || !TryReadKeyValue(segments[5], "studies", out string? rawStudies)
            || !TryReadKeyValue(segments[6], "countries", out string? rawCountries))
        {
            return false;
        }

        string phase;
        try
        {
            phase = Uri.UnescapeDataString(rawPhase!);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(phase)
            || !int.TryParse(rawMonths, NumberStyles.None, CultureInfo.InvariantCulture, out int months) || months < 1
            || !int.TryParse(rawSites, NumberStyles.None, CultureInfo.InvariantCulture, out int sites) || sites < 1
            || !int.TryParse(rawParticipants, NumberStyles.None, CultureInfo.InvariantCulture, out int participants) || participants < 1
            || !int.TryParse(rawStudies, NumberStyles.None, CultureInfo.InvariantCulture, out int studies) || studies < 1
            || string.IsNullOrWhiteSpace(rawCountries))
        {
            return false;
        }

        string[] rawCountryParts = rawCountries.Split(',');
        if (rawCountryParts.Length == 0)
        {
            return false;
        }

        var countries = new string[rawCountryParts.Length];
        for (int i = 0; i < rawCountryParts.Length; i++)
        {
            string code;
            try
            {
                code = Uri.UnescapeDataString(rawCountryParts[i]);
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(code)
                || code.Length is < 2 or > 64
                || !string.Equals(code, code.Trim().ToUpperInvariant(), StringComparison.Ordinal))
            {
                return false;
            }

            if (i > 0 && StringComparer.Ordinal.Compare(countries[i - 1], code) >= 0)
            {
                return false;
            }

            countries[i] = code;
        }

        var candidate = new TenantScopingCaps(phase, months, countries, sites, participants, studies);
        if (!string.Equals(candidate.ToHeaderValue(), raw, StringComparison.Ordinal))
        {
            return false;
        }

        scoping = candidate;
        return true;
    }

    private static bool TryReadKeyValue(string segment, string expectedKey, out string? value)
    {
        value = null;
        int eq = segment.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0 || eq == segment.Length - 1)
        {
            return false;
        }

        if (!segment.AsSpan(0, eq).SequenceEqual(expectedKey.AsSpan()))
        {
            return false;
        }

        value = segment[(eq + 1)..];
        return true;
    }
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
    bool TenantReadOnly = false,
    IReadOnlyList<string>? ProductFamilies = null,
    TenantScopingCaps? TenantScoping = null)
{
    /// <summary>Sentinel emitted on <see cref="GatewayHeaders.TenantProductFamilies"/> when a tenant holds zero product families.</summary>
    public const string NoProductFamiliesSentinel = "none";

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
        string? tenantProductFamilies = CanonicalizeProductFamilies(ProductFamilies, emitNoneWhenEmpty: tenantId is not null);
        string? tenantScoping = TenantScoping?.ToHeaderValue();

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
            tenantReadOnly: TenantReadOnly,
            tenantProductFamilies: tenantProductFamilies,
            tenantScoping: tenantScoping);

        var headers = new Dictionary<string, string>(capacity: 14, StringComparer.OrdinalIgnoreCase)
        {
            [GatewayHeaders.UserId] = UserId,
            [GatewayHeaders.Roles] = rolesHeader,
            [GatewayHeaders.UnblindedAccess] = UnblindedAccess ? "true" : "false",
            [GatewayHeaders.TenantReadOnly] = TenantReadOnly ? "true" : "false",
            [GatewayHeaders.Timestamp] = timestamp,
            [GatewayHeaders.Signature] = signature,
        };

        AddIfPresent(headers, GatewayHeaders.TenantId, tenantId);
        AddIfPresent(headers, GatewayHeaders.TenantProductFamilies, tenantProductFamilies);
        AddIfPresent(headers, GatewayHeaders.TenantScoping, tenantScoping);
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

    /// <summary>
    /// Validates and canonicalizes a list of product-family names into enum ordinal order, or <c>"none"</c> when
    /// <paramref name="emitNoneWhenEmpty"/> is true and the list is empty.
    /// </summary>
    public static string? CanonicalizeProductFamilies(IReadOnlyList<string>? families, bool emitNoneWhenEmpty)
    {
        if (families is null || families.Count == 0)
        {
            return emitNoneWhenEmpty ? NoProductFamiliesSentinel : null;
        }

        if (families.Count == 1 && string.Equals(families[0]?.Trim(), NoProductFamiliesSentinel, StringComparison.OrdinalIgnoreCase))
        {
            return emitNoneWhenEmpty ? NoProductFamiliesSentinel : null;
        }

        var parsed = new SortedSet<ProductFamily>();
        foreach (string? raw in families)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string token = raw.Trim();
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                || !Enum.TryParse<ProductFamily>(token, ignoreCase: true, out var family)
                || !Enum.IsDefined(family))
            {
                throw new ArgumentException($"Unknown Cadence ProductFamily '{token}'.", nameof(families));
            }

            parsed.Add(family);
        }

        if (parsed.Count == 0)
        {
            return emitNoneWhenEmpty ? NoProductFamiliesSentinel : null;
        }

        return string.Join(',', parsed.Select(static f => f.ToString()));
    }

    /// <summary>
    /// Parses a wire <see cref="GatewayHeaders.TenantProductFamilies"/> value, enforcing canonical ordering and valid
    /// <see cref="ProductFamily"/> tokens with no duplicates.
    /// </summary>
    public static bool TryParseProductFamiliesHeader(string? raw, out IReadOnlyList<string> families)
    {
        families = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (string.Equals(raw, NoProductFamiliesSentinel, StringComparison.Ordinal))
        {
            return true;
        }

        string[] parts = raw.Split(',');
        if (parts.Length == 0)
        {
            return false;
        }

        var resolved = new List<string>(parts.Length);
        int previousOrdinal = -1;
        foreach (string part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)
                || int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                || !Enum.TryParse<ProductFamily>(part, ignoreCase: false, out var family)
                || !Enum.IsDefined(family))
            {
                return false;
            }

            int currentOrdinal = (int)family;
            if (currentOrdinal <= previousOrdinal)
            {
                return false;
            }

            previousOrdinal = currentOrdinal;
            resolved.Add(family.ToString());
        }

        families = resolved;
        return true;
    }

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
