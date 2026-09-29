using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<KnowledgeDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Knowledge",
    "knowledge",
    "Knowledge base and study training health probe.",
    "Returns Knowledge base operational status.");

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
