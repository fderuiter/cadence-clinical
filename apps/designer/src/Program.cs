using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

ConfigureLogging(builder.Logging);
ConfigureTelemetry(builder.Services);

builder.Services.AddOpenApi();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var activity = System.Diagnostics.Activity.Current;
    if (activity != null)
    {
        var traceParent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
        context.Response.Headers["traceparent"] = traceParent;
    }
    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Designer", graph = "Neo4j" }))
    .WithName("DesignerHealthCheck")
    .WithSummary("Protocol designer metadata health probe.");

var api = app.MapGroup("/api/v1/studies");
api.MapGet("/", () => TypedResults.Ok(Array.Empty<string>()))
    .WithName("ListStudies")
    .WithSummary("Lists active CDISC USDM study protocol graph definitions.");

await app.RunAsync();

static void ConfigureLogging(ILoggingBuilder logging)
{
    logging.ClearProviders();
    logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
    });
}

static void ConfigureTelemetry(IServiceCollection services)
{
    services.AddOpenTelemetry()
        .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
        .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
}

namespace Cadence.Apps.Designer
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}

