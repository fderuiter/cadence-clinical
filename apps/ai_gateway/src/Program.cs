using Cadence.Apps.AiGateway.Endpoints;
using Cadence.Apps.AiGateway.Services;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();

builder.Services.AddSingleton<IVectorEmbeddingService, VectorEmbeddingService>();
builder.Services.AddSingleton<IVectorCacheService, VectorCacheService>();
builder.Services.AddSingleton<IMedicalCodingSuggestService, MedicalCodingSuggestService>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.AiGateway",
    "ai",
    "AI Gateway medical coding proxy health probe.",
    "Returns AI Gateway operational status.");

app.MapMedicalCodingEndpoints();

await app.RunAsync();

namespace Cadence.Apps.AiGateway
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}

