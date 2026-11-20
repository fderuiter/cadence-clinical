using System;
using System.Linq;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Database;
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
builder.Services.AddHttpClient(CanonicalObservationForwarder.HttpClientName);
builder.Services.AddSingleton<ICanonicalObservationForwarder, CanonicalObservationForwarder>();
builder.Services.AddHttpClient(ActivationBindingResolver.HttpClientName);
builder.Services.AddSingleton<IActivationBindingResolver, ActivationBindingResolver>();
builder.AddCadencePostgresDbContext<InteropDbContext>();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);

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
app.MapEproEndpoints();

if (args.Contains("--seed"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
    var clock = scope.ServiceProvider.GetRequiredService<IClock>();
    await InteropDbSeeder.SeedAsync(dbContext, clock);
    Console.WriteLine("[SUCCESS] InteropDbContext seeded successfully.");
    return;
}

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
