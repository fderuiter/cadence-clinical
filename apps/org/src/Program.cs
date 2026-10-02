using Cadence.Apps.Org.Endpoints;
using Cadence.Apps.Org.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.AddCadencePostgresDbContext<OrgDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Org",
    "org",
    "Organization directory health probe.",
    "Returns Organization directory operational status.");
app.MapOnboardingPreferencesEndpoints();

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
