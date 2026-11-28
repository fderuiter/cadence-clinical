using System;

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

builder.Services.AddOptions<FileshareWorkerOptions>()
    .Bind(builder.Configuration.GetSection(FileshareWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => options.TranscodePollInterval > TimeSpan.Zero,
        "FileshareWorkers:TranscodePollInterval must be greater than zero.")
    .Validate(
        options => options.ConversionPollInterval > TimeSpan.Zero,
        "FileshareWorkers:ConversionPollInterval must be greater than zero.")
    .Validate(
        options => options.InitialBackoff > TimeSpan.Zero,
        "FileshareWorkers:InitialBackoff must be greater than zero.")
    .ValidateOnStart();
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
