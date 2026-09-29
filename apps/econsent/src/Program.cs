using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<EconsentDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Econsent",
    "econsent",
    "eConsent service health probe.",
    "Returns eConsent operational status.");

await app.RunAsync();

namespace Cadence.Apps.Econsent
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
