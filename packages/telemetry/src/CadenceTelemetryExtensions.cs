using System;
using System.Diagnostics;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Scalar.AspNetCore;

namespace Cadence.Telemetry;

public static partial class CadenceTelemetryExtensions
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "Traceparent header '{TraceParent}' attached to response for request path '{Path}'")]
    public static partial void LogTraceparentHeaderInjected(this ILogger logger, string traceParent, string? path);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Cadence telemetry and logging initialized for service '{ServiceName}'")]
    public static partial void LogTelemetryInitialized(this ILogger logger, string serviceName);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Trace,
        Message = "Tracing request '{Method}' '{Path}' with ActivityId '{ActivityId}'")]
    public static partial void LogRequestTracingEvent(this ILogger logger, string method, string? path, string? activityId);

    public static WebApplicationBuilder AddCadenceLogging(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            options.JsonWriterOptions = new JsonWriterOptions
            {
                Indented = false
            };
        });

        return builder;
    }

    public static IServiceCollection AddCadenceTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        return services;
    }

    public static WebApplicationBuilder AddCadenceWebDefaults(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddCadenceLogging();
        builder.Services.AddCadenceTelemetry();
        builder.Services.AddOpenApi();

        return builder;
    }

    public static IApplicationBuilder UseCadenceTraceparent(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var activity = Activity.Current;
            if (activity != null)
            {
                var traceParent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
                context.Response.Headers["traceparent"] = traceParent;

                var logger = context.RequestServices?.GetService<ILogger<IApplicationBuilder>>();
                if (logger != null)
                {
                    logger.LogTraceparentHeaderInjected(traceParent, context.Request.Path.Value);
                }
            }

            await next(context);
        });
    }

    public static IEndpointRouteBuilder MapCadenceApiDocumentation(this IEndpointRouteBuilder endpoints, IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsDevelopment())
        {
            endpoints.MapOpenApi();
            endpoints.MapScalarApiReference();
        }

        return endpoints;
    }

    public static WebApplication UseCadenceWebDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseCadenceTraceparent();
        app.MapCadenceApiDocumentation(app.Environment);

        return app;
    }
}
