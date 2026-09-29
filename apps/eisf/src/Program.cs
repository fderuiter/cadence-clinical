using Cadence.Apps.Eisf.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<EisfDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
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
