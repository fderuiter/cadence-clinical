using System;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
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

builder.Services.AddCadenceDbContext<PaymentsDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();
app.UseCadenceWebDefaults();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Payments" }))
    .WithName("PaymentsHealthCheck")
    .WithSummary("Site and participant payments health probe.");

var api = app.MapGroup("/api/v1/payments");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", service = "Cadence.Apps.Payments" }))
    .WithName("PaymentsStatus")
    .WithSummary("Returns Payments operational status.");

await app.RunAsync();

namespace Cadence.Apps.Payments
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
