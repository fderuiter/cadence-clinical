using Cadence.Apps.Knowledge.Application;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
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
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.AddCadencePostgresDbContext<KnowledgeDbContext>();
builder.Services.AddKnowledgeApplicationServices();
// Delegation sign-off is e-signed; each token is single use across instances and restarts (ADR-2515, #5434).
builder.Services.AddCadenceDurableSigTokenStore<KnowledgeDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Knowledge",
    "knowledge",
    "Knowledge base and study training health probe.",
    "Returns Knowledge base operational status.");

app.MapKnowledgeEndpoints();
app.MapTrainingEndpoints();

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
