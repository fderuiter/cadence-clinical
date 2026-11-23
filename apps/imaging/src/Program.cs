using Cadence.Apps.Imaging.Endpoints;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Infrastructure.Security;
using Cadence.Apps.Imaging.Services;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddCadenceDurableSigTokenStore<ImagingDbContext>();
// Certificates and manifests are signed, and UIDs pseudonymized, with CADENCE_IMAGING_DEID_SIGNING_KEY and
// CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY, both required and validated at startup (#5399).
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddImagingDeidentification();
builder.Services.AddScoped<IDicomIngestionService, DicomIngestionService>();
builder.Services.AddScoped<IImagingSegmentationService, ImagingSegmentationService>();
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
app.MapImagingSegmentationEndpoints();

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
