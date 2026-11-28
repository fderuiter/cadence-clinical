using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;

namespace Cadence.Telemetry;

/// <summary>
/// HTTP hardening shared by every service: which proxies may set <c>X-Forwarded-*</c> headers, which forwarded
/// hostnames are accepted, how OpenAPI <c>servers</c> entries reflect active proxy origins, and opt-in HTTPS
/// redirection that preserves plain-HTTP container probes.
/// </summary>
public static class CadenceHttpHardening
{
    /// <summary>
    /// Comma-separated IP addresses or CIDR ranges of the proxies in front of the service (for example the
    /// front proxy and the gateway). Forwarded headers from any other sender are ignored. Loopback is always trusted.
    /// </summary>
    public const string TrustedProxiesKey = "CADENCE_TRUSTED_PROXIES";

    /// <summary>
    /// Optional comma- or semicolon-separated allowlist of hostnames (or subdomain wildcards such as
    /// <c>*.example.org</c>) accepted in <c>X-Forwarded-Host</c>. When unset or empty, all forwarded hostnames from
    /// trusted proxies are accepted.
    /// </summary>
    public const string AllowedHostsKey = "CADENCE_ALLOWED_HOSTS";

    /// <summary>
    /// Optional public origin or base path (for example <c>https://api.example.org</c> or <c>/</c>) emitted in
    /// OpenAPI <c>servers</c> during offline contract export or when no external request host is active.
    /// </summary>
    public const string OpenApiPublicOriginKey = "CADENCE_OPENAPI_PUBLIC_ORIGIN";

    /// <summary>
    /// Optional boolean (<c>true</c>/<c>false</c>, default <c>false</c>) enabling HTTP-to-HTTPS redirection outside
    /// <c>Development</c> while keeping <c>/healthz</c>, <c>/healthz/live</c>, and <c>/healthz/ready</c> reachable
    /// over plain HTTP.
    /// </summary>
    public const string HttpsRedirectEnabledKey = "CADENCE_HTTPS_REDIRECT_ENABLED";

    /// <summary>
    /// Optional external HTTPS port (<c>1..65535</c>, default <c>443</c>) used when constructing redirect
    /// <c>Location</c> URLs while <see cref="HttpsRedirectEnabledKey"/> is enabled.
    /// </summary>
    public const string HttpsPortKey = "CADENCE_HTTPS_PORT";

    /// <summary>The <c>Strict-Transport-Security</c> max-age sent on HTTPS responses outside Development.</summary>
    public static readonly TimeSpan HstsMaxAge = TimeSpan.FromDays(365);

    /// <summary>
    /// Applies <paramref name="trustedProxies"/> (<see cref="TrustedProxiesKey"/>) and optional
    /// <paramref name="allowedHosts"/> (<see cref="AllowedHostsKey"/>) to <paramref name="options"/>. Throws when an
    /// entry is malformed so a configuration typo fails startup instead of silently weakening proxy validation.
    /// </summary>
    public static void ConfigureForwardedHeaders(
        ForwardedHeadersOptions options,
        string? trustedProxies,
        string? allowedHosts = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost
            | ForwardedHeaders.XForwardedPrefix;

        // Walk back through every trusted hop (front proxy, then gateway) to the first untrusted address,
        // which is the real client, instead of stopping after one hop.
        options.ForwardLimit = null;

        options.AllowedHosts.Clear();
        foreach (string host in ParseAllowedHosts(allowedHosts))
        {
            options.AllowedHosts.Add(host);
        }

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

    /// <summary>
    /// Parses and validates <see cref="AllowedHostsKey"/> entries. Accepts <c>*</c>, single-label subdomain wildcards
    /// (<c>*.example.org</c>), DNS hostnames, IPv4 addresses, or bracketed IPv6 literals without scheme, port, or path.
    /// </summary>
    public static IReadOnlyList<string> ParseAllowedHosts(string? allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(allowedHosts))
        {
            return [];
        }

        var parsed = new List<string>();
        foreach (string entry in allowedHosts.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IsValidAllowedHostEntry(entry))
            {
                throw new InvalidOperationException(
                    $"{AllowedHostsKey} entry '{entry}' is not a valid hostname or subdomain wildcard (for example api.example.org or *.example.org without scheme, port, or path).");
            }

            parsed.Add(entry);
        }

