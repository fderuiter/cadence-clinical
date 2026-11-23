using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Execution facade over <see cref="ClinicalScopeGuard"/> preserving Execution's RFC 7807
/// <c>FORBIDDEN_SCOPE</c> extension code (ADR-2516, PRD-SYS-192).
/// </summary>
public static class ExecutionScopeGuard
{
    private const string ForbiddenCode = "FORBIDDEN_SCOPE";

    public static Principal? ResolvePrincipal(HttpContext? httpContext) =>
        ClinicalScopeGuard.ResolvePrincipal(httpContext);

    public static bool CanAccessStudy(HttpContext? httpContext, string? studyId) =>
        ClinicalScopeGuard.CanAccessStudy(httpContext, studyId);

    public static bool CanAccessSite(HttpContext? httpContext, string? siteId) =>
        ClinicalScopeGuard.CanAccessSite(httpContext, siteId);

    public static bool IsSiteBound(Principal principal) =>
        ClinicalScopeGuard.IsSiteBound(principal);

    public static IResult? RequireVerifiedTenant(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        if (CallerScope.Principal(httpContext) is { } gatewayPrincipal && !ClinicalScopeGuard.HasTenant(gatewayPrincipal))
        {
            return Forbidden("Caller has no verified workspace tenant scope.");
        }

        return null;
    }

    public static IResult? RequireAnyStudyScope(HttpContext? httpContext) =>
        RequireVerifiedTenant(httpContext)
        ?? (ClinicalScopeGuard.RequireAnyStudyScope(httpContext) is null
            ? null
            : Forbidden("Caller has no assigned study scope."));

    public static IResult? RequireAllStudies(HttpContext? httpContext) =>
        RequireVerifiedTenant(httpContext)
        ?? (ClinicalScopeGuard.RequireAllStudies(httpContext) is null
            ? null
            : Forbidden("Global enterprise scope requires wildcard study assignment ('*')."));

    public static IResult? RequireStudy(HttpContext? httpContext, string? studyId) =>
        RequireVerifiedTenant(httpContext)
        ?? (CanAccessStudy(httpContext, studyId)
            ? null
            : Forbidden($"Caller is not authorized for study '{studyId ?? string.Empty}'."));

    public static IResult? RequireSite(HttpContext? httpContext, string? siteId) =>
        RequireVerifiedTenant(httpContext)
        ?? (CanAccessSite(httpContext, siteId)
            ? null
            : Forbidden($"Caller is not authorized for site '{siteId ?? string.Empty}'."));

    public static IResult? RequireStudyAndSite(HttpContext? httpContext, string? studyId, string? siteId) =>
        RequireStudy(httpContext, studyId) ?? RequireSite(httpContext, siteId);

    public static IResult? RequireStudyAndOptionalSite(
        HttpContext? httpContext,
        string? studyId,
        string? siteId,
        bool allowUnassignedSiteForScopedCaller = false)
    {
        if (RequireStudy(httpContext, studyId) is { } studyDenied)
        {
            return studyDenied;
        }

        var principal = ResolvePrincipal(httpContext);
        if (principal is null)
        {
            return Forbidden("Caller identity is required.");
        }

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            return SiteAccessEvaluator.CanAccessSite(principal, siteId)
                ? null
                : Forbidden($"Caller is not authorized for site '{siteId}'.");
        }

        if (SiteAccessEvaluator.CanAccessSite(principal, siteId))
        {
            return null;
        }

        if (allowUnassignedSiteForScopedCaller
            && principal.AssignedSites.Any(s => !string.IsNullOrWhiteSpace(s) && SiteAccessEvaluator.CanAccessSite(principal, s)))
        {
            return null;
        }

