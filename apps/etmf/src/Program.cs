using System;

using Cadence.Apps.Etmf.Application;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Consumers;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Security;
using Cadence.Apps.Etmf.Services;
using Cadence.Apps.Etmf.Workers;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Storage;
using Cadence.Telemetry;
using Cadence.Topology;

using MassTransit;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, EtmfScopeAuthorizationResultHandler>();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddCadenceStorage(builder.Configuration, builder.Environment);
builder.AddCadencePostgresDbContext<EtmfDbContext>();
builder.Services.AddScoped<IEtmfUnitOfWork, EtmfUnitOfWork>();
builder.Services.AddScoped<IEtmfQcService, EtmfQcService>();
builder.Services.AddScoped<IEtmfDocumentService, EtmfDocumentService>();
builder.Services.AddScoped<IEtmfExportService, EtmfExportService>();
builder.Services.AddSingleton<EtmfExportQueue>();
builder.Services.AddHostedService<EtmfExportWorker>();

bool allowLocalhostFallbacks = builder.Configuration.GetValue<bool>(ServiceTopology.AllowLocalhostFallbacksKey);
Uri executionServiceUri = ServiceTopology.ResolveServiceUrl(
    ServiceTopology.Get("execution"),
    builder.Configuration["EXECUTION_SERVICE_URL"],
    builder.Environment.IsDevelopment(),
    allowLocalhostFallbacks);

builder.Services.AddHttpClient<IEtmfCompletenessWebhookPublisher, HttpEtmfCompletenessWebhookPublisher>(client =>
{
    client.BaseAddress = executionServiceUri;
});

builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<EtmfDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });
    x.AddConsumer<EtmfDocumentFilingRequestedConsumer>();
    x.AddConsumer<EConsentCompletedConsumer>();
    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Etmf",
    "etmf",
    "eTMF operations health probe.",
    "Returns eTMF operational status.");

app.MapEtmfEndpoints();

await app.RunAsync();

namespace Cadence.Apps.Etmf
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
