using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cadence.Security.Rbac;

/// <summary>
/// Shared fail-closed study and site scope evaluator for Minimal API endpoints (ADR-2516, ADR-2548).
/// Delegates caller scope evaluation to <see cref="ClinicalScopeGuard"/> while returning standardized
/// RFC 7807 HTTP 403 <c>ProblemHttpResult</c> responses with <c>code = "FORBIDDEN_SCOPE"</c>.
/// @req:PRD-SYS-192
/// </summary>
public static class EndpointScopeGuard
{
    /// <summary>Machine-readable error code emitted on scope-authorization denials.</summary>
    public const string ForbiddenCode = "FORBIDDEN_SCOPE";

    /// <summary>
    /// Resolves the authenticated <see cref="Principal"/> via <see cref="ClinicalScopeGuard.ResolvePrincipal"/>.
    /// </summary>
    public static Principal? ResolvePrincipal(HttpContext? httpContext) =>
        ClinicalScopeGuard.ResolvePrincipal(httpContext);

    /// <summary>True when the caller is authorized to access <paramref name="studyId"/>.</summary>
    public static bool CanAccessStudy(HttpContext? httpContext, string? studyId) =>
        ClinicalScopeGuard.CanAccessStudy(httpContext, studyId);

    /// <summary>True when the caller is authorized to access <paramref name="siteId"/>.</summary>
    public static bool CanAccessSite(HttpContext? httpContext, string? siteId) =>
        ClinicalScopeGuard.CanAccessSite(httpContext, siteId);

    /// <summary>True when the caller holds a site-scoped role or carries explicit site assignments.</summary>
    public static bool IsSiteBound(Principal? principal) =>
        ClinicalScopeGuard.IsSiteBound(principal);

    /// <summary>
    /// Returns <see langword="null"/> when the caller has at least one non-empty study assignment,
    /// otherwise a fail-closed 403 <c>ProblemHttpResult</c>.
    /// </summary>
    public static ProblemHttpResult? RequireAnyStudyScope(HttpContext? httpContext)
    {
        var principal = ResolvePrincipal(httpContext);
        if (principal is null || !principal.AssignedStudies.Any(s => !string.IsNullOrWhiteSpace(s)))
        {
            return Problem("Caller has no assigned study scope.");
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="null"/> when the caller holds <see cref="StudyAccessEvaluator.AllStudies"/> (<c>*</c>),
    /// otherwise a fail-closed 403 <c>ProblemHttpResult</c>.
    /// </summary>
    public static ProblemHttpResult? RequireAllStudies(HttpContext? httpContext)
    {
        var principal = ResolvePrincipal(httpContext);
        bool hasAllStudies = principal is not null
            && principal.AssignedStudies.Any(s =>
                string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal));

        return hasAllStudies
            ? null
            : Problem("Global enterprise scope requires wildcard study assignment ('*').");
    }

    /// <summary>
    /// Returns <see langword="null"/> when the caller may access <paramref name="studyId"/>,
    /// otherwise a fail-closed 403 <c>ProblemHttpResult</c>.
    /// </summary>
    public static ProblemHttpResult? RequireStudy(
        HttpContext? httpContext,
        string? studyId,
        string title = "Forbidden") =>
        CanAccessStudy(httpContext, studyId)
            ? null
            : Problem($"Caller is not authorized for study '{studyId ?? string.Empty}'.", title);

    /// <summary>
    /// Returns <see langword="null"/> when the caller may access <paramref name="siteId"/>,
    /// otherwise a fail-closed 403 <c>ProblemHttpResult</c>.
    /// </summary>
    public static ProblemHttpResult? RequireSite(
        HttpContext? httpContext,
        string? siteId,
        string title = "Forbidden") =>
        CanAccessSite(httpContext, siteId)
            ? null
            : Problem($"Caller is not authorized for site '{siteId ?? string.Empty}'.", title);

    /// <summary>
    /// Validates an optional <paramref name="studyId"/> query filter and outputs <paramref name="studyFilter"/>.
    /// </summary>
    public static ProblemHttpResult? ResolveStudyFilter(
        HttpContext? httpContext,
        string? studyId,
        out IReadOnlySet<string>? studyFilter,
        string deniedTitle = "Forbidden")
    {
        studyFilter = null;

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            return RequireStudy(httpContext, studyId, deniedTitle);
        }

        var principal = ResolvePrincipal(httpContext);
        if (principal is null || !principal.AssignedStudies.Any(s => !string.IsNullOrWhiteSpace(s)))
        {
            return Problem("Caller has no assigned study scope.");
        }

        if (httpContext is not null && CallerScope.Principal(httpContext) is not null)
        {
            var scopedStudies = CallerScope.StudyFilter(httpContext);
            if (scopedStudies is null)
            {
                return null;
            }

            studyFilter = scopedStudies as IReadOnlySet<string>
                ?? scopedStudies.ToHashSet(StringComparer.Ordinal);
            return studyFilter.Count == 0
                ? Problem("Caller has no assigned study scope.")
                : null;
        }

        if (principal.AssignedStudies.Any(s =>
                string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal)))
        {
            return null;
        }

        studyFilter = principal.AssignedStudies
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return studyFilter.Count == 0
            ? Problem("Caller has no assigned study scope.")
            : null;
    }

    /// <summary>
    /// Validates an optional <paramref name="siteId"/> query filter for list endpoints.
    /// </summary>
    public static ProblemHttpResult? ResolveSiteFilter(
        HttpContext? httpContext,
        string? siteId,
        out IReadOnlySet<string>? siteFilter,
        out string? effectiveSingleSiteId,
        string deniedTitle = "Forbidden")
    {
        siteFilter = null;
        effectiveSingleSiteId = siteId;

        var principal = ResolvePrincipal(httpContext);
        if (principal is null)
        {
            return Problem("Caller identity is required.");
        }

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            if (!SiteAccessEvaluator.CanAccessSite(principal, siteId))
            {
                return Problem($"Caller is not authorized for site '{siteId}'.", deniedTitle);
            }

            if (IsSiteBound(principal))
            {
                siteFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { siteId.Trim() };
            }

            return null;
        }

        if (!IsSiteBound(principal))
        {
            return SiteAccessEvaluator.CanAccessSite(principal, siteId)
                ? null
                : Problem("Caller has no assigned site scope.", deniedTitle);
        }

        var assignedSites = principal.AssignedSites
            .Where(s => !string.IsNullOrWhiteSpace(s) && SiteAccessEvaluator.CanAccessSite(principal, s))
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (assignedSites.Count == 0)
        {
            return Problem("Site-scoped caller has no assigned sites.", deniedTitle);
        }

        siteFilter = assignedSites;
        if (assignedSites.Count == 1)
        {
            effectiveSingleSiteId = assignedSites.First();
        }

        return null;
    }

    /// <summary>Filters <paramref name="items"/> to study and site scopes when narrowing sets are active.</summary>
    public static List<T> ApplyFilters<T>(
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

    /// <summary>Creates a standardized RFC 7807 HTTP 403 <c>ProblemHttpResult</c> with <c>FORBIDDEN_SCOPE</c>.</summary>
    public static ProblemHttpResult Problem(string detail, string title = "Forbidden") =>
        TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            title: title,
            extensions: new Dictionary<string, object?> { ["code"] = ForbiddenCode });
}
