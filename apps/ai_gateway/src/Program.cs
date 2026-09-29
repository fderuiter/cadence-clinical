using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.AiGateway",
    "ai",
    "AI Gateway medical coding proxy health probe.",
    "Returns AI Gateway operational status.");

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