        return Forbidden("Caller is not authorized for unscoped site records.");
    }

    public static IResult? ResolveStudyFilter(
        HttpContext? httpContext,
        string? studyId,
        out IReadOnlySet<string>? studyFilter)
    {
        if (RequireVerifiedTenant(httpContext) is { } tenantDenied)
        {
            studyFilter = null;
            return tenantDenied;
        }

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            studyFilter = null;
            return RequireStudy(httpContext, studyId);
        }

        if (ClinicalScopeGuard.ResolveStudyFilter(httpContext, studyId, out var allowedStudies, out _))
        {
            studyFilter = allowedStudies;
            return null;
        }

        studyFilter = null;
        return Forbidden("Caller has no assigned study scope.");
    }

    public static IResult? ResolveSiteFilter(
        HttpContext? httpContext,
        string? siteId,
        out IReadOnlySet<string>? siteFilter,
        out string? effectiveSingleSiteId)
    {
        if (RequireVerifiedTenant(httpContext) is { } tenantDenied)
        {
            siteFilter = null;
            effectiveSingleSiteId = null;
            return tenantDenied;
        }

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            siteFilter = null;
            effectiveSingleSiteId = siteId;
            if (ClinicalScopeGuard.CanAccessSite(httpContext, siteId))
            {
                return null;
            }

            return ResolvePrincipal(httpContext) is null
                ? Forbidden("Caller identity is required.")
                : Forbidden($"Caller is not authorized for site '{siteId}'.");
        }

        if (ClinicalScopeGuard.ResolveSiteFilter(httpContext, null, out var allowedSites, out _))
        {
            siteFilter = allowedSites is null
                ? null
                : new HashSet<string>(allowedSites, StringComparer.OrdinalIgnoreCase);
            effectiveSingleSiteId = allowedSites is { Count: 1 } ? allowedSites.First() : null;
            return null;
        }

        siteFilter = null;
        effectiveSingleSiteId = null;
        var principal = ResolvePrincipal(httpContext);
        if (principal is null)
        {
            return Forbidden("Caller identity is required.");
        }

        return IsSiteBound(principal)
            ? Forbidden("Site-scoped caller has no assigned sites.")
            : Forbidden("Caller has no assigned site scope.");
    }

    public static IResult? RequireStudyAndResolveSiteFilter(
        HttpContext? httpContext,
        string? studyId,
        string? siteId,
        out IReadOnlySet<string>? siteFilter,
        out string? effectiveSingleSiteId)
    {
        siteFilter = null;
        effectiveSingleSiteId = siteId;

        if (RequireStudy(httpContext, studyId) is { } studyDenied)
        {
            return studyDenied;
        }

        return ResolveSiteFilter(httpContext, siteId, out siteFilter, out effectiveSingleSiteId);
    }

    public static IResult? ResolveStudyAndSiteFilters(
        HttpContext? httpContext,
        string? studyId,
        string? siteId,
        out IReadOnlySet<string>? studyFilter,
        out IReadOnlySet<string>? siteFilter,
        out string? effectiveSingleSiteId)
    {
        siteFilter = null;
        effectiveSingleSiteId = siteId;

        if (ResolveStudyFilter(httpContext, studyId, out studyFilter) is { } studyDenied)
        {
            return studyDenied;
        }

        return ResolveSiteFilter(httpContext, siteId, out siteFilter, out effectiveSingleSiteId);
    }

    public static IReadOnlyList<T> ApplyFilters<T>(
        IEnumerable<T> items,
        IReadOnlySet<string>? studyFilter,
        Func<T, string?> studySelector,
        IReadOnlySet<string>? siteFilter,
        Func<T, string?> siteSelector)
    {
        IEnumerable<T> query = items;

        if (studyFilter is not null)
        {
            query = query.Where(item =>
                studySelector(item) is { } study && studyFilter.Contains(study.Trim()));
        }

        if (siteFilter is not null)
        {
            query = query.Where(item =>
                siteSelector(item) is { } site
                && !string.IsNullOrWhiteSpace(site)
                && siteFilter.Contains(site.Trim()));
        }

        return query.ToList();
    }

    public static async Task<IResult?> RequireUnlockedAsync(
        HttpContext? httpContext,
        string? studyId,
        string? siteId = null,
        string? subjectId = null,
        CancellationToken cancellationToken = default)
    {
        if (httpContext?.RequestServices?.GetService<IKernelLockGuard>() is not { } lockGuard)
        {
            return null;
        }

        try
        {
            var error = await lockGuard.CheckWriteAllowedAsync(studyId, siteId, subjectId, cancellationToken).ConfigureAwait(false);
            return error?.ToProblemResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Results.Problem(
                detail: "Failed to verify Clinical Kernel lock state before writing.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                extensions: new Dictionary<string, object?> { ["code"] = "KERNEL_LOCK_CHECK_FAILED" });
        }
    }

    public static IResult Forbidden(string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden",
            extensions: new Dictionary<string, object?> { ["code"] = ForbiddenCode });
}
