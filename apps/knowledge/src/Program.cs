using System;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();

string connectionString = builder.Configuration["POSTGRES_CONNECTION_STRING"]
    is { Length: > 0 } configuredConnectionString
        ? configuredConnectionString
        : "Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=cadence_password";

builder.Services.AddDbContext<KnowledgeDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();
app.UseCadenceWebDefaults();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Knowledge" }))
    .WithName("KnowledgeHealthCheck")
    .WithSummary("Knowledge base and study training health probe.");

var api = app.MapGroup("/api/v1/knowledge");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", service = "Cadence.Apps.Knowledge" }))
    .WithName("KnowledgeStatus")
    .WithSummary("Returns Knowledge base operational status.");

await app.RunAsync();

namespace Cadence.Apps.Knowledge
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
