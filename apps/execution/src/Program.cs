using System;

using Cadence.Apps.Execution.Application.Serialization;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Shared snake_case wire convention (`subject_id`, `study_id`, ...) for every Application-layer
// response/request DTO in this service, registered once here per MIG-SUBJ-008 (`02_technical_design.md`
// §2.6). ExecutionJsonSerialization.Configure is itself idempotent, so this stays additive/safe even if
// another slice's own ConfigureHttpJsonOptions call also runs it on the same options instance.
builder.Services.ConfigureHttpJsonOptions(options => ExecutionJsonSerialization.Configure(options.SerializerOptions));

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
