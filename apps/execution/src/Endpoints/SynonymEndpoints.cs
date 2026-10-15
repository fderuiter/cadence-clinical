using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Coding;
using Cadence.Security;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API route group for Bulk Synonym Dictionary Import and Rule Management.
/// @req:PRD-SYS-001
/// </summary>
public static class SynonymEndpoints
{
    public static RouteGroupBuilder MapSynonymEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/execution/coding/synonyms")
            .WithTags("Medical Coding Synonyms");

        group.MapPost("/validate", ValidateSynonymFileAsync)
            .WithName("ValidateSynonymFile")
            .WithSummary("Pre-flight validation report for bulk synonym dictionary CSV/XLSX file.")
            .DisableAntiforgery();

        group.MapPost("/import", ImportSynonymFileAsync)
            .WithName("ImportSynonymFile")
            .WithSummary("Ingest validated synonym dictionary rules into persistent store with GxP audit lineage.")
            .DisableAntiforgery();

        group.MapGet("/", GetActiveSynonymRulesAsync)
            .WithName("GetActiveSynonymRules")
            .WithSummary("Retrieve active synonym rules for study auto-coding engine.");

        group.MapGet("/jobs", GetSynonymImportJobsAsync)
            .WithName("GetSynonymImportJobs")
            .WithSummary("Retrieve GxP audit ledger of bulk synonym import jobs.");

        group.MapDelete("/{id:guid}", DeactivateSynonymRuleAsync)
            .WithName("DeactivateSynonymRule")
            .WithSummary("Deactivate an active synonym rule.");

        routes.MapGet("/api/v1/execution/coding/autosuggest", GetCodingAutosuggestAsync)
            .WithName("GetCodingAutosuggest")
            .WithSummary("Retrieve real-time semantic medical coding suggestions proxied to AI Gateway.")
            .WithTags("Medical Coding");

