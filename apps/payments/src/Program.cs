using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.AddCadencePostgresDbContext<PaymentsDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Payments",
    "payments",
    "Site and participant payments health probe.",
    "Returns Payments operational status.");

await app.RunAsync();

namespace Cadence.Apps.Payments
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
