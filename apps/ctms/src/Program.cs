using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<CtmsDbContext>();
builder.Services.AddFluidTemplating();

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

app.MapGroup("/api/v1/ctms").MapLetterTemplateEndpoints();

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
