using System;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Cec;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Application.Serialization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure;
using Cadence.Apps.Execution.Infrastructure.Consumers;
using Cadence.Apps.Execution.Infrastructure.EditChecks;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Apps.Execution.Infrastructure.OpenApi;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Telemetry;

using FluentValidation;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Polly;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();

// Shared snake_case wire convention (`subject_id`, `study_id`, ...) for every Application-layer
// response/request DTO in this service, registered once here per MIG-SUBJ-008 (`02_technical_design.md`
// §2.6). ExecutionJsonSerialization.Configure is itself idempotent, so this stays additive/safe even if
// another slice's own ConfigureHttpJsonOptions call also runs it on the same options instance.
builder.Services.ConfigureHttpJsonOptions(options => ExecutionJsonSerialization.Configure(options.SerializerOptions));
// Describe the value objects and statuses those converters write, so the OpenAPI document matches the wire.
builder.Services.ConfigureAll<OpenApiOptions>(options => options.AddSchemaTransformer<ExecutionWireTypesOpenApiTransformer>());

// MIG-SUBJ-013 (`02_technical_design.md` §4.2): the shared transient-failure retry pipeline every
// ExecutionRepositoryBase-derived repository's SaveChangesAsync wraps around ExecutionDbContext.SaveChangesAsync.
// Concurrency conflicts are not retried; they surface as 409 through ConcurrencyConflictExceptionHandler (#4766).
// Registered once here so every repository consumes the SAME pipeline instance rather than each building its own. Registered as a singleton ResiliencePipeline (not
// via the Polly.Extensions `AddResiliencePipeline` DI helper, which is a separate NuGet package not
// otherwise needed by this slice) since a stateless retry pipeline has no per-request dependencies.
builder.Services.AddSingleton(SaveChangesResiliencePipelineFactory.Create());

// RFC 9457 ProblemDetails for every domain/validation/unhandled-exception response (MIG-SUBJ-010,
// `02_technical_design.md` §3.4). `DomainError.ToProblemResult()` (Domain/Subjects/DomainErrorExtensions.cs)
// is the primary mechanism call sites use directly; `CustomizeProblemDetails` here is only the
// defense-in-depth safety net for `01_spec.md` §5's "no global not-found handler exists in the legacy
// system" gap — it re-derives the canonical status from `DomainErrorHttpMapping` for any response that
// already carries a recognized `code` extension (including ones `ToProblemResult()` itself produced), and
// leaves anything else — most importantly a bare, non-`DomainError` exception's generic 500 — untouched, so
// raise site #19's blanket "catch-all downgrades to 400" bug is never reproduced.
builder.Services.AddExceptionHandler<ConcurrencyConflictExceptionHandler>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        DomainErrorHttpMapping.ApplyKnownCodeStatus(context.ProblemDetails);

        // `ProblemHttpResult`/the exception-handler middleware already stamped `Response.StatusCode` from
        // the status computed *before* this callback ran, so a correction made just above must be mirrored
        // onto the live response explicitly — otherwise the wire status and the JSON body's `status` field
        // could disagree.
        if (context.ProblemDetails.Status is int correctedStatus)
        {
            context.HttpContext.Response.StatusCode = correctedStatus;
        }
    };
});

// ================================================================================================
// MIG-SUBJ-024 -- final composition-root wiring. Everything below this point registers a port/service
// built by an earlier ticket (MIG-SUBJ-011 through -022) that had no production DI registration yet; see
// `03_tickets.md`'s own dependency list and each type's doc comment (cross-referenced below) for why each
// one was deliberately left unregistered until this ticket.
// ================================================================================================

