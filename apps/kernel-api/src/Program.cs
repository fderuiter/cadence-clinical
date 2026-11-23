using System.Text.Json;

using Cadence.Database;
using Cadence.Ddf;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Activity;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Application.Search;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Application.Standards;
using Cadence.Kernel.Application.Validation;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Host;
using Cadence.Kernel.Host.Onboarding;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Activity;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.Persistence.Search;
using Cadence.Kernel.Persistence.Simulation;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Modules.Execution;
using Cadence.Modules.Intelligence;
using Cadence.Modules.Intelligence.Mcp;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Rules;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();

// snake_case on the wire, like Execution, so the generated web clients read the same way.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;

    // A client may send a typed value's "type" member after its other members (ADR-2623).
    options.SerializerOptions.AllowOutOfOrderMetadataProperties = true;
});

// The only host that registers kernel persistence (K-XS-2). In production it connects as the application role and
// its migrations and role grant are applied separately with KernelDatabase; a local stack that connects as the
// schema owner sets Database__MigrateOnStartup to true instead.
string connectionString = CadenceDatabaseExtensions.ResolvePostgresConnectionString(builder.Configuration, builder.Environment);
KernelMigrationHostedService.Register(builder.Services, builder.Configuration, connectionString);

// A local stack's sign-ins name a tenant code (cadence-local in the local realm, ADR-2514); in Development the host can
// provision that tenant on start, as a platform operator it holds (ADR-2542). Self-service trial onboarding uses a
// dedicated host-scoped service account across environments so authenticated OIDC callers without a tenant can bootstrap
// an isolated trial workspace through the command pipeline.
string[] platformOperators =
[
    .. DevelopmentTenantBootstrap.Register(builder.Services, builder.Configuration, builder.Environment),
    KernelAuthorityOptions.TrialOnboardingOperator.Id,
    KernelAuthorityOptions.CommercialBillingOperator.Id,
];
string? quoteSigningSecret = builder.Configuration["CommercialQuote:SigningSecret"]
    ?? builder.Configuration["Security:GatewaySecret"]
    ?? builder.Configuration["GATEWAY_SECRET"];
builder.Services.AddSingleton(new CommercialQuoteOptions(quoteSigningSecret));
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddHttpContextAccessor();
builder.Services
    .AddKernelPersistence(connectionString)
    .AddKernelScopeGraph()
    .AddKernelNavigation()
    .AddKernelSearchStore()
    .AddKernelSearch()
    .AddKernelAuthorization()

    // Decisions come from the caller's grants, roles and delegations (ADR-2455). Platform operators are host-scoped
    // service accounts (development bootstrap when configured, plus self-service trial onboarding); callers never act as them.
    .AddKernelAuthorityEvaluator(platformOperators)

    // The activity stream (ADR-2485). This is the one host with kernel persistence, so it also runs the projector
    // that copies committed events into the stream (ADR-2540 §4.7).
    .AddKernelActivityProjection()
    .AddKernelActivityStream()

    // Simulation engine and persistence inputs (ADR-2480)
    .AddKernelSimulationInputs()
    .AddKernelSimulation()

    // Standards registry, validation findings ledger, and rule evaluation (spec §6.3, §10, K-RULE-3, K-FIND-1)
    .AddKernelStandardsPersistence()
    .AddKernelStandards()
    .AddKernelValidationPersistence()
    .AddKernelValidation()
    .AddKernelWorkQueues()
    .AddCadenceRules()
    .AddCadenceCollectionValidation()

    // Writes: the kernel command pipeline (ADR-2449). Observation reads and writes are the first module writes it serves
    // (ADR-2620); every write is authorized by the authority evaluator above and commits state, audit and outbox together.
    .AddKernelCommandPipeline()

    // After commit (ADR-2601): the outbox relay delivers committed events to the consumers registered in this host,
    // and the audit chains are verified and sealed. Each job takes a PostgreSQL leader lock per pass, so with several
    // replicas one runs it at a time.
    .AddKernelInProcessEvents()
    .AddKernelBackgroundJobs();
builder.Services.AddScoped<ServiceActorScope>();
builder.Services.AddScoped<ICurrentActor, ClaimsCurrentActor>();
builder.Services.AddSingleton<IKernelChangeReason, SignedChangeReason>();
builder.Services.AddScoped<IKernelReadAccess, KernelReadAccess>();
builder.Services.AddSingleton<IProtocolDesignImporter, ProtocolDesignImporterService>();

// A reader sees what its grants in effect cover (ADR-2457) and what treatment blinding permits (K-AUTH-4); everything
// else is masked in place (ADR-2471). The IdentityVault (#4782) is not decided here yet.
builder.Services.AddKernelGrantVisibility();

// Modules in this host (ADR-2601). The Intelligence module is the governed AI agent runtime (ADR-2630): agents read and
// propose, a person approves, and AI is never required for clinical work (K-AI-8).
builder.Services.AddCadenceIntelligenceModule(builder.Configuration, builder.Environment, connectionString);

// The Execution module: eCRF forms over kernel form instances (ADR-2622).
builder.Services.AddCadenceExecutionModule(connectionString);

// The agents' read tools over the kernel reads above, and the MCP server that serves them to outside agents (ADR-2633).
builder.Services.AddIntelligenceTools().AddIntelligenceMcp();

// The kernel tenant comes from the tenant code the gateway signs, through the kernel's tenant directory (ADR-2540).
builder.Services.AddSingleton<IKernelTenantResolver, DirectoryTenantResolver>();
builder.Services.AddKeycloakOnboarding(builder.Configuration);
builder.Services.AddScoped<TrialOnboardingService>();

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
app.MapKernelApi()
    .MapKernelReads()
    .MapKernelStudyImports()
    .MapKernelProtocolGovernance()
    .MapKernelEffectivity()
    .MapKernelObservations()
    .MapKernelExecution()
    .MapKernelLocks()
    .MapKernelProposals()
    .MapKernelValidation()
    .MapKernelWorkQueues()
    .MapKernelEntitlements()
    .MapTrialOnboarding();
app.MapIntelligenceModule();
app.MapExecutionModule();
app.MapIntelligenceMcp();

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
