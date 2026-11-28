using Cadence.Apps.Econsent.Application;
using Cadence.Apps.Econsent.Endpoints;
using Cadence.Apps.Econsent.Infrastructure.Consumers;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.FieldProtection;
using Cadence.Telemetry;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
// Signer e-mail addresses are encrypted at rest; FIELD_ENCRYPTION_KEY is required and validated at startup (#4770).
builder.Services.AddCadenceFieldEncryption();
// Biometric liveness tokens are verified with CADENCE_BIOMETRIC_LIVENESS_KEY, validated at startup (ADR-2660).
builder.Services.AddCadenceBiometricLiveness();
builder.AddCadencePostgresDbContext<EconsentDbContext>();
builder.Services.AddScoped<IEconsentUnitOfWork, EconsentUnitOfWork>();
builder.Services.AddScoped<IEconsentApplicationService, EconsentApplicationService>();
// Consent capture is e-signed; each token is single use across instances and restarts (ADR-2515, #5434).
builder.Services.AddCadenceDurableSigTokenStore<EconsentDbContext>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<KernelScopeLockEconsentConsumer>();
    x.AddEntityFrameworkOutbox<EconsentDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });
    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Apps.Econsent",
    "econsent",
    "eConsent service health probe.",
    "Returns eConsent operational status.");
app.MapEconsentEndpoints();

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
