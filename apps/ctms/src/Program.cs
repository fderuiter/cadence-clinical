using System;
using System.Linq;

using Cadence.Apps.Ctms.Application;
using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;
using Cadence.Templating;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.AddCadencePostgresDbContext<CtmsDbContext>();
builder.Services.AddCadenceDurableSigTokenStore<CtmsDbContext>();
if ((builder.Configuration.GetSection(CadenceMigrationOptions.SectionName).Get<CadenceMigrationOptions>() is { MigrateOnStartup: true, MigrateOnly: false })
    || string.Equals(builder.Configuration["Database:SeedOnStartup"], "true", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<CtmsStartupSeederHostedService>();
}
builder.Services.AddFluidTemplating();

builder.Services.AddMassTransit(x =>
{
    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

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

var ctmsApi = app.MapGroup("/api/v1/ctms");
ctmsApi.MapLetterTemplateEndpoints();
ctmsApi.MapStartupGreenlightEndpoints();
ctmsApi.MapMonitoringVisitEndpoints();
ctmsApi.MapSiteMilestoneEndpoints();
ctmsApi.MapDoaEndpoints();

if (args.Contains("--seed"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CtmsDbContext>();
    var clock = scope.ServiceProvider.GetRequiredService<IClock>();
    await CtmsDbSeeder.EnsureSeededAsync(dbContext, clock);
    Console.WriteLine("[SUCCESS] CtmsDbContext seeded successfully.");
    return;
}

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
