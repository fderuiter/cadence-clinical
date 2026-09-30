using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<SafetyDbContext>();
builder.Services.AddFluidTemplating();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);

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

var safetyGroup = app.MapGroup("/api/v1/safety");
safetyGroup.MapIcsrTemplateEndpoints();
safetyGroup.MapDsurEndpoints();

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