        return parsed;
    }

    /// <summary>
    /// Validates and normalizes <see cref="OpenApiPublicOriginKey"/> when set. Returns <see langword="null"/> when
    /// unset or whitespace, <c>"/"</c> for root relative path, or the origin/path without a trailing slash.
    /// </summary>
    public static string? NormalizeOpenApiPublicOrigin(string? configuredOrigin)
    {
        if (string.IsNullOrWhiteSpace(configuredOrigin))
        {
            return null;
        }

        string trimmed = configuredOrigin.Trim();
        if (trimmed == "/")
        {
            return "/";
        }

        if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            if (trimmed.IndexOfAny(['?', '#', '\\', ' ', '\t', '\r', '\n']) >= 0)
            {
                throw new InvalidOperationException(
                    $"{OpenApiPublicOriginKey} relative path '{configuredOrigin}' must not contain query, fragment, backslashes, or whitespace.");
            }

            return trimmed.TrimEnd('/');
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment))
        {
            string authority = $"{uri.Scheme.ToLowerInvariant()}://{uri.Authority}";
            string path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath.TrimEnd('/');
            return authority + path;
        }

        throw new InvalidOperationException(
            $"{OpenApiPublicOriginKey} value '{configuredOrigin}' must be '/' or an absolute http(s) URL without query or fragment (for example https://api.example.org).");
    }

    /// <summary>
    /// Resolves the OpenAPI <c>servers[0].url</c> from the active HTTP request's scheme, host, and path base after
    /// <c>UseForwardedHeaders</c> has run, or falls back to <paramref name="configuredPublicOrigin"/> / <c>"/"</c>
    /// during offline contract export.
    /// </summary>
    public static string ResolveOpenApiServerUrl(HttpContext? httpContext, string? configuredPublicOrigin = null)
    {
        string? normalizedOrigin = NormalizeOpenApiPublicOrigin(configuredPublicOrigin);
        HttpRequest? request = httpContext?.Request;
        if (request is null || !request.Host.HasValue || IsBareOfflineExportRequest(request))
        {
            return normalizedOrigin ?? "/";
        }

        string pathBase = request.PathBase.HasValue
            ? request.PathBase.Value!.TrimEnd('/')
            : string.Empty;

        return $"{request.Scheme}://{request.Host.Value}{pathBase}";
    }

    /// <summary>Parses <see cref="HttpsRedirectEnabledKey"/> (defaults to <see langword="false"/> when unset).</summary>
    public static bool ParseHttpsRedirectEnabled(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        string trimmed = rawValue.Trim();
        if (bool.TryParse(trimmed, out bool parsed))
        {
            return parsed;
        }

        return trimmed switch
        {
            "1" => true,
            "0" => false,
            _ => throw new InvalidOperationException(
                $"{HttpsRedirectEnabledKey} value '{rawValue}' must be 'true' or 'false'."),
        };
    }

    /// <summary>Parses <see cref="HttpsPortKey"/> (<c>1..65535</c>, or <see langword="null"/> when unset).</summary>
    public static int? ParseHttpsPort(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (int.TryParse(rawValue.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            && port is >= 1 and <= 65535)
        {
            return port;
        }

        throw new InvalidOperationException(
            $"{HttpsPortKey} value '{rawValue}' must be a valid TCP port between 1 and 65535.");
    }

    /// <summary>
    /// Returns <see langword="true"/> for container liveness and readiness probe paths (<c>/healthz</c>,
    /// <c>/healthz/live</c>, and <c>/healthz/ready</c>) that must remain reachable over plain HTTP.
    /// </summary>
    public static bool IsHealthProbePath(PathString path) =>
        path.Equals("/healthz", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/healthz/live", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/healthz/ready", StringComparison.OrdinalIgnoreCase);

    private static bool IsBareOfflineExportRequest(HttpRequest request) =>
        string.Equals(request.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        && string.Equals(request.Host.Value, "localhost", StringComparison.OrdinalIgnoreCase)
        && !request.PathBase.HasValue;

    private static bool IsValidAllowedHostEntry(string entry)
    {
        if (entry == "*")
        {
            return true;
        }

        if (entry.Contains("://", StringComparison.Ordinal)
            || entry.IndexOfAny(['/', '?', '#', '@', '\\', ' ', '\t']) >= 0)
        {
            return false;
        }

        string candidate = entry.StartsWith("*.", StringComparison.Ordinal) ? entry[2..] : entry;
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        if (candidate.StartsWith('['))
        {
            return candidate.EndsWith(']')
                && Uri.CheckHostName(candidate) == UriHostNameType.IPv6;
        }

        if (candidate.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        UriHostNameType hostType = Uri.CheckHostName(candidate);
        return hostType is UriHostNameType.Dns or UriHostNameType.IPv4;
    }
}
