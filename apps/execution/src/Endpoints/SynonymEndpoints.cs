using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Coding;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

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
            .DisableAntiforgery()
            .RequirePermission("medical_coding:manage_synonyms").RequireCallerStudyScope();

        group.MapPost("/import", ImportSynonymFileAsync)
            .WithName("ImportSynonymFile")
            .WithSummary("Ingest validated synonym dictionary rules into persistent store with GxP audit lineage.")
            .DisableAntiforgery()
            .RequirePermission("medical_coding:manage_synonyms").RequireCallerStudyScope();

        group.MapGet("/", GetActiveSynonymRulesAsync)
            .WithName("GetActiveSynonymRules")
            .WithSummary("Retrieve active synonym rules for study auto-coding engine.")
            .RequirePermission("medical_coding:read").RequireCallerStudyScope();

        group.MapGet("/jobs", GetSynonymImportJobsAsync)
            .WithName("GetSynonymImportJobs")
            .WithSummary("Retrieve GxP audit ledger of bulk synonym import jobs.")
            .RequirePermission("medical_coding:read").RequireCallerStudyScope();

        group.MapDelete("/{id:guid}", DeactivateSynonymRuleAsync)
            .WithName("DeactivateSynonymRule")
            .WithSummary("Deactivate an active synonym rule.")
            .RequirePermission("medical_coding:manage_synonyms").RequireCallerStudyScope();

        return group;
    }

    public static async Task<IResult> ValidateSynonymFileAsync(
        HttpContext httpContext,
        ISynonymImportService synonymService,
        CancellationToken cancellationToken)
    {
        var caller = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (!HasImportAuthorization(caller))
        {
            return ClinicalScopeGuard.Forbidden(
                "Bulk synonym dictionary import requires DATA_MANAGER or SPONSOR_DESIGNER authorization.",
                code: ClinicalScopeGuard.ForbiddenCode);
        }

        if (ClinicalScopeGuard.RequireAnyStudyScope(httpContext, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } callerDenied)
        {
            return callerDenied;
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

        if (RequireSynonymScope(httpContext, scope, studyId) is { } scopeDenied)
        {
            return scopeDenied;
        }

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
        var caller = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (!HasImportAuthorization(caller))
        {
            return ClinicalScopeGuard.Forbidden(
                "Bulk synonym dictionary import requires DATA_MANAGER or SPONSOR_DESIGNER authorization.",
                code: ClinicalScopeGuard.ForbiddenCode);
        }

        if (ClinicalScopeGuard.RequireAnyStudyScope(httpContext, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } callerDenied)
        {
            return callerDenied;
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

        if (RequireSynonymScope(httpContext, scope, studyId) is { } scopeDenied)
        {
            return scopeDenied;
        }

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
            string createdBy = caller?.UserId is { Length: > 0 } callerId && !string.IsNullOrWhiteSpace(callerId)
                ? callerId
                : "DATA_MANAGER";
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
        CancellationToken cancellationToken,
        HttpContext? httpContext = null)
    {
        string? effectiveStudyId = !string.IsNullOrWhiteSpace(study_id) ? study_id : studyId;
        IResult? scopeDenied = !string.IsNullOrWhiteSpace(effectiveStudyId)
            ? ClinicalScopeGuard.RequireStudy(httpContext, effectiveStudyId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            : ClinicalScopeGuard.RequireAllStudies(httpContext, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);
        if (scopeDenied is not null)
        {
            return scopeDenied;
        }

        var rules = await synonymService.GetActiveSynonymRulesAsync(effectiveStudyId, cancellationToken);
        return TypedResults.Ok(rules);
    }

    public static async Task<IResult> GetSynonymImportJobsAsync(
        ISynonymImportService synonymService,
        string? study_id,
        string? studyId,
        CancellationToken cancellationToken,
        HttpContext? httpContext = null)
    {
        string? effectiveStudyId = !string.IsNullOrWhiteSpace(study_id) ? study_id : studyId;
        IResult? scopeDenied = !string.IsNullOrWhiteSpace(effectiveStudyId)
            ? ClinicalScopeGuard.RequireStudy(httpContext, effectiveStudyId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            : ClinicalScopeGuard.RequireAllStudies(httpContext, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);
        if (scopeDenied is not null)
        {
            return scopeDenied;
        }

        var jobs = await synonymService.GetImportJobsAsync(effectiveStudyId, cancellationToken);
        return TypedResults.Ok(jobs);
    }

    public static async Task<IResult> DeactivateSynonymRuleAsync(
        Guid id,
        HttpContext httpContext,
        ISynonymImportService synonymService,
        CancellationToken cancellationToken)
    {
        var caller = ClinicalScopeGuard.ResolvePrincipal(httpContext);
        if (!HasImportAuthorization(caller))
        {
            return ClinicalScopeGuard.Forbidden(
                "Deactivating synonym rules requires DATA_MANAGER or SPONSOR_DESIGNER authorization.",
                code: ClinicalScopeGuard.ForbiddenCode);
        }

        if (ClinicalScopeGuard.RequireVerifiedTenant(httpContext, ClinicalScopeGuard.ForbiddenCode) is { } tenantDenied)
        {
            return tenantDenied;
        }

        var existing = await synonymService.GetRuleByIdAsync(id, cancellationToken).ConfigureAwait(false);
        IResult? scopeDenied = existing is not null
            ? RequireSynonymScope(httpContext, existing.Scope, existing.StudyId)
            : ClinicalScopeGuard.RequireAnyStudyScope(httpContext, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);
        if (scopeDenied is not null)
        {
            return scopeDenied;
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

    private static IResult? RequireSynonymScope(HttpContext? httpContext, string? scope, string? studyId) =>
        IsGlobalScope(scope)
            ? ClinicalScopeGuard.RequireAllStudies(httpContext, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            : ClinicalScopeGuard.RequireStudy(httpContext, studyId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);

    private static bool IsGlobalScope(string? scope) =>
        string.Equals(scope?.Trim(), "ENTERPRISE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(scope?.Trim(), "GLOBAL", StringComparison.OrdinalIgnoreCase);

    private static bool HasImportAuthorization(Principal? caller)
    {
        if (caller is null)
        {
            return false;
        }

        return caller.Roles.Concat(caller.RawRoles).Any(r =>
            r.Equals(RoleNormalization.ROLE_SPONSOR_DM, StringComparison.OrdinalIgnoreCase) ||
            r.Equals("DATA_MANAGER", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("SPONSOR_DESIGNER", StringComparison.OrdinalIgnoreCase));
    }
}
