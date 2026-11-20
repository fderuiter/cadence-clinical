using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Shared fail-closed study and site scope guard for Execution Minimal API endpoints (ADR-2516).
/// Evaluates the gateway-signed caller identity via <see cref="CallerScope"/>,
/// <see cref="StudyAccessEvaluator"/>, and <see cref="SiteAccessEvaluator"/>, returning RFC 7807
/// HTTP 403 <c>ProblemDetails</c> responses whenever study, site, or enterprise (<c>*</c>) scope is missing.
/// @req:PRD-SYS-192
/// </summary>
public static class ExecutionScopeGuard
{
    private const string ForbiddenCode = "FORBIDDEN_SCOPE";

    /// <summary>
    /// Resolves the authenticated <see cref="Principal"/> from <see cref="CallerScope.Principal"/>
    /// (populated by <c>GatewayAuthenticationHandler</c>), falling back to verified <see cref="HttpContext.User"/>
    /// claims when endpoint methods are invoked directly in unit tests without the authentication middleware.
    /// </summary>
    public static Principal? ResolvePrincipal(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        if (CallerScope.Principal(httpContext) is { } principal)
        {
            return principal;
        }

        ClaimsPrincipal user = httpContext.User;
        string? userId = user.FindFirst(CadenceClaimTypes.Subject)?.Value;
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var rawRoles = ReadClaims(user, ClaimTypes.Role);
        return new Principal
        {
            UserId = userId,
            Roles = RoleNormalization.GetNormalizedRoles(rawRoles),
            RawRoles = rawRoles,
            AssignedStudies = ReadClaims(user, CadenceClaimTypes.StudyIds),
            AssignedSites = ReadClaims(user, CadenceClaimTypes.SiteId),
            UnblindedAccess = string.Equals(
                user.FindFirst(CadenceClaimTypes.UnblindedAccess)?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>True when the caller is authorized to access <paramref name="studyId"/>.</summary>
    public static bool CanAccessStudy(HttpContext? httpContext, string? studyId)
    {
        if (httpContext is null)
        {
            return false;
        }

        if (CallerScope.Principal(httpContext) is not null)
        {
            return CallerScope.CanAccessStudy(httpContext, studyId);
        }

        return StudyAccessEvaluator.CanAccessStudy(ResolvePrincipal(httpContext), studyId);
    }

    /// <summary>True when the caller is authorized to access <paramref name="siteId"/>.</summary>
    public static bool CanAccessSite(HttpContext? httpContext, string? siteId) =>
        SiteAccessEvaluator.CanAccessSite(ResolvePrincipal(httpContext), siteId);

    /// <summary>True when the caller holds a site-scoped role or carries explicit site assignments.</summary>
    public static bool IsSiteBound(Principal principal) =>
        principal.Roles.Any(SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains)
        || principal.AssignedSites.Count > 0;

    /// <summary>
    /// Returns <see langword="null"/> when the caller has at least one non-empty study assignment,
    /// otherwise a fail-closed 403 <c>ProblemDetails</c> response.
    /// </summary>
    public static IResult? RequireAnyStudyScope(HttpContext? httpContext)
    {
        var principal = ResolvePrincipal(httpContext);
        if (principal is null || !principal.AssignedStudies.Any(s => !string.IsNullOrWhiteSpace(s)))
        {
            return Forbidden("Caller has no assigned study scope.");
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="null"/> when the caller holds <see cref="StudyAccessEvaluator.AllStudies"/> (<c>*</c>),
    /// otherwise a fail-closed 403 <c>ProblemDetails</c> response.
    /// </summary>
    public static IResult? RequireAllStudies(HttpContext? httpContext)
    {
        var principal = ResolvePrincipal(httpContext);
        bool hasAllStudies = principal is not null
            && principal.AssignedStudies.Any(s =>
                string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal));

        return hasAllStudies
            ? null
            : Forbidden("Global enterprise scope requires wildcard study assignment ('*').");
    }

    /// <summary>
    /// Returns <see langword="null"/> when the caller may access <paramref name="studyId"/>,
    /// otherwise a fail-closed 403 <c>ProblemDetails</c> response.
    /// </summary>
    public static IResult? RequireStudy(HttpContext? httpContext, string? studyId) =>
        CanAccessStudy(httpContext, studyId)
            ? null
            : Forbidden($"Caller is not authorized for study '{studyId ?? string.Empty}'.");

    /// <summary>
    /// Returns <see langword="null"/> when the caller may access <paramref name="siteId"/>,
    /// otherwise a fail-closed 403 <c>ProblemDetails</c> response.
    /// </summary>
    public static IResult? RequireSite(HttpContext? httpContext, string? siteId) =>
        CanAccessSite(httpContext, siteId)
            ? null
            : Forbidden($"Caller is not authorized for site '{siteId ?? string.Empty}'.");

    /// <summary>
    /// Enforces both concrete <paramref name="studyId"/> and concrete <paramref name="siteId"/> scope.
    /// </summary>
    public static IResult? RequireStudyAndSite(HttpContext? httpContext, string? studyId, string? siteId) =>
        RequireStudy(httpContext, studyId) ?? RequireSite(httpContext, siteId);

    /// <summary>
    /// Enforces <paramref name="studyId"/> scope and optional <paramref name="siteId"/> scope for entities whose
    /// <c>SiteId</c> may be <see langword="null"/> in the domain. Non-empty <paramref name="siteId"/> values are
    /// always evaluated by <see cref="SiteAccessEvaluator.CanAccessSite"/>; empty/null <paramref name="siteId"/>
    /// values deny site-bound callers with empty <c>AssignedSites</c>, and allow site-bound callers only when
    /// <paramref name="allowUnassignedSiteForScopedCaller"/> is <see langword="true"/> and they hold at least one
    /// valid assigned site.
    /// </summary>
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

    /// <summary>
    /// Validates an optional <paramref name="studyId"/> query filter and outputs <paramref name="studyFilter"/>
    /// (<see langword="null"/> when the caller holds <c>*</c> or supplied an authorized explicit study, otherwise
    /// the caller's assigned studies). Returns a 403 <c>ProblemDetails</c> result when denied.
    /// </summary>
    public static IResult? ResolveStudyFilter(
        HttpContext? httpContext,
        string? studyId,
        out IReadOnlySet<string>? studyFilter)
    {
        studyFilter = null;

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            return RequireStudy(httpContext, studyId);
        }

        var principal = ResolvePrincipal(httpContext);
        if (principal is null || !principal.AssignedStudies.Any(s => !string.IsNullOrWhiteSpace(s)))
        {
            return Forbidden("Caller has no assigned study scope.");
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
                ? Forbidden("Caller has no assigned study scope.")
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
            ? Forbidden("Caller has no assigned study scope.")
            : null;
    }

    /// <summary>
    /// Validates an optional <paramref name="siteId"/> query filter for list endpoints. When <paramref name="siteId"/>
    /// is supplied, verifies <see cref="SiteAccessEvaluator.CanAccessSite"/> (returning 403 on mismatch). When
    /// <paramref name="siteId"/> is omitted by a site-bound caller, fails closed with 403 if <c>AssignedSites</c> has
    /// no valid sites, otherwise populates <paramref name="siteFilter"/> with the caller's assigned sites so only
    /// assigned-site rows are returned.
    /// </summary>
    public static IResult? ResolveSiteFilter(
        HttpContext? httpContext,
        string? siteId,
        out IReadOnlySet<string>? siteFilter,
        out string? effectiveSingleSiteId)
    {
        siteFilter = null;
        effectiveSingleSiteId = siteId;

        var principal = ResolvePrincipal(httpContext);
        if (principal is null)
        {
            return Forbidden("Caller identity is required.");
        }

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            if (!SiteAccessEvaluator.CanAccessSite(principal, siteId))
            {
                return Forbidden($"Caller is not authorized for site '{siteId}'.");
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
                : Forbidden("Caller has no assigned site scope.");
        }

        var assignedSites = principal.AssignedSites
            .Where(s => !string.IsNullOrWhiteSpace(s) && SiteAccessEvaluator.CanAccessSite(principal, s))
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (assignedSites.Count == 0)
        {
            return Forbidden("Site-scoped caller has no assigned sites.");
        }

        siteFilter = assignedSites;
        if (assignedSites.Count == 1)
        {
            effectiveSingleSiteId = assignedSites.First();
        }

        return null;
    }

    /// <summary>
    /// Enforces concrete <paramref name="studyId"/> scope and resolves an optional <paramref name="siteId"/> query filter.
    /// </summary>
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

    /// <summary>
    /// Resolves both optional <paramref name="studyId"/> and optional <paramref name="siteId"/> query filters.
    /// </summary>
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

    /// <summary>Filters <paramref name="items"/> to study and site scopes when narrowing sets are active.</summary>
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

    /// <summary>Creates a standardized RFC 7807 HTTP 403 <c>ProblemDetails</c> result.</summary>
    public static IResult Forbidden(string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden",
            extensions: new Dictionary<string, object?> { ["code"] = ForbiddenCode });

    private static List<string> ReadClaims(ClaimsPrincipal user, string claimType) =>
        user.FindAll(claimType)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
}
