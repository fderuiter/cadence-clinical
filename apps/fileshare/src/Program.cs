using Cadence.Apps.Fileshare.Infrastructure.Persistence;
using Cadence.Apps.Fileshare.Workers;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.AddCadencePostgresDbContext<FileshareDbContext>();

builder.Services.Configure<FileshareWorkerOptions>(builder.Configuration.GetSection("FileshareWorkers"));
builder.Services.AddScoped<IMediaTranscoderProcessor, MediaTranscoderProcessor>();
builder.Services.AddScoped<IDocumentConverterProcessor, DocumentConverterProcessor>();
builder.Services.AddHostedService<MediaTranscoderWorker>();
builder.Services.AddHostedService<DocumentConversionWorker>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Fileshare",
    "fileshare",
    "FileShare storage health probe.",
    "Returns FileShare operational status.");

await app.RunAsync();

namespace Cadence.Apps.Fileshare
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
