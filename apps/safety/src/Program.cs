using System;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Kernel.Templating;
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

builder.Services.AddDbContext<SafetyDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddFluidTemplating();

FluidTemplateRenderer.RegisterAssembly(typeof(Program).Assembly);
FluidTemplateRenderer.RegisterType<IcsrHeader>();
FluidTemplateRenderer.RegisterType<IcsrReportIdentifiers>();
FluidTemplateRenderer.RegisterType<IcsrPatient>();
FluidTemplateRenderer.RegisterType<IcsrMeddraCoding>();
FluidTemplateRenderer.RegisterType<IcsrReaction>();
FluidTemplateRenderer.RegisterType<IcsrSuspectDrug>();
FluidTemplateRenderer.RegisterType<IcsrReport>();

var app = builder.Build();
app.UseCadenceWebDefaults();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Safety" }))
    .WithName("SafetyHealthCheck")
    .WithSummary("Clinical Safety & Pharmacovigilance health probe.");

var api = app.MapGroup("/api/v1/safety");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", service = "Cadence.Apps.Safety" }))
    .WithName("SafetyStatus")
    .WithSummary("Returns Safety operational status.");

api.MapPost("/icsr/render", async (IcsrReport request, ITemplateRenderer renderer) =>
{
    string rendered = await renderer.RenderAsync("e2b_r3_icsr.xml.liquid", request, typeof(Program).Assembly);
    return TypedResults.Content(rendered, "application/xml");
})
.WithName("RenderIcsrReport")
.WithSummary("Renders an ICSR E2B(R3) safety report as Liquid XML.");

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
