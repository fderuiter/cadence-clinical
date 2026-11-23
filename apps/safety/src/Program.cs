using System;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Telemetry;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddHttpContextAccessor();

// GxPAuditInterceptor writes the hash-chained audit entries the DSUR operations record (AuditActions). Its actor is the
// request's verified caller; a save outside a verified request has no actor and fails rather than defaulting (#5462).
builder.AddCadencePostgresDbContext<SafetyDbContext>(configureOptions: (sp, options) => options.AddInterceptors(
    new GxPAuditInterceptor(
        () => CallerScope.VerifiedUserId(sp.GetRequiredService<IHttpContextAccessor>().HttpContext)
            ?? throw new InvalidOperationException("A safety audit entry needs a verified caller."),
        sp.GetRequiredService<IClock>())));
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
