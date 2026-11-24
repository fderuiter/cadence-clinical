using System;

using Cadence.Apps.Econsent.Endpoints;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Apps.Econsent.Services;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Security.FieldProtection;
using Cadence.Telemetry;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
// Signer e-mail addresses are encrypted at rest; FIELD_ENCRYPTION_KEY is required and validated at startup (#4770).
builder.Services.AddCadenceFieldEncryption();
builder.AddCadencePostgresDbContext<EconsentDbContext>();
// Consent capture is e-signed; each token is single use across instances and restarts (ADR-2515, #5434).
builder.Services.AddCadenceDurableSigTokenStore<EconsentDbContext>();
builder.Services.AddHttpClient<IEconsentExecutionWebhookPublisher, HttpEconsentExecutionWebhookPublisher>(client =>
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
    x.AddEntityFrameworkOutbox<EconsentDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });
    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Econsent",
    "econsent",
    "eConsent service health probe.",
    "Returns eConsent operational status.");
app.MapEconsentEndpoints();

await app.RunAsync();

namespace Cadence.Apps.Econsent
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
