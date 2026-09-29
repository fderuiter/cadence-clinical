using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<EtmfDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
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
