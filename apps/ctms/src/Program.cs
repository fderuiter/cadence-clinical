using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
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
builder.AddCadencePostgresDbContext<CtmsDbContext>();
builder.Services.AddFluidTemplating();

FluidTemplateRenderer.RegisterAssembly(typeof(Program).Assembly);
FluidTemplateRenderer.RegisterType<SiteVisitFindingModel>();
FluidTemplateRenderer.RegisterType<SiteVisitLetterModel>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Ctms",
    "ctms",
    "CTMS operations health probe.",
    "Returns CTMS operational status.");

var api = app.MapGroup("/api/v1/ctms");
api.MapPost("/letters/render", async (string templateName, SiteVisitLetterModel request, ITemplateRenderer renderer) =>
{
    string rendered = await renderer.RenderAsync(templateName, request, typeof(Program).Assembly);
    return TypedResults.Content(rendered, "text/plain");
})
.WithName("RenderSiteVisitLetter")
.WithSummary("Renders a CTMS site visit monitoring letter via Liquid template.");

await app.RunAsync();

namespace Cadence.Apps.Ctms
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
