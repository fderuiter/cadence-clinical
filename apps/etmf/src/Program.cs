using System;

using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Consumers;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Apps.Etmf.Workers;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.AddCadencePostgresDbContext<EtmfDbContext>();
builder.Services.AddSingleton<EtmfExportQueue>();
builder.Services.AddHostedService<EtmfExportWorker>();
builder.Services.AddHttpClient<IEtmfCompletenessWebhookPublisher, HttpEtmfCompletenessWebhookPublisher>(client =>
{
    string? executionUrl = builder.Configuration["EXECUTION_SERVICE_URL"];
    if (!string.IsNullOrWhiteSpace(executionUrl) && Uri.TryCreate(executionUrl, UriKind.Absolute, out var configuredUri))
    {
        client.BaseAddress = configuredUri;
    }
    else if (builder.Environment.IsDevelopment())
    {
        client.BaseAddress = new UriBuilder(Uri.UriSchemeHttp, "localhost", 8002).Uri;
    }
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
