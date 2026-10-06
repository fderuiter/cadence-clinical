using Cadence.Apps.Quality.Application;
using Cadence.Apps.Quality.Endpoints;
using Cadence.Apps.Quality.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<QualityDbContext>();
builder.Services.AddSingleton<SiteRiskHeatmapService>();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Quality",
    "quality",
    "Quality management health probe.",
    "Returns Quality management operational status.");

app.MapSiteRiskHeatmapEndpoints();

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
