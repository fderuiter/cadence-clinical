using Cadence.Apps.Quality.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<QualityDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Quality",
    "quality",
    "Quality management health probe.",
    "Returns Quality management operational status.");

await app.RunAsync();

namespace Cadence.Apps.Quality
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
