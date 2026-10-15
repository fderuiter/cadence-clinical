using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.AddCadencePostgresDbContext<KnowledgeDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Knowledge",
    "knowledge",
    "Knowledge base and study training health probe.",
    "Returns Knowledge base operational status.");

app.MapTrainingEndpoints();
app.MapRweEndpoints();

await app.RunAsync();

namespace Cadence.Apps.Knowledge
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