// IHttpContextAccessor backs AmbientAuditContextAccessor below (Application/Abstractions read the
// ambient HTTP request's "sub" claim / X-Change-Reason header through it). ASP.NET Core does not register
// this by default -- omitting this call would make every HTTP-scoped ICurrentAuditContextAccessor
// resolution above throw at first use, not at startup (IHttpContextAccessor's own constructor has no
// dependencies), so this is exactly the kind of gap this ticket's "boundary check: all DI registrations
// resolve successfully at startup" AC exists to catch structurally rather than by manual audit.
builder.Services.AddHttpContextAccessor();

// Rule 7 demographics encryption key (AesGcmDemographicsEncryptionService, MIG-SUBJ-015): bound from
// configuration, never a literal in source (the hardcoded-Fernet-key trap `01_spec.md` §3 names). There is
// no default: the host refuses to start without a valid AES key, so a missing key fails at boot, not on the
// first subject registration.
builder.Services.AddOptions<DemographicsEncryptionOptions>()
    .Bind(builder.Configuration.GetSection(DemographicsEncryptionOptions.SectionName))
    .Validate(options => DemographicsEncryptionOptions.IsValidKey(options.KeyBase64), DemographicsEncryptionOptions.InvalidKeyMessage)
    .ValidateOnStart();

// IClock / IDemographicsEncryptionService / IDemographicsNormalizationService -- all three Singleton per
// `02_technical_design.md` §2's DI-lifetime table ("stateless, holds [at most] configured key material").
// DemographicsNormalizationService itself depends on IDemographicsEncryptionService + IClock, both already
// Singleton, so this is not a captive-dependency risk.
// IClock/SystemClock are the shared kernel ones (Cadence.Kernel.Time, #4784 K-TIME-3).
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddSingleton<IDemographicsEncryptionService, AesGcmDemographicsEncryptionService>();
builder.Services.AddSingleton<IDemographicsNormalizationService, DemographicsNormalizationService>();
builder.Services.AddSingleton<IUcumNormalizationService, UcumNormalizationService>();
builder.Services.AddSingleton<ILabRangeService, LabRangeService>();
builder.Services.AddSingleton<IOutlierDetectionService, OutlierDetectionService>();
builder.Services.AddScoped<IBatchEvaluationContextLoader, BatchEvaluationContextLoader>();
builder.Services.AddSingleton<IEditCheckEngine, AstEditCheckEngine>();

// ExecutionDbContext (Scoped, the EF Core default `AddDbContext` lifetime) -- the connection string mirrors
// ExecutionDbContextFactory's own design-time convention exactly (same POSTGRES_CONNECTION_STRING
// environment variable, set by docker/docker-compose.yml's services and by the Aspire AppHost). Both
// interceptors flagged forward by MIG-SUBJ-011/013's own doc comments are registered together here, exactly as those comments asked: ConcurrencyTokenIncrementInterceptor is
// stateless (safe to share one instance across every scope), while GxPAuditInterceptor's current-user
// delegate must be re-resolved per scope -- achieved by using AddDbContext's `(IServiceProvider, ...)`
// options-action overload, whose `optionsLifetime` defaults to Scoped, so this callback (and therefore the
// `sp.GetRequiredService<ICurrentAuditContextAccessor>()` capture inside it) genuinely re-runs once per
// scope rather than once globally -- the ambient scope's own accessor (AmbientAuditContextAccessor: the
// caller inside an HTTP request, the declared service account inside a consumer) is what actually stamps AuditLogEntry.UserId, not a fixed value captured once at
// startup.
//
// The connection string comes from ConnectionStrings:cadence or POSTGRES_CONNECTION_STRING; there is no
// hardcoded fallback in any environment, so a missing value fails at startup naming both keys.
string executionDbConnectionString = CadenceDatabaseExtensions.WithModuleSchema(
    CadenceDatabaseExtensions.ResolvePostgresConnectionString(builder.Configuration, builder.Environment),
    ExecutionDbContextFactory.Schema);
