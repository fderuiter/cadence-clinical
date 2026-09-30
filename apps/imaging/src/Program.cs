using Cadence.Apps.Imaging.Endpoints;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddScoped<IDicomIngestionService, DicomIngestionService>();
builder.AddCadencePostgresDbContext<ImagingDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Imaging",
    "imaging",
    "Medical Imaging DICOM service health probe.",
    "Returns Medical Imaging operational status.");

app.MapDicomWebEndpoints();
app.MapImagingAdjudicationEndpoints();

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
