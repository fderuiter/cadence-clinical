using System;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
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

builder.Services.AddCadenceDbContext<ImagingDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();
app.UseCadenceWebDefaults();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Imaging" }))
    .WithName("ImagingHealthCheck")
    .WithSummary("Medical Imaging DICOM service health probe.");

var api = app.MapGroup("/api/v1/imaging");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", service = "Cadence.Apps.Imaging" }))
    .WithName("ImagingStatus")
    .WithSummary("Returns Medical Imaging operational status.");

await app.RunAsync();

namespace Cadence.Apps.Imaging
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
