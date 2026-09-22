using System;
using System.Collections.Generic;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Scalar.AspNetCore;

using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

string designerUrl = builder.Configuration["DESIGNER_SERVICE_URL"]
    is { Length: > 0 } configuredDesignerUrl
        ? configuredDesignerUrl
        : new UriBuilder(Uri.UriSchemeHttp, "localhost", 8001).ToString();

string executionUrl = builder.Configuration["EXECUTION_SERVICE_URL"]
    is { Length: > 0 } configuredExecutionUrl
        ? configuredExecutionUrl
        : new UriBuilder(Uri.UriSchemeHttp, "localhost", 8002).ToString();

var routes = new[]
{
    new RouteConfig
    {
        RouteId = "designer-studies-route",
        ClusterId = "designer-cluster",
        Match = new RouteMatch
        {
            Path = "/api/v1/studies/{**catch-all}"
        }
    },
    new RouteConfig
    {
        RouteId = "execution-subjects-route",
        ClusterId = "execution-cluster",
        Match = new RouteMatch
        {
            Path = "/api/v1/subjects/{**catch-all}"
        }
    }
};

var clusters = new[]
{
    new ClusterConfig
    {
        ClusterId = "designer-cluster",
        Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
        {
            { "designer-destination", new DestinationConfig { Address = designerUrl } }
        }
    },
    new ClusterConfig
    {
        ClusterId = "execution-cluster",
        Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
        {
            { "execution-destination", new DestinationConfig { Address = executionUrl } }
        }
    }
};

builder.Services.AddReverseProxy()
    .LoadFromMemory(routes, clusters);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Gateway", version = "10.0" }))
    .WithName("HealthCheck")
    .WithSummary("Gateway liveness and readiness probe.");

var api = app.MapGroup("/api/v1");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", gateway = "Cadence.Gateway.NET10" }))
    .WithName("GatewayStatus")
    .WithSummary("Returns gateway dispatch status.");

app.MapReverseProxy();

await app.RunAsync();

namespace Cadence.Apps.Gateway
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
