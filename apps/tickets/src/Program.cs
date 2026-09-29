using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<TicketsDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Tickets",
    "tickets",
    "Support tickets health probe.",
    "Returns Tickets operational status.");

await app.RunAsync();

namespace Cadence.Apps.Tickets
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
