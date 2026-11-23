using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cadence.Security.Rbac;

/// <summary>
/// Shared fail-closed tenant, study, and site scope guard for standalone clinical service handlers.
/// Enforces identical fail-closed tenant, study, and site boundaries across standalone services
/// (PRD-CRF-013, PRD-SYS-053, PRD-SYS-192, ADR-2514, ADR-2516, ADR-2548).
/// </summary>
public static class ClinicalScopeGuard
{
    public const string TenantAccessDeniedDetail = "The caller does not carry an active verified tenant scope.";
    public const string StudyAccessDeniedDetail = "The caller is not assigned to the requested study.";
    public const string SiteAccessDeniedDetail = "The caller is not assigned to the requested site.";
    public const string OutsideStudyTitle = "Outside your studies";
    public const string OutsideStudyDeniedDetail = "You are not assigned to this study.";
    public const string ForbiddenCode = "FORBIDDEN_SCOPE";

    private static readonly MethodInfo StringTrimMethod =
        typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes)!;

    private static readonly MethodInfo StringToUpperMethod =
        typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!;

    /// <summary>
    /// Resolves the caller <see cref="Principal"/> from <see cref="CallerScope.Principal"/>,
    /// falling back to authenticated <see cref="ClaimsPrincipal"/> claims for direct handler tests.
    /// </summary>
    public static Principal? ResolvePrincipal(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        if (CallerScope.Principal(httpContext) is { } callerPrincipal)
        {
            return callerPrincipal;
        }

        if (httpContext.User.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        var rawRoles = httpContext.User.Claims
            .Where(c => c.Type == ClaimTypes.Role || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .ToArray();

        var studies = httpContext.User.FindAll(CadenceClaimTypes.StudyIds)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        var sites = httpContext.User.FindAll(CadenceClaimTypes.SiteId)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        string? tenantId = httpContext.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;

        return new Principal
        {
            UserId = httpContext.User.FindFirst(CadenceClaimTypes.Subject)?.Value
                ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.Identity.Name
                ?? string.Empty,
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
            Roles = RoleNormalization.GetNormalizedRoles(rawRoles),
            RawRoles = rawRoles,
            AssignedStudies = studies,
            AssignedSites = sites,
            SponsorId = httpContext.User.FindFirst(CadenceClaimTypes.SponsorId)?.Value,
            UnblindedAccess = string.Equals(
                httpContext.User.FindFirst(CadenceClaimTypes.UnblindedAccess)?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase),
        };
    }

    public static bool HasTenant(Principal? principal) =>
        principal?.TenantId is { Length: > 0 } tenantId && !string.IsNullOrWhiteSpace(tenantId);

    public static ProblemHttpResult? RequireTenant(
        HttpContext? httpContext,
        out string tenantId,
        string? code = null)
    {
        var principal = ResolvePrincipal(httpContext);
        if (principal?.TenantId is { Length: > 0 } resolved && !string.IsNullOrWhiteSpace(resolved))
        {
            tenantId = resolved.Trim();
            return null;
        }

        tenantId = string.Empty;
        return Forbidden(TenantAccessDeniedDetail, code: code);
    }

    public static bool CanAccessStudy(HttpContext? httpContext, string? studyId) =>
        StudyAccessEvaluator.CanAccessStudy(ResolvePrincipal(httpContext), studyId);

    public static bool CanAccessSite(HttpContext? httpContext, string? siteId) =>
        SiteAccessEvaluator.CanAccessSite(ResolvePrincipal(httpContext), siteId);

    public static bool HasAnyStudyAssignment(Principal? principal) =>
        principal is not null
        && principal.AssignedStudies.Any(s => !string.IsNullOrWhiteSpace(s));

    public static bool HasValidSiteScopeIfRequired(Principal? principal)
    {
        if (principal is null)
        {
            return false;
        }

        if (!IsSiteBound(principal))
        {
            return true;
        }

        return principal.AssignedSites.Any(s => !string.IsNullOrWhiteSpace(s));
    }

    /// <summary>
    /// True when the caller carries site-scoped roles (<see cref="SiteAccessEvaluator.SITE_SCOPED_ROLES"/>)
    /// or has explicit site assignments on their signed identity.
    /// </summary>
    public static bool IsSiteBound(Principal? principal)
    {
        if (principal is null)
        {
            return true;
        }

        bool hasSiteRole = principal.Roles
            .Select(RoleNormalization.NormalizeRole)
            .Any(role => SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains(role));

        return hasSiteRole || principal.AssignedSites.Count > 0;
    }

    /// <summary>
    /// True when the caller holds wildcard study access (<c>*</c>) and is not site-bound,
    /// so neither study nor site narrowing needs to be applied.
    /// </summary>
    public static bool IsUnrestricted(HttpContext? httpContext) =>
        IsUnrestricted(ResolvePrincipal(httpContext));

    /// <summary>
    /// True when <paramref name="principal"/> holds wildcard study access (<c>*</c>) and is not site-bound.
    /// </summary>
    public static bool IsUnrestricted(Principal? principal) =>
        principal is not null
        && principal.AssignedStudies.Any(s =>
            string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal))
        && !IsSiteBound(principal);

    public static ProblemHttpResult? RequireAnyStudyScope(HttpContext? httpContext, string? code = null)
    {
        var principal = ResolvePrincipal(httpContext);
        if (!HasAnyStudyAssignment(principal))
        {
            return Forbidden(StudyAccessDeniedDetail, code: code);
        }

        return null;
    }

    public static ProblemHttpResult? RequireAnyStudyAndSiteScope(HttpContext? httpContext, string? code = null)
    {
        var principal = ResolvePrincipal(httpContext);
        if (!HasAnyStudyAssignment(principal))
        {
            return Forbidden(StudyAccessDeniedDetail, code: code);
        }

        if (!HasValidSiteScopeIfRequired(principal))
        {
            return Forbidden(SiteAccessDeniedDetail, code: code);
        }

        return null;
    }

    public static ProblemHttpResult? RequireAllStudies(HttpContext? httpContext, string? code = null)
    {
        var principal = ResolvePrincipal(httpContext);
        if (principal is null
            || !principal.AssignedStudies.Any(s =>
                string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal)))
        {
            return Forbidden(StudyAccessDeniedDetail, code: code);
        }

        return null;
    }

    public static ProblemHttpResult? RequireStudy(
        HttpContext? httpContext,
        string? studyId,
        string? code = null)
    {
        var principal = ResolvePrincipal(httpContext);
        if (!StudyAccessEvaluator.CanAccessStudy(principal, studyId))
        {
            return Forbidden(StudyAccessDeniedDetail, code: code);
        }

        return null;
    }

    public static ProblemHttpResult? RequireSite(
        HttpContext? httpContext,
        string? siteId,
        string? code = null)
    {
        var principal = ResolvePrincipal(httpContext);
        if (!SiteAccessEvaluator.CanAccessSite(principal, siteId))
        {
            return Forbidden(SiteAccessDeniedDetail, code: code);
        }

        return null;
    }

    public static ProblemHttpResult? RequireStudyAndSite(
        HttpContext? httpContext,
        string? studyId,
        string? siteId,
        string? code = null)
    {
        if (RequireStudy(httpContext, studyId, code) is { } studyDenied)
        {
            return studyDenied;
        }

        return RequireSite(httpContext, siteId, code);
    }

    public static ProblemHttpResult? RequireStudyAndOptionalSite(
        HttpContext? httpContext,
        string? studyId,
        string? siteId,
        bool allowUnassignedSiteForScopedCaller = false,
        string? code = null)
    {
        var principal = ResolvePrincipal(httpContext);
        if (!StudyAccessEvaluator.CanAccessStudy(principal, studyId))
        {
            return Forbidden(StudyAccessDeniedDetail, code: code);
        }

        if (string.IsNullOrWhiteSpace(siteId))
        {
            if (IsSiteBound(principal)
                && (!allowUnassignedSiteForScopedCaller || !HasValidSiteScopeIfRequired(principal)))
            {
                return Forbidden(SiteAccessDeniedDetail, code: code);
            }

            return null;
        }

        return SiteAccessEvaluator.CanAccessSite(principal, siteId)
            ? null
            : Forbidden(SiteAccessDeniedDetail, code: code);
    }

    public static bool ResolveStudyFilter(
        HttpContext? httpContext,
        string? requestedStudyId,
        out HashSet<string>? allowedStudies,
        out ProblemHttpResult? errorResult,
        string? code = null)
    {
        var principal = ResolvePrincipal(httpContext);
        if (principal is null)
        {
            allowedStudies = [];
            errorResult = Forbidden(StudyAccessDeniedDetail, code: code);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestedStudyId))
        {
            string trimmed = requestedStudyId.Trim();
            if (!StudyAccessEvaluator.CanAccessStudy(principal, trimmed))
            {
                allowedStudies = [];
                errorResult = Forbidden(StudyAccessDeniedDetail, code: code);
                return false;
            }

            allowedStudies = new HashSet<string>([trimmed], StringComparer.Ordinal);
            errorResult = null;
            return true;
        }

        if (principal.AssignedStudies.Any(s => string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal)))
        {
            allowedStudies = null;
            errorResult = null;
            return true;
        }

        var studies = principal.AssignedStudies
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        if (studies.Count == 0)
        {
            allowedStudies = [];
            errorResult = Forbidden(StudyAccessDeniedDetail, code: code);
            return false;
        }

        allowedStudies = studies;
        errorResult = null;
        return true;
    }

    public static bool ResolveSiteFilter(
        HttpContext? httpContext,
        string? requestedSiteId,
        out HashSet<string>? allowedSites,
        out ProblemHttpResult? errorResult,
        string? code = null)
    {
        var principal = ResolvePrincipal(httpContext);
        if (principal is null)
        {
            allowedSites = [];
            errorResult = Forbidden(SiteAccessDeniedDetail, code: code);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestedSiteId))
        {
            string trimmed = requestedSiteId.Trim();
            if (!SiteAccessEvaluator.CanAccessSite(principal, trimmed))
            {
                allowedSites = [];
                errorResult = Forbidden(SiteAccessDeniedDetail, code: code);
                return false;
            }

            allowedSites = new HashSet<string>([trimmed], StringComparer.OrdinalIgnoreCase);
            errorResult = null;
            return true;
        }

        if (!IsSiteBound(principal))
        {
            allowedSites = null;
            errorResult = null;
            return true;
        }

        var sites = principal.AssignedSites
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (sites.Count == 0)
        {
            allowedSites = [];
            errorResult = Forbidden(SiteAccessDeniedDetail, code: code);
            return false;
        }

        allowedSites = sites;
        errorResult = null;
        return true;
    }

    /// <summary>
    /// Resolves site scope and single-site pushdown for repository list endpoints: when <paramref name="requestedSiteId"/>
    /// is provided and authorized, <paramref name="effectiveSingleSiteId"/> is set to <paramref name="requestedSiteId"/>
    /// and <paramref name="allowedSites"/> is null; when omitted, <paramref name="allowedSites"/> holds the caller's
    /// case-insensitive site set and <paramref name="effectiveSingleSiteId"/> is populated when the caller has exactly one site.
    /// </summary>
    public static ProblemHttpResult? ResolveSingleSiteFilter(
        HttpContext? httpContext,
        string? requestedSiteId,
        out HashSet<string>? allowedSites,
        out string? effectiveSingleSiteId,
        string? code = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedSiteId))
        {
            allowedSites = null;
            effectiveSingleSiteId = requestedSiteId;
            return RequireSite(httpContext, requestedSiteId, code);
        }

        if (ResolveSiteFilter(httpContext, null, out allowedSites, out var errorResult, code))
        {
            effectiveSingleSiteId = allowedSites is { Count: 1 } ? allowedSites.First() : null;
            return null;
        }

        allowedSites = null;
        effectiveSingleSiteId = null;
        return errorResult;
    }

    public static bool RequireStudyAndResolveSiteFilter(
        HttpContext? httpContext,
        string? studyId,
        string? requestedSiteId,
        out HashSet<string>? allowedSites,
        out ProblemHttpResult? errorResult,
        string? code = null)
    {
        if (RequireStudy(httpContext, studyId, code) is { } studyDenied)
        {
            allowedSites = [];
            errorResult = studyDenied;
            return false;
        }

        return ResolveSiteFilter(httpContext, requestedSiteId, out allowedSites, out errorResult, code);
    }

    public static ProblemHttpResult? RequireStudyAndResolveSingleSiteFilter(
        HttpContext? httpContext,
        string? studyId,
        string? requestedSiteId,
        out HashSet<string>? allowedSites,
        out string? effectiveSingleSiteId,
        string? code = null)
    {
        allowedSites = null;
        effectiveSingleSiteId = requestedSiteId;

        if (RequireStudy(httpContext, studyId, code) is { } studyDenied)
        {
            return studyDenied;
        }

        return ResolveSingleSiteFilter(httpContext, requestedSiteId, out allowedSites, out effectiveSingleSiteId, code);
    }

    public static bool ResolveStudyAndSiteFilters(
        HttpContext? httpContext,
        string? requestedStudyId,
        string? requestedSiteId,
        out HashSet<string>? allowedStudies,
        out HashSet<string>? allowedSites,
        out ProblemHttpResult? errorResult,
        string? code = null)
    {
        if (!ResolveStudyFilter(httpContext, requestedStudyId, out allowedStudies, out errorResult, code))
        {
            allowedSites = [];
            return false;
        }

        return ResolveSiteFilter(httpContext, requestedSiteId, out allowedSites, out errorResult, code);
    }

    public static ProblemHttpResult? ResolveStudyAndSingleSiteFilters(
        HttpContext? httpContext,
        string? requestedStudyId,
        string? requestedSiteId,
        out HashSet<string>? allowedStudies,
        out HashSet<string>? allowedSites,
        out string? effectiveSingleSiteId,
        string? code = null)
    {
        allowedSites = null;
        effectiveSingleSiteId = requestedSiteId;

        if (!string.IsNullOrWhiteSpace(requestedStudyId))
        {
            allowedStudies = null;
            if (RequireStudy(httpContext, requestedStudyId, code) is { } studyDenied)
            {
                return studyDenied;
            }
        }
        else if (!ResolveStudyFilter(httpContext, null, out allowedStudies, out var studyError, code))
        {
            allowedStudies = null;
            return studyError;
        }

        return ResolveSingleSiteFilter(httpContext, requestedSiteId, out allowedSites, out effectiveSingleSiteId, code);
    }

    public static IEnumerable<T> ApplyFilters<T>(
        IEnumerable<T> items,
        IReadOnlySet<string>? studyFilter,
        IReadOnlySet<string>? siteFilter,
        Func<T, string?> studySelector,
        Func<T, string?> siteSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(studySelector);
        ArgumentNullException.ThrowIfNull(siteSelector);

        if (studyFilter is not null)
        {
            items = items.Where(item =>
                studySelector(item) is { } study
                && !string.IsNullOrWhiteSpace(study)
                && studyFilter.Contains(study.Trim()));
        }

        if (siteFilter is not null)
        {
            items = items.Where(item =>
                siteSelector(item) is { } site
                && !string.IsNullOrWhiteSpace(site)
                && siteFilter.Contains(site.Trim()));
        }

        return items;
    }

    /// <summary>
    /// Narrows <paramref name="query"/> to rows in the caller's assigned studies. Fails closed to an empty query
    /// when the caller is unauthenticated or has no study assignments, and leaves <paramref name="query"/> unchanged
    /// when the caller holds wildcard (<c>*</c>) study access.
    /// </summary>
    public static IQueryable<T> NarrowToStudyScope<T>(
        IQueryable<T> query,
        HttpContext? httpContext,
        Expression<Func<T, string?>> studySelector) =>
        NarrowToStudyScope(query, ResolvePrincipal(httpContext), studySelector);

    /// <summary>
    /// Narrows <paramref name="query"/> to rows in <paramref name="principal"/>'s assigned studies.
    /// </summary>
    public static IQueryable<T> NarrowToStudyScope<T>(
        IQueryable<T> query,
        Principal? principal,
        Expression<Func<T, string?>> studySelector)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(studySelector);

        if (principal is null)
        {
            return query.Where(_ => false);
        }

        if (principal.AssignedStudies.Any(s =>
                string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal)))
        {
            return query;
        }

        var studies = principal.AssignedStudies
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (studies.Count == 0)
        {
            return query.Where(_ => false);
        }

        Expression<Func<List<string>>> captured = () => studies;
        var notNull = Expression.NotEqual(studySelector.Body, Expression.Constant(null, typeof(string)));
        var containsCall = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(string)],
            captured.Body,
            studySelector.Body);
        var predicate = Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(notNull, containsCall),
            studySelector.Parameters);

        return query.Where(predicate);
    }

    /// <summary>
    /// Narrows <paramref name="query"/> to rows in the caller's assigned studies and, when site-bound,
    /// case-insensitively to their assigned sites. Fails closed to an empty query when unassigned.
    /// </summary>
    public static IQueryable<T> NarrowToScope<T>(
        IQueryable<T> query,
        HttpContext? httpContext,
        Expression<Func<T, string?>> studySelector,
        Expression<Func<T, string?>> siteSelector) =>
        NarrowToScope(query, ResolvePrincipal(httpContext), studySelector, siteSelector);

    /// <summary>
    /// Narrows <paramref name="query"/> to rows in <paramref name="principal"/>'s assigned studies and,
    /// when site-bound, case-insensitively to their assigned sites.
    /// </summary>
    public static IQueryable<T> NarrowToScope<T>(
        IQueryable<T> query,
        Principal? principal,
        Expression<Func<T, string?>> studySelector,
        Expression<Func<T, string?>> siteSelector)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(studySelector);
        ArgumentNullException.ThrowIfNull(siteSelector);

        if (principal is null)
        {
            return query.Where(_ => false);
        }

        query = NarrowToStudyScope(query, principal, studySelector);

        if (!IsSiteBound(principal))
        {
            return query;
        }

        var upperSites = principal.AssignedSites
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpper())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (upperSites.Count == 0)
        {
            return query.Where(_ => false);
        }

        Expression<Func<List<string>>> captured = () => upperSites;
        var trimmed = Expression.Call(siteSelector.Body, StringTrimMethod);
        var uppered = Expression.Call(trimmed, StringToUpperMethod);
        var notNull = Expression.NotEqual(siteSelector.Body, Expression.Constant(null, typeof(string)));
        var containsCall = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(string)],
            captured.Body,
            uppered);
        var predicate = Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(notNull, containsCall),
            siteSelector.Parameters);

        return query.Where(predicate);
    }

    public static ProblemHttpResult Forbidden(
        string detail,
        string title = "Forbidden",
        string? code = null) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            title: title,
            extensions: string.IsNullOrWhiteSpace(code)
                ? null
                : new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult OutsideStudy(
        string detail = OutsideStudyDeniedDetail,
        string? code = null) =>
        Forbidden(detail, OutsideStudyTitle, code);
}
