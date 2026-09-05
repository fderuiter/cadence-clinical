using System;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Serialization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Consumers;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Apps.Execution.Infrastructure.Security;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Polly;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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
// otherwise needed by this slice) since a stateless retry pipeline has no per-request dependencies. The
// repositories themselves (ISubjectRepository/IConsentRepository/ISiteComplianceRepository ->
// SubjectRepository/ConsentRepository/SiteComplianceRepository) and ExecutionDbContext are not yet
// registered in this composition root — that is MIG-SUBJ-024's "final wiring" ticket, which should
// register ConcurrencyTokenIncrementInterceptor (Infrastructure/Persistence/ConcurrencyTokenIncrementInterceptor.cs)
// alongside GxPAuditInterceptor on that same AddDbContext<ExecutionDbContext> call.
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

// MIG-SUBJ-021 (`02_technical_design.md` §5.2, ADR-2234 / Rule 8 / MIG-06): MassTransit plus
// AddEntityFrameworkOutbox<ExecutionDbContext>() close the legacy Redis pub/sub subscriber's documented
// delivery-durability gap (MIG-06) for EConsentCompletedConsumer. The outbox/consumer registration itself is
// transport-agnostic; UsingInMemory is the transport used here because no real broker package (RabbitMQ,
// Azure Service Bus, ...) is declared in Directory.Packages.props yet -- selecting and wiring one is the
// responsibility of whichever future ticket actually publishes EConsentCompletedEvent from upstream eConsent
// completion (out of this slice's scope, the same boundary §5.2 draws around econsent_client.py). Swapping
// UsingInMemory for a real broker later is a one-line change here, not a rework of this registration.
// ExecutionDbContext itself is not yet registered via AddDbContext (that is MIG-SUBJ-024's "final wiring"
// ticket -- see ExecutionDbContext's own remarks and SaveChangesResiliencePipelineFactory's registration
// above), so this consumer cannot actually run end-to-end in this composition root until that lands; this
// ticket's own test coverage exercises the consumer via MassTransit.Testing.ITestHarness against fakes,
// per `02_technical_design.md` §6.2, independent of Program.cs's own runtime wiring.
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

// SystemAuditContextAccessor's fixed "system-pubsub" identity (MIG-SUBJ-017), registered scoped so a fresh
// instance backs each MassTransit-created per-message DI scope ("registered scoped-per-consume via
// MassTransit's own DI scope", `02_technical_design.md` §4.4) -- matching the legacy subscriber's own fixed
// `user_id="system-pubsub"`. This is the only ICurrentAuditContextAccessor registration in this composition
// root today because no HTTP-scoped alternative (HttpCurrentAuditContextAccessor) is wired in yet
// (MIG-SUBJ-018/019/023/024 remain outstanding); whichever of those tickets registers
// HttpCurrentAuditContextAccessor for the HTTP request pipeline owns resolving the resulting
// two-implementations-one-interface question (e.g. a composite accessor, or a per-ambient-scope choice made
// where ExecutionDbContext's own AddDbContext callback wires GxPAuditInterceptor) -- it is not re-litigated
// here.
builder.Services.AddScoped<ICurrentAuditContextAccessor, SystemAuditContextAccessor>();

var app = builder.Build();

// Global fallback for any exception that escapes an endpoint handler unhandled: writes a generic RFC 9457
// 500 via the ProblemDetails service registered above. Deliberately generic — this must never special-case
// down to 400 (that was raise site #19's bug) — only a genuine `DomainError`, routed explicitly through
// `ToProblemResult()` at its own raise site, ever produces a non-500 problem response.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Execution", engine = "EDC" }))
    .WithName("ExecutionHealthCheck")
    .WithSummary("Execution engine health probe.");

var api = app.MapGroup("/api/v1/execution");
api.MapGet("/subjects", () => TypedResults.Ok(Array.Empty<string>()))
    .WithName("ListSubjects")
    .WithSummary("Retrieves subjects projected under the active protocol version.");

await app.RunAsync();
