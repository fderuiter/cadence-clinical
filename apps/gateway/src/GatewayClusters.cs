using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Health;

namespace Cadence.Apps.Gateway;

/// <summary>
/// Options controlling YARP forwarder timeout and active readiness health-check probe cadence across gateway clusters.
/// </summary>
public sealed class GatewayClusterOptions
{
    public const string ProxyTimeoutSecondsKey = "GATEWAY_PROXY_TIMEOUT_SECONDS";
    public const string HealthCheckIntervalSecondsKey = "GATEWAY_HEALTH_CHECK_INTERVAL_SECONDS";
    public const string HealthCheckTimeoutSecondsKey = "GATEWAY_HEALTH_CHECK_TIMEOUT_SECONDS";

    [ConfigurationKeyName(ProxyTimeoutSecondsKey)]
    [Range(1, int.MaxValue)]
    public int ProxyTimeoutSeconds { get; set; } = GatewayClusters.DefaultProxyTimeoutSeconds;

    [ConfigurationKeyName(HealthCheckIntervalSecondsKey)]
    [Range(1, int.MaxValue)]
    public int HealthCheckIntervalSeconds { get; set; } = GatewayClusters.DefaultHealthCheckIntervalSeconds;

    [ConfigurationKeyName(HealthCheckTimeoutSecondsKey)]
    [Range(1, int.MaxValue)]
    public int HealthCheckTimeoutSeconds { get; set; } = GatewayClusters.DefaultHealthCheckTimeoutSeconds;
}

/// <summary>
/// Builds the gateway's YARP clusters with the same timeout and health-check policy for every service.
/// A request whose service stops sending data for <c>GATEWAY_PROXY_TIMEOUT_SECONDS</c> fails with 504 rather
/// than holding the connection. Each service's <c>/healthz/ready</c> is probed every
/// <c>GATEWAY_HEALTH_CHECK_INTERVAL_SECONDS</c>, and connection failures are counted passively. While a service is
/// unhealthy, the gateway answers 503 at once instead of forwarding to it.
/// </summary>
public static class GatewayClusters
{
    public const string ReadinessPath = "/healthz/ready";
    public const int DefaultProxyTimeoutSeconds = 30;
    public const int DefaultHealthCheckIntervalSeconds = 10;
    public const int DefaultHealthCheckTimeoutSeconds = 5;

    // Two failed probes in a row mark a service unhealthy; the next successful probe restores it.
    private const string ConsecutiveFailuresThreshold = "2";

    // The time a destination hit by connection failures stays out of rotation before it is retried.
    private static readonly TimeSpan PassiveReactivationPeriod = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddGatewayClusterOptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<GatewayClusterOptions>()
            .Configure(options =>
            {
                if (TryParseInt(configuration[GatewayClusterOptions.ProxyTimeoutSecondsKey], out int proxyTimeout))
                {
                    options.ProxyTimeoutSeconds = proxyTimeout;
                }

                if (TryParseInt(configuration[GatewayClusterOptions.HealthCheckIntervalSecondsKey], out int interval))
                {
                    options.HealthCheckIntervalSeconds = interval;
                }

                if (TryParseInt(configuration[GatewayClusterOptions.HealthCheckTimeoutSecondsKey], out int timeout))
                {
                    options.HealthCheckTimeoutSeconds = timeout;
                }
            })
            .ValidateDataAnnotations()
            .Validate(
                _ => IsUnsetOrPositiveInt(configuration[GatewayClusterOptions.ProxyTimeoutSecondsKey]),
                $"{GatewayClusterOptions.ProxyTimeoutSecondsKey} must be a positive number of seconds.")
            .Validate(
                _ => IsUnsetOrPositiveInt(configuration[GatewayClusterOptions.HealthCheckIntervalSecondsKey]),
                $"{GatewayClusterOptions.HealthCheckIntervalSecondsKey} must be a positive number of seconds.")
            .Validate(
                _ => IsUnsetOrPositiveInt(configuration[GatewayClusterOptions.HealthCheckTimeoutSecondsKey]),
                $"{GatewayClusterOptions.HealthCheckTimeoutSecondsKey} must be a positive number of seconds.")
            .ValidateOnStart();

        return services;
    }

    public static ClusterConfig Create(string clusterId, string address, GatewayClusterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ClusterConfig
        {
            ClusterId = clusterId,
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["destination"] = new DestinationConfig { Address = address },
            },
            HttpRequest = new ForwarderRequestConfig
            {
                ActivityTimeout = TimeSpan.FromSeconds(options.ProxyTimeoutSeconds),
            },
            HealthCheck = new HealthCheckConfig
            {
                // Without this, YARP falls back to unhealthy destinations when none is healthy ("HealthyOrPanic"),
                // and every cluster has a single destination, so health checks would never stop a request.
                AvailableDestinationsPolicy = HealthCheckConstants.AvailableDestinations.HealthyAndUnknown,
                Active = new ActiveHealthCheckConfig
                {
                    Enabled = true,
                    Interval = TimeSpan.FromSeconds(options.HealthCheckIntervalSeconds),
                    Timeout = TimeSpan.FromSeconds(options.HealthCheckTimeoutSeconds),
                    Policy = "ConsecutiveFailures",
                    Path = ReadinessPath,
                },
                Passive = new PassiveHealthCheckConfig
                {
                    Enabled = true,
                    Policy = "TransportFailureRate",
                    ReactivationPeriod = PassiveReactivationPeriod,
                },
            },
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ConsecutiveFailuresHealthPolicyOptions.ThresholdMetadataName] = ConsecutiveFailuresThreshold,
            },
        };
    }

    private static bool TryParseInt(string? raw, out int value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = default;
            return false;
        }

        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsUnsetOrPositiveInt(string? raw) =>
        raw is null
        || (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= 1);
}

internal sealed class GatewayProxyConfigProvider(
    IReadOnlyList<RouteConfig> routes,
    IReadOnlyList<(string ClusterId, string Url)> endpoints,
    IOptions<GatewayClusterOptions> options) : IProxyConfigProvider
{
    private InMemoryConfigProvider? _inner;

    public IProxyConfig GetConfig()
    {
        _inner ??= new InMemoryConfigProvider(
            routes,
            endpoints.Select(endpoint => GatewayClusters.Create(endpoint.ClusterId, endpoint.Url, options.Value)).ToArray());
        return _inner.GetConfig();
    }
}
