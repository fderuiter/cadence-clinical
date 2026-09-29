using Cadence.Apps.Eisf.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<EisfDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Eisf",
    "eisf",
    "eISF operations health probe.",
    "Returns eISF operational status.");

await app.RunAsync();

namespace Cadence.Apps.Eisf
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
