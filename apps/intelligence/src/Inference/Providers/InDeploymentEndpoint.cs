using System;
using System.Net;
using System.Net.Sockets;

namespace Cadence.Modules.Intelligence.Inference.Providers;

/// <summary>
/// Decides whether an endpoint is reachable only inside the deployment (ADR-2636 §4.1), which is what makes a
/// local-only tenant checkable: a loopback or private address (RFC 1918, RFC 4193) or a cluster-internal host name.
/// Public addresses and public DNS names are refused, even when the operator labels the endpoint local.
/// </summary>
public static class InDeploymentEndpoint
{
    private static readonly string[] InternalSuffixes = [".svc", ".cluster.local", ".internal", ".localhost"];

    /// <summary>Null when <paramref name="endpoint"/> is in the deployment; otherwise why it is not.</summary>
    public static string? Problem(Uri? endpoint)
    {
        if (endpoint is null || !endpoint.IsAbsoluteUri)
        {
            return "an absolute endpoint URL is required";
        }

        if (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return $"scheme '{endpoint.Scheme}' is not http or https";
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo))
        {
            return "credentials in the URL are not allowed; use the ApiKey setting";
        }

        return IsInDeploymentHost(endpoint.IdnHost) ? null : $"host '{endpoint.Host}' is not a loopback, private or cluster-internal address";
    }

    /// <summary>True for a loopback or private IP literal, <c>localhost</c>, a single-label name or a cluster-internal suffix.</summary>
    public static bool IsInDeploymentHost(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var trimmed = host.Trim('[', ']').TrimEnd('.');
        if (IPAddress.TryParse(trimmed, out var address))
        {
            return IsPrivate(address);
        }

        if (trimmed.Length == 0)
        {
            return false;
        }

        var lower = trimmed.ToLowerInvariant();
        if (lower == "localhost" || !lower.Contains('.', StringComparison.Ordinal))
        {
            return true;
        }

        return Array.Exists(InternalSuffixes, suffix => lower.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fc00::/7, unique local addresses (RFC 4193).
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}