var concurrencyTokenIncrementInterceptor = new ConcurrencyTokenIncrementInterceptor();
builder.Services.AddCadenceDbContext<ExecutionDbContext>((sp, options) =>
{
    options.UseNpgsql(executionDbConnectionString);
    options.AddInterceptors(
        concurrencyTokenIncrementInterceptor,
        new Cadence.Database.GxPAuditInterceptor(
            () => sp.GetRequiredService<ICurrentAuditContextAccessor>().UserId,
            sp.GetRequiredService<IClock>()));
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ExecutionDbContext>(name: nameof(ExecutionDbContext), tags: [CadenceDatabaseExtensions.ReadinessTag]);
builder.Services.AddCadenceMigrations<ExecutionDbContext>(builder.Configuration);

// The repository ports, Scoped to match ExecutionDbContext's own lifetime
builder.Services.AddScoped<ISubjectRepository, SubjectRepository>();
builder.Services.AddScoped<IConsentRepository, ConsentRepository>();
builder.Services.AddScoped<ISiteComplianceRepository, SiteComplianceRepository>();
builder.Services.AddScoped<IObservationRepository, ObservationRepository>();
builder.Services.AddScoped<IFormSubmissionRepository, FormSubmissionRepository>();
builder.Services.AddScoped<IClinicalQueryRepository, ClinicalQueryRepository>();
builder.Services.AddScoped<ICecRepository, CecRepository>();

// IAuditLogWriter (MIG-SUBJ-017's explicit-call audit seam, EfAuditLogWriter's own implementation
// MIG-SUBJ-018/019/020 all call): Scoped, since it appends directly to the scoped ExecutionDbContext's own
// ChangeTracker rather than opening a second DbContext (its own doc comment's "commits inside the same
// SaveChangesAsync call" requirement depends on sharing that exact scoped instance).
builder.Services.AddScoped<IAuditLogWriter, EfAuditLogWriter>();

// One scoped AmbientAuditContextAccessor answers both audit interfaces: it reads the caller's sub claim inside
// a request, and message consumers use it to declare their service account before writing. Both interfaces
// must resolve to the same instance, or the GxPAuditInterceptor above would not see that service account.
// There is no anonymous fallback actor, see issue 4767.
builder.Services.AddScoped<AmbientAuditContextAccessor>();
builder.Services.AddScoped<ICurrentAuditContextAccessor>(sp => sp.GetRequiredService<AmbientAuditContextAccessor>());
builder.Services.AddScoped<IAuditActorScope>(sp => sp.GetRequiredService<AmbientAuditContextAccessor>());

// The two application services (MIG-SUBJ-018/019/020), Scoped -- both take only Scoped/Singleton
// dependencies above (SubjectApplicationService's own trailing `screeningEligibility` constructor parameter
// is optional with a `null` default, and no route in this slice maps to
// EvaluateAndTransitionScreeningAsync, the one method that would ever invoke it -- the built-in DI
// container resolves an unregistered parameter type via its declared default value, so this needs no
// explicit registration; see that constructor's own doc comment for why a real evaluator does not exist in
// this slice yet).
builder.Services.AddScoped<ISubjectApplicationService, SubjectApplicationService>();
builder.Services.AddScoped<ISiteComplianceApplicationService, SiteComplianceApplicationService>();
builder.Services.AddScoped<IObservationApplicationService, ObservationApplicationService>();
builder.Services.AddScoped<IFormSubmissionApplicationService, FormSubmissionApplicationService>();
builder.Services.AddScoped<IClinicalQueryApplicationService, ClinicalQueryApplicationService>();
builder.Services.AddScoped<IAutoQueryDispatchService, AutoQueryDispatchService>();
builder.Services.AddScoped<ICecApplicationService, CecApplicationService>();

// Every FluentValidation AbstractValidator<T> in this assembly (MIG-SUBJ-009: SubjectCreateRequestValidator,
// SubjectConsentRequestValidator, SubjectStateUpdateRequestValidator, EtmfCompletenessWebhookPayloadValidator
// -- verified by a solution-wide search for `AbstractValidator` to be the complete set, not assumed from the
// ticket text's paraphrase) via FluentValidation.DependencyInjectionExtensions'
// AddValidatorsFromAssemblyContaining<T>, rather than four individual AddScoped<IValidator<T>, TValidator>
// calls: this assembly-scan approach is what the package is for, keeps this list from silently drifting out
// of sync the next time a slice adds a validator, and every validator here is a stateless, dependency-free
// AbstractValidator<T> so its assigned lifetime has no observable effect. `AddFluentValidationAutoValidation`
// (from the separate, MVC-model-binding-pipeline-only `FluentValidation.AspNetCore` package) is deliberately
// NOT used -- this solution has no MVC pipeline at all (no AddControllers/MapControllers anywhere), only
// Minimal API endpoints, and every mutating route in this slice already opts into validation explicitly via
// ValidationFilter<T> (MIG-SUBJ-022), which resolves IValidator<T> from DI itself; auto-validation would
// hook a pipeline this service never runs and would not affect these endpoints at all.
builder.Services.AddValidatorsFromAssemblyContaining<SubjectCreateRequestValidator>();

// MIG-SUBJ-021 (`02_technical_design.md` §5.2, ADR-2428 / Rule 8 / MIG-06): MassTransit plus
// AddEntityFrameworkOutbox<ExecutionDbContext>() close the legacy Redis pub/sub subscriber's documented
// delivery-durability gap (MIG-06) for EConsentCompletedConsumer. The outbox/consumer registration itself is
// transport-agnostic; UsingInMemory is the transport used here because no real broker package (RabbitMQ,
// Azure Service Bus, ...) is declared in Directory.Packages.props yet -- selecting and wiring one is the
// responsibility of whichever future ticket actually publishes EConsentCompletedEvent from upstream eConsent
// completion (out of this slice's scope, the same boundary §5.2 draws around econsent_client.py). Swapping
// UsingInMemory for a real broker later is a one-line change here, not a rework of this registration.
// ExecutionDbContext is now registered (above), so this consumer can run end-to-end in this composition root
// for the first time as of this ticket; per §5.2's "no environment-variable gate" decision, this in-memory
// transport requires no live broker at all (dev, test, or production) -- only a reachable ExecutionDbContext
// connection is ever needed for the outbox itself to actually deliver.
builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<ExecutionDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.AddConsumer<EConsentCompletedConsumer>();

    x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
});

