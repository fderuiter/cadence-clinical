using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Templating;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<SafetyDbContext>();
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
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Safety",
    "safety",
    "Clinical Safety & Pharmacovigilance health probe.",
    "Returns Safety operational status.");

var api = app.MapGroup("/api/v1/safety");
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
