using System;

using Cadence.Apps.Execution.Application.Serialization;

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

var app = builder.Build();

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
