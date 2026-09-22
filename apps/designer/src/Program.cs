using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Cadence.Telemetry;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();

var app = builder.Build();
app.UseCadenceWebDefaults();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Designer", graph = "Neo4j" }))
    .WithName("DesignerHealthCheck")
    .WithSummary("Protocol designer metadata health probe.");

var api = app.MapGroup("/api/v1/studies");
api.MapGet("/", () => TypedResults.Ok(Array.Empty<string>()))
    .WithName("ListStudies")
    .WithSummary("Lists active CDISC USDM study protocol graph definitions.");

await app.RunAsync();

namespace Cadence.Apps.Designer
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}

