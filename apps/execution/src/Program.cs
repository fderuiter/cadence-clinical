using System;

using Cadence.Apps.Execution.Application.Abstractions;
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
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Database;

using FluentValidation;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Polly;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
    {
        Indented = false
    };
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
    });

builder.Services.AddOpenApi();

// Shared snake_case wire convention (`subject_id`, `study_id`, ...) for every Application-layer
// response/request DTO in this service, registered once here per MIG-SUBJ-008 (`02_technical_design.md`
// §2.6). ExecutionJsonSerialization.Configure is itself idempotent, so this stays additive/safe even if
// another slice's own ConfigureHttpJsonOptions call also runs it on the same options instance.
builder.Services.ConfigureHttpJsonOptions(options => ExecutionJsonSerialization.Configure(options.SerializerOptions));

// MIG-SUBJ-013 (`02_technical_design.md` §4.2): the shared DbUpdateConcurrencyException retry pipeline
// every ExecutionRepositoryBase-derived repository's SaveChangesAsync wraps around
// ExecutionDbContext.SaveChangesAsync — registered once here so all three repositories consume the SAME
// pipeline instance rather than each building its own. Registered as a singleton ResiliencePipeline (not
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

// IHttpContextAccessor backs HttpCurrentAuditContextAccessor below (Application/Abstractions read the
// ambient HTTP request's "sub" claim / X-Change-Reason header through it). ASP.NET Core does not register
// this by default -- omitting this call would make every HTTP-scoped ICurrentAuditContextAccessor
// resolution above throw at first use, not at startup (IHttpContextAccessor's own constructor has no
// dependencies), so this is exactly the kind of gap this ticket's "boundary check: all DI registrations
// resolve successfully at startup" AC exists to catch structurally rather than by manual audit.
builder.Services.AddHttpContextAccessor();

// Rule 7 demographics encryption key (AesGcmDemographicsEncryptionService, MIG-SUBJ-015): bound from
// configuration, never a literal in source -- closing the exact hardcoded-Fernet-key trap `01_spec.md` §3
// names. No "DemographicsEncryption" configuration section exists yet anywhere in this repo (no
// appsettings.json has been added for any .NET service so far, per a solution-wide search performed while
// implementing this ticket) -- binding against configuration regardless, rather than inlining a literal
// default here, is what keeps the "never a literal in source" guarantee intact even before a real deployment
// value is supplied. DemographicsEncryptionOptions.KeyBase64's own `string.Empty` default
// (Infrastructure/Security/DemographicsEncryptionOptions.cs) is what lets
// AesGcmDemographicsEncryptionService's constructor still resolve cleanly with no configuration present
// (Convert.FromBase64String("") is a valid zero-length key, not a throw) -- satisfying this ticket's own
// DI-resolves-at-startup boundary check; an actual Encrypt/Decrypt call with no real key configured would
// fail downstream at the AesGcm layer, which is a deployment-configuration concern outside this ticket's
// scope.
builder.Services.Configure<DemographicsEncryptionOptions>(builder.Configuration.GetSection("DemographicsEncryption"));

// IClock / IDemographicsEncryptionService / IDemographicsNormalizationService -- all three Singleton per
// `02_technical_design.md` §2's DI-lifetime table ("stateless, holds [at most] configured key material").
// DemographicsNormalizationService itself depends on IDemographicsEncryptionService + IClock, both already
// Singleton, so this is not a captive-dependency risk.
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IDemographicsEncryptionService, AesGcmDemographicsEncryptionService>();
builder.Services.AddSingleton<IDemographicsNormalizationService, DemographicsNormalizationService>();
builder.Services.AddSingleton<IUcumNormalizationService, UcumNormalizationService>();
builder.Services.AddSingleton<ILabRangeService, LabRangeService>();
builder.Services.AddSingleton<IOutlierDetectionService, OutlierDetectionService>();
builder.Services.AddScoped<IBatchEvaluationContextLoader, BatchEvaluationContextLoader>();
builder.Services.AddSingleton<IEditCheckEngine, AstEditCheckEngine>();

// ExecutionDbContext (Scoped, the EF Core default `AddDbContext` lifetime) -- the connection string mirrors
// ExecutionDbContextFactory's own design-time convention exactly (same POSTGRES_CONNECTION_STRING
// environment variable, same local-dev-Postgres fallback matching docker/docker-compose.yml's `postgres`
// service), so `dotnet run` against the same docker-compose Postgres container "just works" with no
// additional configuration. Both interceptors flagged forward by MIG-SUBJ-011/013's own doc comments are
// registered together here, exactly as those comments asked: ConcurrencyTokenIncrementInterceptor is
// stateless (safe to share one instance across every scope), while GxPAuditInterceptor's current-user
// delegate must be re-resolved per scope -- achieved by using AddDbContext's `(IServiceProvider, ...)`
// options-action overload, whose `optionsLifetime` defaults to Scoped, so this callback (and therefore the
// `sp.GetRequiredService<ICurrentAuditContextAccessor>()` capture inside it) genuinely re-runs once per
// scope rather than once globally -- the ambient scope's own accessor (HttpCurrentAuditContextAccessor
// inside an HTTP request) is what actually stamps AuditLogEntry.UserId, not a fixed value captured once at
// startup.
string executionDbConnectionString = builder.Configuration["POSTGRES_CONNECTION_STRING"]
    is { Length: > 0 } configuredConnectionString
        ? configuredConnectionString
        : "Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=cadence_password";
