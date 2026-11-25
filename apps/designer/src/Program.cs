using System;
using System.Collections.Generic;

using Cadence.Apps.Designer;
using Cadence.Apps.Designer.Endpoints;
using Cadence.Ddf;
using Cadence.Rules;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();
builder.Services.AddCadenceSecurity();
builder.Services.AddCadenceRules();

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseCadenceSecurity();
app.MapRuleBatchEndpoints();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Designer", primaryDatastore = "PostgreSQL", compiler = "Cadence.Ddf" }))
    .WithName("DesignerHealthCheck")
    .WithSummary("Protocol designer metadata health probe.")
    .AllowAnonymous();

var api = app.MapGroup("/api/v1/studies");
api.MapGet("/", () => TypedResults.Ok(Array.Empty<string>()))
    .WithName("ListStudies")
    .WithSummary("Lists active CDISC USDM study protocol definitions.")
    .WithDescription("Scaffold placeholder returning an empty study list; persistent CDISC USDM study governance is served by the Clinical Kernel (/api/v1/kernel/studies).")
    .RequirePermission("study_design:read").RequireCallerStudyScope();

var protocols = app.MapGroup("/api/v1/protocols");
protocols.MapPost("/compile", Results<Ok<DDFCompilationResult>, ProblemHttpResult> (CompileProtocolRequest request, HttpContext httpContext) =>
{
    if (!string.IsNullOrWhiteSpace(request.StudyId) && !ClinicalScopeGuard.CanAccessStudy(httpContext, request.StudyId))
    {
        return ClinicalScopeGuard.OutsideStudy(ClinicalScopeGuard.StudyAccessDeniedDetail);
    }

    try
    {
        return TypedResults.Ok(UsdmCompiler.CompileUsdm(request.UsdmJson, request.StudyId, request.VersionTag));
    }
    catch (UsdmFormatException ex)
    {
        // Text that is not JSON, or JSON that is not shaped like a USDM study, is the caller's error (#5040).
        return TypedResults.Problem(
            title: "The USDM document cannot be read.",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
    catch (DdfCompilationException ex)
    {
        // A rule that does not parse, names an unknown item or makes a skip-logic cycle, or a duplicated item.
        return TypedResults.Problem(
            title: "The study cannot be compiled.",
            detail: ex.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?> { ["problems"] = ex.Problems });
    }
    catch (AccessibilityValidationException ex)
    {
        // The compiler never hands out a form that failed its accessibility check; the caller gets what failed.
        return TypedResults.Problem(
            title: "The compiled form failed its accessibility check.",
            detail: ex.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?>
            {
                ["violations"] = ex.Report.Violations,
                ["layoutErrors"] = ex.Report.LayoutErrors,
            });
    }
})
.WithName("CompileProtocol")
.WithSummary("Compiles CDISC USDM protocol JSON into DDF IR and EDC runtime schemas.")
.WithDescription("Stateless CDISC USDM compiler endpoint (Scaffold service maturity); compiles supplied USDM JSON in memory without persisting study definitions.")
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status403Forbidden)
.ProducesProblem(StatusCodes.Status422UnprocessableEntity)
// Compiling a protocol is study design work: sponsor designers and system administrators (#4958, ADR-2510).
.RequirePermission(DesignerPermissions.CompileProtocol).RequireCallerStudyScope();

await app.RunAsync();

namespace Cadence.Apps.Designer
{
    public record CompileProtocolRequest(string UsdmJson, string? StudyId = null, string? VersionTag = null);

    /// <summary>The permissions Designer's routes require, from the permission matrix.</summary>
    public static class DesignerPermissions
    {
        /// <summary>Compiling a protocol is study design work.</summary>
        public const string CompileProtocol = "study_design:create";
    }

    public partial class Program
    {
        protected Program()
        {
        }
    }
}
