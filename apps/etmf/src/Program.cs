using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<EtmfDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Etmf",
    "etmf",
    "eTMF operations health probe.",
    "Returns eTMF operational status.");

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
