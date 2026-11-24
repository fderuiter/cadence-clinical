using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddHttpContextAccessor();

// AddCadencePostgresDbContext attaches GxPAuditInterceptor by default with fail-closed caller resolution (#5462).
builder.AddCadencePostgresDbContext<SafetyDbContext>();
// DSUR approval is e-signed; each token is single use across instances and restarts (ADR-2515, #5434, #5462).
builder.Services.AddCadenceDurableSigTokenStore<SafetyDbContext>();
builder.Services.AddFluidTemplating();

FluidTemplateRenderer.RegisterType<IcsrHeader>();
FluidTemplateRenderer.RegisterType<IcsrSafetyReport>();
FluidTemplateRenderer.RegisterType<IcsrOtherCaseIdentifier>();
FluidTemplateRenderer.RegisterType<IcsrPrimarySource>();
FluidTemplateRenderer.RegisterType<IcsrSender>();
FluidTemplateRenderer.RegisterType<IcsrStudy>();
FluidTemplateRenderer.RegisterType<IcsrPatient>();
FluidTemplateRenderer.RegisterType<IcsrMeddraCoding>();
FluidTemplateRenderer.RegisterType<IcsrReaction>();
FluidTemplateRenderer.RegisterType<IcsrDrug>();
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
