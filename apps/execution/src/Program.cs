using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

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
