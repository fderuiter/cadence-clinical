using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using MassTransit;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<EconsentDbContext>();

builder.Services.AddMassTransit(x =>
{
    x.AddCadenceTransactionalOutbox<EconsentDbContext>();
    x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
});

var app = builder.Build();
app.UseCadenceWebDefaults();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Econsent",
    "econsent",
    "eConsent service health probe.",
    "Returns eConsent operational status.");

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