var concurrencyTokenIncrementInterceptor = new ConcurrencyTokenIncrementInterceptor();
builder.Services.AddDbContext<ExecutionDbContext>((sp, options) =>
{
    options.UseNpgsql(executionDbConnectionString);
    options.AddInterceptors(
        concurrencyTokenIncrementInterceptor,
        new Cadence.Database.GxPAuditInterceptor(() => sp.GetRequiredService<ICurrentAuditContextAccessor>().UserId));
});

// The repository ports, Scoped to match ExecutionDbContext's own lifetime
builder.Services.AddScoped<ISubjectRepository, SubjectRepository>();
builder.Services.AddScoped<IConsentRepository, ConsentRepository>();
builder.Services.AddScoped<ISiteComplianceRepository, SiteComplianceRepository>();
builder.Services.AddScoped<IObservationRepository, ObservationRepository>();
builder.Services.AddScoped<IFormSubmissionRepository, FormSubmissionRepository>();
builder.Services.AddScoped<IClinicalQueryRepository, ClinicalQueryRepository>();

// IAuditLogWriter (MIG-SUBJ-017's explicit-call audit seam, EfAuditLogWriter's own implementation
// MIG-SUBJ-018/019/020 all call): Scoped, since it appends directly to the scoped ExecutionDbContext's own
// ChangeTracker rather than opening a second DbContext (its own doc comment's "commits inside the same
// SaveChangesAsync call" requirement depends on sharing that exact scoped instance).
builder.Services.AddScoped<IAuditLogWriter, EfAuditLogWriter>();

// ICurrentAuditContextAccessor -> HttpCurrentAuditContextAccessor (Scoped) is now the one ambient-HTTP-scope
// registration for this interface: every current caller that resolves it (SubjectApplicationService,
// SiteComplianceApplicationService) is itself Scoped and only ever constructed inside an HTTP request's own
// DI scope, so this is the registration that actually reaches production traffic.
//
// SystemAuditContextAccessor's fixed "system-pubsub" identity (MIG-SUBJ-017) is registered here too, but
// deliberately as its own concrete type -- not a second ICurrentAuditContextAccessor override scoped to
// "just the MassTransit consumer" -- for a reason verified empirically while implementing this ticket, not
// assumed: MassTransit 8.3.6's container integration (`IConsumerConfigurator<T>`'s full public extension
// surface, cross-checked against the installed package's own XML docs) exposes no supported way to override
// a single shared interface registration on a per-consumer-type basis. The one mechanism that looks
// plausible at a glance -- a factory registration that branches on whether a `MassTransit.ConsumeContext` is
// resolvable in the current scope -- was built and run against this exact package version and found unsound:
// MassTransit registers a non-null `ConsumeContext` placeholder (`MassTransit.Context.MissingConsumeContext`)
// in *every* scope regardless of whether a message is actually being consumed, throwing
// `ConsumeContextNotAvailableException` only when a member is actually touched -- so a bare
// `sp.GetService<ConsumeContext>() is not null` check resolves "true" even for an ordinary HTTP request
// scope, which would have silently handed every HTTP caller the fixed "system-pubsub" identity instead of
// their own. Short of standing up an entire second bus/container (MassTransit's multi-bus feature) for the
// sake of one interface, there is no lower-fragility real alternative. `EConsentCompletedConsumer`
// (MIG-SUBJ-021) already sidesteps this exact question on its own: it reads
// `SystemAuditContextAccessor.SystemUserId`/`SystemChangeReason` as plain `const` values directly rather
// than injecting `ICurrentAuditContextAccessor`, so registering the concrete type here (Singleton -- it is
// immutable, dependency-free, compile-time-constant-backed) is what "SystemAuditContextAccessor registered
// specifically for the MassTransit consumer" now cashes out to: available for direct constructor injection
// by concrete type to any future consumer that prefers that over reading the `const` fields straight, without
// silently reinterpreting the shared HTTP-ambient interface registration underneath any other caller.
builder.Services.AddScoped<ICurrentAuditContextAccessor, HttpCurrentAuditContextAccessor>();
builder.Services.AddSingleton<SystemAuditContextAccessor>();

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

// MIG-SUBJ-021 (`02_technical_design.md` §5.2, ADR-2234 / Rule 8 / MIG-06): MassTransit plus
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

// Global fallback for any exception that escapes an endpoint handler unhandled: writes a generic RFC 9457
// 500 via the ProblemDetails service registered above. Deliberately generic — this must never special-case
// down to 400 (that was raise site #19's bug) — only a genuine `DomainError`, routed explicitly through
// `ToProblemResult()` at its own raise site, ever produces a non-500 problem response.
app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    var activity = System.Diagnostics.Activity.Current;
    if (activity != null)
    {
        var traceParent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
        context.Response.Headers["traceparent"] = traceParent;
    }
    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Execution", engine = "EDC" }))
    .WithName("ExecutionHealthCheck")
    .WithSummary("Execution engine health probe.");

// Route-group registrations
app.MapSubjectEndpoints();
app.MapSiteComplianceEndpoints();
app.MapObservationEndpoints();
app.MapFormEndpoints();
app.MapQueryEndpoints();

await app.RunAsync();

// MIG-SUBJ-023: the standard, minimal ASP.NET Core idiom making the implicit top-level-statement `Program`
// class (`internal` by default) visible to `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>`
// in `tests/Execution/Endpoints/SubjectEndpointsRoutingTests.cs` and, as of MIG-SUBJ-024,
// `tests/Execution/Endpoints/SiteComplianceEndpointsRoutingTests.cs` -- pure test-visibility plumbing, not
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
