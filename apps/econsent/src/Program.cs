using System;

using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
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

builder.Services.AddCadenceDbContext<EconsentDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();
app.UseCadenceWebDefaults();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Econsent" }))
    .WithName("EconsentHealthCheck")
    .WithSummary("eConsent service health probe.");

var api = app.MapGroup("/api/v1/econsent");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", service = "Cadence.Apps.Econsent" }))
    .WithName("EconsentStatus")
    .WithSummary("Returns eConsent operational status.");

await app.RunAsync();

namespace Cadence.Apps.Econsent
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
