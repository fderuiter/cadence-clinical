using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using MassTransit;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<SafetyDbContext>();

builder.Services.AddMassTransit(x =>
{
    x.AddCadenceTransactionalOutbox<SafetyDbContext>();
    x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
});

var app = builder.Build();
app.UseCadenceWebDefaults();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Safety",
    "safety",
    "Clinical Safety & Pharmacovigilance health probe.",
    "Returns Safety operational status.");

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
