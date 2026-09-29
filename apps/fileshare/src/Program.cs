using Cadence.Apps.Fileshare.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<FileshareDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
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
