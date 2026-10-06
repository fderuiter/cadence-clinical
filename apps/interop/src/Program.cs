using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Background;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Security.FieldProtection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
// Device tokens are stored only as keyed hashes; FIELD_LOOKUP_HASH_KEY is required and validated at startup (#4770).
builder.Services.AddCadenceLookupHashing();
builder.Services.AddSingleton<DeviceTokenHasher>();
builder.AddCadencePostgresDbContext<InteropDbContext>();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddHostedService<TelemetryWindowingBackgroundService>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Interop",
    "interop",
    "Interoperability FHIR & CDISC connector health probe.",
    "Returns Interoperability operational status.");

app.MapEsourceEndpoints();
app.MapTelemetryEndpoints();

await app.RunAsync();

namespace Cadence.Apps.Interop
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
