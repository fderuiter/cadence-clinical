using System;

using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();

var app = builder.Build();
app.UseCadenceWebDefaults();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.AiGateway" }))
    .WithName("AiGatewayHealthCheck")
    .WithSummary("AI Gateway medical coding proxy health probe.");

var api = app.MapGroup("/api/v1/ai");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", service = "Cadence.Apps.AiGateway" }))
    .WithName("AiGatewayStatus")
    .WithSummary("Returns AI Gateway operational status.");

await app.RunAsync();

namespace Cadence.Apps.AiGateway
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
