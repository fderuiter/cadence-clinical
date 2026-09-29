using System;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Database;
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

builder.Services.AddCadenceDbContext<SafetyDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();
app.UseCadenceWebDefaults();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Safety" }))
    .WithName("SafetyHealthCheck")
    .WithSummary("Clinical Safety & Pharmacovigilance health probe.");

var api = app.MapGroup("/api/v1/safety");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", service = "Cadence.Apps.Safety" }))
    .WithName("SafetyStatus")
    .WithSummary("Returns Safety operational status.");

await app.RunAsync();

namespace Cadence.Apps.Safety
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
