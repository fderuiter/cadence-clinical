using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Consumers;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.AddCadencePostgresDbContext<EtmfDbContext>();

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