var app = builder.Build();

app.UseCadenceWebDefaults();
app.UseCadenceSecurity();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Execution", engine = "EDC" }))
    .WithName("ExecutionHealthCheck")
    .WithSummary("Execution engine health probe.")
    .AllowAnonymous();

// Route-group registrations
app.MapSubjectEndpoints();
app.MapSiteComplianceEndpoints();
app.MapObservationEndpoints();
app.MapFormEndpoints();
app.MapQueryEndpoints();
app.MapCecEndpoints();

if (args.Contains("--seed"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
    await ExecutionDbSeeder.SeedAsync(dbContext);
    Console.WriteLine("[SUCCESS] ExecutionDbContext seeded successfully.");
    return;
}

await app.RunAsync();

// MIG-SUBJ-023: the standard, minimal ASP.NET Core idiom making the implicit top-level-statement `Program`
// class (`internal` by default) visible to `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>`
// in `tests/Cadence.UnitTests/Execution/Endpoints/SubjectEndpointsRoutingTests.cs` and, as of MIG-SUBJ-024,
// `tests/Cadence.UnitTests/Execution/Endpoints/SiteComplianceEndpointsRoutingTests.cs` -- pure test-visibility plumbing, not
// composition-root wiring. MIG-SUBJ-025's future `ExecutionApiFactory` needs this exact same marker too, so
// it stays exactly as MIG-SUBJ-023 first added it.
namespace Cadence.Apps.Execution
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
