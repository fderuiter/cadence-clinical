using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
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

    /// <summary>Configuration key that switches on OTLP export for traces, metrics and logs.</summary>
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Health check tag for dependencies that must be reachable before a service takes traffic.</summary>
    public const string ReadinessTag = "ready";

    public static IServiceCollection AddCadenceTelemetry(
        this IServiceCollection services,
        string? serviceName = null,
        bool exportOtlp = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        var telemetry = services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                        options.Filter = context => !context.Request.Path.StartsWithSegments("/healthz"))
                    .AddHttpClientInstrumentation()
                    .AddSource("Npgsql");
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Npgsql");
            });

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            telemetry.ConfigureResource(resource => resource.AddService(serviceName));
        }

        if (exportOtlp)
        {
            telemetry.WithLogging().UseOtlpExporter();
        }

        return services;
    }

    /// <summary>
    /// Registers tracing, metrics and runtime instrumentation named after the service. When
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is configured (the Aspire AppHost sets it), traces, metrics and
    /// logs are exported over OTLP; otherwise nothing is exported.
    /// </summary>
    public static WebApplicationBuilder AddCadenceTelemetry(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string serviceName = builder.Configuration["OTEL_SERVICE_NAME"] is { Length: > 0 } configured
            ? configured
            : builder.Environment.ApplicationName;

        builder.Services.AddCadenceTelemetry(
            serviceName,
            exportOtlp: !string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]));

        return builder;
    }

    public static WebApplicationBuilder AddCadenceWebDefaults(
        this WebApplicationBuilder builder,
        Action<ProblemDetailsOptions>? configureProblemDetails = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddCadenceLogging();
        builder.AddCadenceTelemetry();

        // Don't advertise the server software, honour X-Forwarded-* only from the configured proxies, and
        // tell browsers to stay on HTTPS (sent only on HTTPS responses outside Development).
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);
        string? trustedProxies = builder.Configuration[CadenceHttpHardening.TrustedProxiesKey];
        CadenceHttpHardening.ConfigureForwardedHeaders(new ForwardedHeadersOptions(), trustedProxies); // fail at startup on a bad entry
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
            CadenceHttpHardening.ConfigureForwardedHeaders(options, trustedProxies));
        builder.Services.AddHsts(options => options.MaxAge = CadenceHttpHardening.HstsMaxAge);
        builder.Services.AddHealthChecks();
        if (configureProblemDetails != null)
        {
            builder.Services.AddProblemDetails(configureProblemDetails);
        }
        else
        {
            builder.Services.AddProblemDetails();
        }
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
            endpoints.MapOpenApi().AllowAnonymous();
            endpoints.MapScalarApiReference().AllowAnonymous();
        }

        return endpoints;
    }

    public static WebApplication UseCadenceWebDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseForwardedHeaders();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseExceptionHandler();
        app.UseCadenceTraceparent();
        app.MapCadenceApiDocumentation(app.Environment);
        app.MapCadenceProbeEndpoints();

        return app;
    }

    /// <summary>
    /// Maps <c>/healthz/live</c> (process is up; runs no checks) and <c>/healthz/ready</c> (every check
    /// tagged <see cref="ReadinessTag"/>, e.g. the service's database, must pass). Orchestrators should use
    /// these rather than the static <c>/healthz</c> payload.
    /// </summary>
    public static IEndpointRouteBuilder MapCadenceProbeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Orchestrators probe without credentials, so the probes opt out of the services' authenticate-by-default
        // fallback policy. They return only Healthy/Unhealthy.
        endpoints.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();
        endpoints.MapHealthChecks("/healthz/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadinessTag)
        })
            .AllowAnonymous();

        return endpoints;
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
            ? char.ToUpper(routePrefix[0], CultureInfo.InvariantCulture) + routePrefix[1..]
            : routePrefix;

        app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = serviceName }))
            .WithName($"{pascalRoutePrefix}HealthCheck")
            .WithSummary(healthSummary)
            .AllowAnonymous();

        var api = app.MapGroup($"/api/v1/{routePrefix}");
        api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", service = serviceName }))
            .WithName($"{pascalRoutePrefix}Status")
            .WithSummary(statusSummary)
            .AllowAnonymous();

        return app;
    }
}
