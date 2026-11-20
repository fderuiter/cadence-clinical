using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Shared fail-closed study and site scope guard for Execution Minimal API endpoints (ADR-2516).
/// Delegates caller scope resolution to <see cref="EndpointScopeGuard"/> in <c>Cadence.Security</c>.
/// @req:PRD-SYS-192
/// </summary>
public static class ExecutionScopeGuard
{
    public static Principal? ResolvePrincipal(HttpContext? httpContext) =>
        EndpointScopeGuard.ResolvePrincipal(httpContext);

    public static bool CanAccessStudy(HttpContext? httpContext, string? studyId) =>
        EndpointScopeGuard.CanAccessStudy(httpContext, studyId);

    public static bool CanAccessSite(HttpContext? httpContext, string? siteId) =>
        EndpointScopeGuard.CanAccessSite(httpContext, siteId);

    public static bool IsSiteBound(Principal principal) =>
        EndpointScopeGuard.IsSiteBound(principal);

    public static IResult? RequireAnyStudyScope(HttpContext? httpContext) =>
        EndpointScopeGuard.RequireAnyStudyScope(httpContext);

    public static IResult? RequireAllStudies(HttpContext? httpContext) =>
        EndpointScopeGuard.RequireAllStudies(httpContext);

    public static IResult? RequireStudy(HttpContext? httpContext, string? studyId) =>
        EndpointScopeGuard.RequireStudy(httpContext, studyId);

    public static IResult? RequireSite(HttpContext? httpContext, string? siteId) =>
        EndpointScopeGuard.RequireSite(httpContext, siteId);

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
        out IReadOnlySet<string>? studyFilter) =>
        EndpointScopeGuard.ResolveStudyFilter(httpContext, studyId, out studyFilter);

    public static IResult? ResolveSiteFilter(
        HttpContext? httpContext,
        string? siteId,
        out IReadOnlySet<string>? siteFilter,
        out string? effectiveSingleSiteId) =>
        EndpointScopeGuard.ResolveSiteFilter(httpContext, siteId, out siteFilter, out effectiveSingleSiteId);

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
        Func<T, string?> siteSelector) =>
        EndpointScopeGuard.ApplyFilters(items, studyFilter, studySelector, siteFilter, siteSelector);

    public static IResult Forbidden(string detail) =>
        EndpointScopeGuard.Problem(detail);
}
