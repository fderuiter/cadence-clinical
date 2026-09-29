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

public static class CadenceTelemetryExtensions
{
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

    public static WebApplication MapCadenceHealthAndStatusEndpoints(
        this WebApplication app,
        string serviceName,
        string routePrefix,
        string healthSummary,
        string statusSummary)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentNullException.ThrowIfNull(routePrefix);
        ArgumentNullException.ThrowIfNull(healthSummary);
        ArgumentNullException.ThrowIfNull(statusSummary);

        string pascalRoutePrefix = routePrefix.Length > 0
            ? char.ToUpper(routePrefix[0]) + routePrefix[1..]
            : routePrefix;

        app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = serviceName }))
            .WithName($"{pascalRoutePrefix}HealthCheck")
            .WithSummary(healthSummary);

        var api = app.MapGroup($"/api/v1/{routePrefix}");
        api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", service = serviceName }))
            .WithName($"{pascalRoutePrefix}Status")
            .WithSummary(statusSummary);

        return app;
    }
}
