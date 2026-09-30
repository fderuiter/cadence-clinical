using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Security.DependencyInjection;
using Cadence.Security.FieldProtection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
// Signer e-mail addresses are encrypted at rest; FIELD_ENCRYPTION_KEY is required and validated at startup (#4770).
builder.Services.AddCadenceFieldEncryption();
builder.AddCadencePostgresDbContext<EconsentDbContext>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
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
