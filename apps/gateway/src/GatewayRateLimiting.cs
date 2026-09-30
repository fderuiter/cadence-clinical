using System;
using System.Globalization;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Gateway;

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

        int permits = configuration.GetValue(PermitsPerMinuteKey, DefaultPermitsPerMinute);
        if (permits <= 0)
        {
            throw new InvalidOperationException($"{PermitsPerMinuteKey} must be a positive number of requests (was {permits}).");
        }

        return services.AddRateLimiter(options =>
        {
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
    }

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst(CadenceClaimTypes.Subject)?.Value is { Length: > 0 } subject
            ? "user:" + subject
            : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}