        return group;
    }

    public static async Task<IResult> ValidateSynonymFileAsync(
        HttpContext httpContext,
        ISynonymImportService synonymService,
        CancellationToken cancellationToken)
    {
        var caller = httpContext.User.ToAuthenticatedCaller();
        if (!HasImportAuthorization(caller))
        {
            return TypedResults.Problem(
                detail: "Bulk synonym dictionary import requires DATA_MANAGER or SPONSOR_DESIGNER authorization.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!httpContext.Request.HasFormContentType)
        {
            return TypedResults.Problem(
                detail: "Request content type must be multipart/form-data with a 'file' parameter.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file == null || file.Length == 0)
        {
            return TypedResults.Problem(
                detail: "No file was uploaded or file is empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string scope = form["scope"].FirstOrDefault() ?? form["file_scope"].FirstOrDefault() ?? "STUDY";
        string? studyId = form["study_id"].FirstOrDefault() ?? form["studyId"].FirstOrDefault();

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        byte[] bytes = ms.ToArray();

        try
        {
            var report = await synonymService.ValidatePreflightAsync(
                bytes,
                file.FileName,
                scope,
                studyId,
                cancellationToken);

            return TypedResults.Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    public static async Task<IResult> ImportSynonymFileAsync(
        HttpContext httpContext,
        ISynonymImportService synonymService,
        CancellationToken cancellationToken)
    {
        var caller = httpContext.User.ToAuthenticatedCaller();
        if (!HasImportAuthorization(caller))
        {
            return TypedResults.Problem(
                detail: "Bulk synonym dictionary import requires DATA_MANAGER or SPONSOR_DESIGNER authorization.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!httpContext.Request.HasFormContentType)
        {
            return TypedResults.Problem(
                detail: "Request content type must be multipart/form-data.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file == null || file.Length == 0)
        {
            return TypedResults.Problem(
                detail: "No file was uploaded or file is empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string scope = form["scope"].FirstOrDefault() ?? form["file_scope"].FirstOrDefault() ?? "STUDY";
        string? studyId = form["study_id"].FirstOrDefault() ?? form["studyId"].FirstOrDefault();
        string? changeReason = form["reason_for_change"].FirstOrDefault() ?? form["change_reason"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(changeReason) && httpContext.Request.Headers.TryGetValue("X-Change-Reason", out var headerVal))
        {
            changeReason = headerVal.ToString();
        }

        if (string.IsNullOrWhiteSpace(changeReason))
        {
            return TypedResults.Problem(
                detail: "A valid reason for change is required for 21 CFR Part 11 audit compliance.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        byte[] bytes = ms.ToArray();

        try
        {
            string createdBy = caller?.UserId ?? "DATA_MANAGER";
            var result = await synonymService.ImportSynonymRulesAsync(
                bytes,
                file.FileName,
                scope,
                studyId,
                changeReason,
                createdBy,
                cancellationToken);

            return TypedResults.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    public static async Task<IResult> GetActiveSynonymRulesAsync(
        ISynonymImportService synonymService,
        string? study_id,
        string? studyId,
        CancellationToken cancellationToken)
    {
        string? effectiveStudyId = !string.IsNullOrWhiteSpace(study_id) ? study_id : studyId;
        var rules = await synonymService.GetActiveSynonymRulesAsync(effectiveStudyId, cancellationToken);
        return TypedResults.Ok(rules);
    }

    public static async Task<IResult> GetSynonymImportJobsAsync(
        ISynonymImportService synonymService,
        string? study_id,
        string? studyId,
        CancellationToken cancellationToken)
    {
        string? effectiveStudyId = !string.IsNullOrWhiteSpace(study_id) ? study_id : studyId;
        var jobs = await synonymService.GetImportJobsAsync(effectiveStudyId, cancellationToken);
        return TypedResults.Ok(jobs);
    }

    public static async Task<IResult> DeactivateSynonymRuleAsync(
        Guid id,
        HttpContext httpContext,
        ISynonymImportService synonymService,
        CancellationToken cancellationToken)
    {
        var caller = httpContext.User.ToAuthenticatedCaller();
        if (!HasImportAuthorization(caller))
        {
            return TypedResults.Problem(
                detail: "Deactivating synonym rules requires DATA_MANAGER or SPONSOR_DESIGNER authorization.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        string? changeReason = httpContext.Request.Query["reason_for_change"].FirstOrDefault() ??
                               httpContext.Request.Query["change_reason"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(changeReason) && httpContext.Request.Headers.TryGetValue("X-Change-Reason", out var headerVal))
        {
            changeReason = headerVal.ToString();
        }

        if (string.IsNullOrWhiteSpace(changeReason))
        {
            changeReason = "Deactivated synonym rule in workbench.";
        }

        bool deactivated = await synonymService.DeactivateRuleAsync(id, changeReason, cancellationToken);
        if (!deactivated)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new { message = "Synonym rule deactivated successfully.", id });
    }

    private static bool HasImportAuthorization(AuthenticatedCaller? caller)
    {
        if (caller == null) return false;
        return caller.Roles.Any(r =>
            r.Equals("DATA_MANAGER", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("SPONSOR_DESIGNER", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<IResult> GetCodingAutosuggestAsync(
        ICodingAutosuggestService autosuggestService,
        string? query,
        string? q,
        string? dictionary,
        string? study_id,
        string? studyId,
        int? limit,
        CancellationToken cancellationToken)
    {
        string termQuery = !string.IsNullOrWhiteSpace(query) ? query : (q ?? string.Empty);
        string? effectiveStudyId = !string.IsNullOrWhiteSpace(study_id) ? study_id : studyId;

        var suggestions = await autosuggestService.GetAutosuggestionsAsync(
            termQuery,
            dictionary,
            effectiveStudyId,
            limit ?? 5,
            cancellationToken);

        return TypedResults.Ok(suggestions);
    }
}
