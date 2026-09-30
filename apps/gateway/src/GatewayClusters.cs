using System;
using System.Collections.Generic;

using Microsoft.Extensions.Configuration;

using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Health;

namespace Cadence.Apps.Gateway;

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

    public static ClusterConfig Create(string clusterId, string address, IConfiguration configuration) => new()
    {
        ClusterId = clusterId,
        Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["destination"] = new DestinationConfig { Address = address },
        },
        HttpRequest = new ForwarderRequestConfig
        {
            ActivityTimeout = Seconds(configuration, "GATEWAY_PROXY_TIMEOUT_SECONDS", DefaultProxyTimeoutSeconds),
        },
        HealthCheck = new HealthCheckConfig
        {
            // Without this, YARP falls back to unhealthy destinations when none is healthy ("HealthyOrPanic"),
            // and every cluster has a single destination, so health checks would never stop a request.
            AvailableDestinationsPolicy = HealthCheckConstants.AvailableDestinations.HealthyAndUnknown,
            Active = new ActiveHealthCheckConfig
            {
                Enabled = true,
                Interval = Seconds(configuration, "GATEWAY_HEALTH_CHECK_INTERVAL_SECONDS", DefaultHealthCheckIntervalSeconds),
                Timeout = Seconds(configuration, "GATEWAY_HEALTH_CHECK_TIMEOUT_SECONDS", DefaultHealthCheckTimeoutSeconds),
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

    private static TimeSpan Seconds(IConfiguration configuration, string key, int defaultSeconds)
    {
        int seconds = configuration.GetValue(key, defaultSeconds);
        return seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : throw new InvalidOperationException($"{key} must be a positive number of seconds (was {seconds}).");
    }
}
