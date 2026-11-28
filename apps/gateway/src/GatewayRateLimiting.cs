using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Gateway;

/// <summary>
/// Options controlling the per-caller request rate limit enforced by the gateway.
/// </summary>
public sealed class GatewayRateLimitOptions
{
    [ConfigurationKeyName(GatewayRateLimiting.PermitsPerMinuteKey)]
    [Range(1, int.MaxValue)]
    public int PermitsPerMinute { get; set; } = GatewayRateLimiting.DefaultPermitsPerMinute;
}

/// <summary>
/// Limits how many requests each caller can send through the gateway. An authenticated caller is counted by
/// the token's subject, anyone else by client IP address. Over the limit, the gateway answers 429 with a
/// <c>Retry-After</c> header and forwards nothing. Health probes are never limited.
/// </summary>
public static class GatewayRateLimiting
{
    public const string PermitsPerMinuteKey = "GATEWAY_RATE_LIMIT_PER_MINUTE";
    public const int DefaultPermitsPerMinute = 600;

    // A fixed one-minute window: its lease reports when the window resets, which becomes Retry-After.
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<GatewayRateLimitOptions>()
            .Configure(options =>
            {
                if (configuration[PermitsPerMinuteKey] is { } raw
                    && !string.IsNullOrWhiteSpace(raw)
                    && int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    options.PermitsPerMinute = parsed;
                }
            })
            .ValidateDataAnnotations()
            .Validate(
                _ => configuration[PermitsPerMinuteKey] is not { } raw
                    || (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= 1),
                $"{PermitsPerMinuteKey} must be a positive number of requests.")
            .ValidateOnStart();

        services.AddRateLimiter();
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<GatewayRateLimitOptions>>((options, rateLimitOptions) =>
            {
                int permits = rateLimitOptions.Value.PermitsPerMinute;
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = (context, _) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    }

                    return ValueTask.CompletedTask;
                };
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    context.Request.Path.StartsWithSegments("/healthz")
                        ? RateLimitPartition.GetNoLimiter("probes")
                        : RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = permits,
                            Window = Window,
                            QueueLimit = 0,
                            AutoReplenishment = true,
                        }));
            });

        return services;
    }

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst(CadenceClaimTypes.Subject)?.Value is { Length: > 0 } subject
            ? "user:" + subject
            : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}
