using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Fail-closed study and site scope guard for CTMS Minimal API endpoints (ADR-2516).
/// Delegates caller scope resolution to <see cref="EndpointScopeGuard"/> in <c>Cadence.Security</c>.
/// </summary>
public static class CtmsScopeGuard
{
    public const string ForbiddenCode = EndpointScopeGuard.ForbiddenCode;

    public static Principal? ResolvePrincipal(HttpContext? httpContext) =>
        EndpointScopeGuard.ResolvePrincipal(httpContext);

    public static bool CanAccessStudy(HttpContext? httpContext, string? studyId) =>
        EndpointScopeGuard.CanAccessStudy(httpContext, studyId);

    public static bool CanAccessSite(HttpContext? httpContext, string? siteId) =>
        EndpointScopeGuard.CanAccessSite(httpContext, siteId);

    public static bool IsSiteBound(Principal principal) =>
        EndpointScopeGuard.IsSiteBound(principal);

    public static ProblemHttpResult? RequireAnyScopeForLookup(HttpContext? httpContext)
    {
        if (EndpointScopeGuard.RequireAnyStudyScope(httpContext) is { } missingStudy)
        {
            return missingStudy;
        }

        var principal = ResolvePrincipal(httpContext)!;
        if (IsSiteBound(principal)
            && !principal.AssignedSites.Any(s => !string.IsNullOrWhiteSpace(s) && SiteAccessEvaluator.CanAccessSite(principal, s)))
        {
            return Forbidden("Site-scoped caller has no assigned sites.");
        }

        return null;
    }

    public static ProblemHttpResult? RequireStudy(HttpContext? httpContext, string? studyId) =>
        EndpointScopeGuard.RequireStudy(httpContext, studyId, "Outside your studies");

    public static ProblemHttpResult? RequireSite(HttpContext? httpContext, string? siteId) =>
        EndpointScopeGuard.RequireSite(httpContext, siteId, "Outside your sites");

    public static ProblemHttpResult? RequireStudyAndSite(HttpContext? httpContext, string? studyId, string? siteId) =>
        RequireStudy(httpContext, studyId) ?? RequireSite(httpContext, siteId);

    public static ProblemHttpResult? ResolveStudyFilter(
        HttpContext? httpContext,
        string? studyId,
        out IReadOnlySet<string>? studyFilter) =>
        EndpointScopeGuard.ResolveStudyFilter(httpContext, studyId, out studyFilter, "Outside your studies");

    public static ProblemHttpResult? ResolveSiteFilter(
        HttpContext? httpContext,
        string? siteId,
        out IReadOnlySet<string>? siteFilter) =>
        EndpointScopeGuard.ResolveSiteFilter(httpContext, siteId, out siteFilter, out _, "Outside your sites");

    public static ProblemHttpResult? ResolveStudyAndSiteFilters(
        HttpContext? httpContext,
        string? studyId,
        string? siteId,
        out IReadOnlySet<string>? studyFilter,
        out IReadOnlySet<string>? siteFilter)
    {
        siteFilter = null;
        return ResolveStudyFilter(httpContext, studyId, out studyFilter)
            ?? ResolveSiteFilter(httpContext, siteId, out siteFilter);
    }

    public static async Task<ProblemHttpResult?> RequireDoaSiteScopeAsync(
        HttpContext? httpContext,
        CtmsDbContext db,
        string? siteId,
        CancellationToken ct)
    {
        if (RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return missingScope;
        }

        if (RequireSite(httpContext, siteId) is { } siteDenied)
        {
            return siteDenied;
        }

        if (string.IsNullOrWhiteSpace(siteId))
        {
            return null;
        }

        string trimmedSite = siteId.Trim();
        var gateStudies = await db.CtmsSiteGreenlightGates.AsNoTracking()
            .Where(g => g.SiteId == trimmedSite && g.StudyId != string.Empty)
            .Select(g => g.StudyId)
            .Distinct()
            .ToListAsync(ct);

        var milestoneStudies = await db.CtmsSiteMilestones.AsNoTracking()
            .Where(m => m.SiteId == trimmedSite && m.StudyId != string.Empty)
            .Select(m => m.StudyId)
            .Distinct()
            .ToListAsync(ct);

        var visitStudies = await db.CtmsMonitoringVisits.AsNoTracking()
            .Where(v => v.SiteId == trimmedSite && v.StudyId != string.Empty)
            .Select(v => v.StudyId)
            .Distinct()
            .ToListAsync(ct);

        var knownStudies = gateStudies
            .Concat(milestoneStudies)
            .Concat(visitStudies)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (knownStudies.Count > 0 && !knownStudies.Any(study => CanAccessStudy(httpContext, study)))
        {
            return Forbidden($"Caller is not authorized for the study governing site '{trimmedSite}'.", "Outside your studies");
        }

        return null;
    }

    public static List<T> ApplyFilters<T>(
        IEnumerable<T> items,
        IReadOnlySet<string>? studyFilter,
        Func<T, string?> studySelector,
        IReadOnlySet<string>? siteFilter,
        Func<T, string?> siteSelector) =>
        EndpointScopeGuard.ApplyFilters(items, studyFilter, studySelector, siteFilter, siteSelector);

    public static ProblemHttpResult Forbidden(string detail, string title = "Forbidden") =>
        EndpointScopeGuard.Problem(detail, title);
}
