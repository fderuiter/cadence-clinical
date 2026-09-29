using Cadence.Apps.Org.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using MassTransit;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<OrgDbContext>();

builder.Services.AddMassTransit(x =>
{
    x.AddCadenceTransactionalOutbox<OrgDbContext>();
    x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
});

var app = builder.Build();
app.UseCadenceWebDefaults();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Org",
    "org",
    "Organization directory health probe.",
    "Returns Organization directory operational status.");

await app.RunAsync();

namespace Cadence.Apps.Org
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
