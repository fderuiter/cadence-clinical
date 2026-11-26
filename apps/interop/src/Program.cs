using Cadence.Apps.Interop.Application;
using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Security.FieldProtection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
// Device tokens are stored only as keyed hashes; FIELD_LOOKUP_HASH_KEY is required and validated at startup (#4770).
builder.Services.AddCadenceLookupHashing();
builder.Services.AddSingleton<DeviceTokenHasher>();
// SCE lock tokens and run signatures are keyed by CADENCE_SCE_SIGNING_KEY, required and validated at startup (#5382).
builder.Services.AddSceSigning();
builder.Services.AddSingleton<ILockfileValidatorService, LockfileValidatorService>();
builder.Services.AddSingleton<ISceDockerEngineDriver, SceDockerEngineDriver>();
// SMART client assertions are signed with server-side keys named per endpoint, never keys from a request (#5436).
builder.Services.AddSingleton<ISmartSigningKeys, ConfigurationSmartSigningKeys>();
builder.AddCadencePostgresDbContext<InteropDbContext>();
builder.Services.AddCadenceDurableSigTokenStore<InteropDbContext>();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddScoped<IInteropUnitOfWork, InteropUnitOfWork>();
builder.Services.AddScoped<IEsourceIngestionService, EsourceIngestionService>();
builder.Services.AddScoped<IEsourceReconciliationService, EsourceReconciliationService>();
builder.Services.AddScoped<ISceProgramLockService, SceProgramLockService>();
builder.Services.AddScoped<ISceExecutionService, SceExecutionService>();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Interop",
    "interop",
    "Interoperability FHIR & CDISC connector health probe.",
    "Returns Interoperability operational status.");

app.MapEsourceEndpoints();
app.MapSceEndpoints();

await app.RunAsync();

namespace Cadence.Apps.Interop
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
