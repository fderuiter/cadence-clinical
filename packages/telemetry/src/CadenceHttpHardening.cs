using System;
using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Cadence.Telemetry;

/// <summary>
/// HTTP hardening shared by every service: which proxies may set <c>X-Forwarded-For</c> and
/// <c>X-Forwarded-Proto</c>, and how long browsers are told to keep using HTTPS.
/// </summary>
public static class CadenceHttpHardening
{
    /// <summary>
    /// Comma-separated IP addresses or CIDR ranges of the proxies in front of the service (for example the
    /// front proxy and the gateway). Forwarded headers from any other sender are ignored. Loopback is always trusted.
    /// </summary>
    public const string TrustedProxiesKey = "CADENCE_TRUSTED_PROXIES";

    /// <summary>The <c>Strict-Transport-Security</c> max-age sent on HTTPS responses outside Development.</summary>
    public static readonly TimeSpan HstsMaxAge = TimeSpan.FromDays(365);

    /// <summary>
    /// Applies <paramref name="trustedProxies"/> (the <see cref="TrustedProxiesKey"/> value) to
    /// <paramref name="options"/>. Throws when an entry is neither an IP address nor a CIDR range, so a typo
    /// fails startup instead of silently distrusting the proxy.
    /// </summary>
    public static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, string? trustedProxies)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Walk back through every trusted hop (front proxy, then gateway) to the first untrusted address,
        // which is the real client, instead of stopping after one hop.
        options.ForwardLimit = null;

        if (string.IsNullOrWhiteSpace(trustedProxies))
        {
            return;
        }

        foreach (string entry in trustedProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (entry.Contains('/', StringComparison.Ordinal) && System.Net.IPNetwork.TryParse(entry, out var network))
            {
                options.KnownIPNetworks.Add(network);
            }
            else if (IPAddress.TryParse(entry, out var address))
            {
                options.KnownProxies.Add(address);
            }
            else
            {
                throw new InvalidOperationException(
                    $"{TrustedProxiesKey} entry '{entry}' is not an IP address or a CIDR range (for example 10.0.0.0/8).");
            }
        }
    }
}
