using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<ImagingDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Imaging",
    "imaging",
    "Medical Imaging DICOM service health probe.",
    "Returns Medical Imaging operational status.");

await app.RunAsync();

namespace Cadence.Apps.Imaging
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
