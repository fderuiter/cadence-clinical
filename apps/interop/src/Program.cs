using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<InteropDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Interop",
    "interop",
    "Interoperability FHIR & CDISC connector health probe.",
    "Returns Interoperability operational status.");

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
