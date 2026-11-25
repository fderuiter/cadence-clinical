using Cadence.Apps.Payments.Application;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Consumers;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Security.FieldProtection;
using Cadence.Telemetry;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
// Processor references are encrypted at rest; FIELD_ENCRYPTION_KEY is required and validated at startup (#4770).
builder.Services.AddCadenceFieldEncryption();
builder.AddCadencePostgresDbContext<PaymentsDbContext>();
builder.Services.AddCadenceDurableSigTokenStore<PaymentsDbContext>();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddPaymentsApplicationServices();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ClinicalVisitCompletedConsumer>();
    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Payments",
    "payments",
    "Site and participant payments health probe.",
    "Returns Payments operational status.");

app.MapPaymentEndpoints();

await app.RunAsync();

namespace Cadence.Apps.Payments
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
