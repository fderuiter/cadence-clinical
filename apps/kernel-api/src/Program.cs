using System.Text.Json;

using Cadence.Database;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Application.Search;
using Cadence.Kernel.Host;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.Persistence.Search;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();

// snake_case on the wire, like Execution, so the generated web clients read the same way.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

// The only host that registers kernel persistence (K-XS-2). In production it connects as the application role and
// its migrations and role grant are applied separately with KernelDatabase; a local stack that connects as the
// schema owner sets Database__MigrateOnStartup to true instead.
string connectionString = CadenceDatabaseExtensions.ResolvePostgresConnectionString(builder.Configuration, builder.Environment);
KernelMigrationHostedService.Register(builder.Services, builder.Configuration, connectionString);
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddHttpContextAccessor();
builder.Services
    .AddKernelPersistence(connectionString)
    .AddKernelScopeGraph()
    .AddKernelNavigation()
    .AddKernelSearchStore()
    .AddKernelSearch()
    .AddKernelAuthorization();
builder.Services.AddScoped<ICurrentActor, ClaimsCurrentActor>();

// Until the authority slice (#4781) ships the evaluator and visibility, every decision is a denial that says so and
// every object is masked (ADR-2471, ADR-2540).
builder.Services.AddSingleton<IAuthorizationEvaluator, UnavailableAuthorizationEvaluator>();
builder.Services.AddSingleton<INodeVisibility, UnavailableNodeVisibility>();

// The kernel tenant comes from the tenant code the gateway signs, through the kernel's tenant directory (ADR-2540).
builder.Services.AddSingleton<IKernelTenantResolver, DirectoryTenantResolver>();

builder.Services.AddHealthChecks()
    .AddCheck("kernel-database", new KernelDatabaseHealthCheck(connectionString), tags: [CadenceTelemetryExtensions.ReadinessTag]);

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapCadenceHealthAndStatusEndpoints(
    "Cadence.Kernel.Host",
    "kernel",
    "Clinical Kernel API health probe.",
    "Returns the Clinical Kernel API's operational status.");
app.MapKernelApi().MapKernelReads();

await app.RunAsync();

namespace Cadence.Kernel.Host
{
    /// <summary>The kernel API host's entry point, visible to test and export hosts.</summary>
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
