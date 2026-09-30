using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Security.FieldProtection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
// Processor references are encrypted at rest; FIELD_ENCRYPTION_KEY is required and validated at startup (#4770).
builder.Services.AddCadenceFieldEncryption();
builder.AddCadencePostgresDbContext<PaymentsDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
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
