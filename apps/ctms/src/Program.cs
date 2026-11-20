using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Clients;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;
using Cadence.Templating;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
string kernelServiceUrl = builder.Configuration["KERNEL_SERVICE_URL"]
    ?? builder.Configuration["Services:Kernel"]
    ?? "http://localhost:8018";
builder.Services.AddHttpClient<IKernelDelegationBridge, KernelDelegationBridge>(client =>
{
    client.BaseAddress = new Uri(kernelServiceUrl.TrimEnd('/'));
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.AddCadencePostgresDbContext<CtmsDbContext>();
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
