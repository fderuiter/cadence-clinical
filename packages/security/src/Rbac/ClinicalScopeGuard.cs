using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;

namespace Cadence.Security.Rbac;

/// <summary>
/// Shared fail-closed tenant, study, and site scope guard for standalone clinical service handlers.
/// Promoted from <c>ExecutionScopeGuard</c> so every standalone service enforces identical fail-closed
/// tenant, study, and site boundaries (PRD-CRF-013, PRD-SYS-053, ADR-2514, ADR-2516).
/// </summary>
public static class ClinicalScopeGuard
{
    public const string TenantAccessDeniedDetail = "The caller does not carry an active verified tenant scope.";
    public const string StudyAccessDeniedDetail = "The caller is not assigned to the requested study.";
    public const string SiteAccessDeniedDetail = "The caller is not assigned to the requested site.";

    /// <summary>
    /// Resolves the caller <see cref="Principal"/> from <see cref="GatewayAuthenticationHandler.PrincipalItemKey"/>,
    /// falling back to authenticated <see cref="ClaimsPrincipal"/> claims for direct handler tests.
    /// </summary>
    public static Principal? ResolvePrincipal(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        if (CallerScope.Principal(httpContext) is { } gatewayPrincipal)
        {
            return gatewayPrincipal;
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

    public static IResult? RequireTenant(HttpContext? httpContext, out string tenantId)
    {
        var principal = ResolvePrincipal(httpContext);
        if (principal?.TenantId is { Length: > 0 } resolved && !string.IsNullOrWhiteSpace(resolved))
        {
            tenantId = resolved.Trim();
            return null;
        }

        tenantId = string.Empty;
        return Forbidden(TenantAccessDeniedDetail);
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

    public static IResult? RequireAnyStudyScope(HttpContext? httpContext)
    {
        var principal = ResolvePrincipal(httpContext);
        if (!HasAnyStudyAssignment(principal))
        {
            return Forbidden(StudyAccessDeniedDetail);
        }

        return null;
    }

    public static IResult? RequireAnyStudyAndSiteScope(HttpContext? httpContext)
    {
        var principal = ResolvePrincipal(httpContext);
        if (!HasAnyStudyAssignment(principal))
        {
            return Forbidden(StudyAccessDeniedDetail);
        }

        if (!HasValidSiteScopeIfRequired(principal))
        {
            return Forbidden(SiteAccessDeniedDetail);
        }

        return null;
    }

    public static IResult? RequireAllStudies(HttpContext? httpContext)
    {
        var principal = ResolvePrincipal(httpContext);
        if (principal is null
            || !principal.AssignedStudies.Any(s =>
                string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal)))
        {
            return Forbidden(StudyAccessDeniedDetail);
        }

        return null;
    }

    public static IResult? RequireStudy(HttpContext? httpContext, string? studyId)
    {
        var principal = ResolvePrincipal(httpContext);
        if (!StudyAccessEvaluator.CanAccessStudy(principal, studyId))
        {
            return Forbidden(StudyAccessDeniedDetail);
        }

        return null;
    }

    public static IResult? RequireSite(HttpContext? httpContext, string? siteId)
    {
        var principal = ResolvePrincipal(httpContext);
        if (!SiteAccessEvaluator.CanAccessSite(principal, siteId))
        {
            return Forbidden(SiteAccessDeniedDetail);
        }

        return null;
    }

    public static IResult? RequireStudyAndSite(HttpContext? httpContext, string? studyId, string? siteId)
    {
        if (RequireStudy(httpContext, studyId) is { } studyDenied)
        {
            return studyDenied;
        }

        return RequireSite(httpContext, siteId);
    }

    public static IResult? RequireStudyAndOptionalSite(
        HttpContext? httpContext,
        string? studyId,
        string? siteId,
        bool allowUnassignedSiteForScopedCaller = false)
    {
        var principal = ResolvePrincipal(httpContext);
        if (!StudyAccessEvaluator.CanAccessStudy(principal, studyId))
        {
            return Forbidden(StudyAccessDeniedDetail);
        }

        if (string.IsNullOrWhiteSpace(siteId))
        {
            if (IsSiteBound(principal)
                && (!allowUnassignedSiteForScopedCaller || !HasValidSiteScopeIfRequired(principal)))
            {
                return Forbidden(SiteAccessDeniedDetail);
            }

            return null;
        }

        return SiteAccessEvaluator.CanAccessSite(principal, siteId)
            ? null
            : Forbidden(SiteAccessDeniedDetail);
    }

    public static bool ResolveStudyFilter(
        HttpContext? httpContext,
        string? requestedStudyId,
        out HashSet<string>? allowedStudies,
        out IResult? errorResult)
    {
        var principal = ResolvePrincipal(httpContext);
        if (principal is null)
        {
            allowedStudies = [];
            errorResult = Forbidden(StudyAccessDeniedDetail);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestedStudyId))
        {
            string trimmed = requestedStudyId.Trim();
            if (!StudyAccessEvaluator.CanAccessStudy(principal, trimmed))
            {
                allowedStudies = [];
                errorResult = Forbidden(StudyAccessDeniedDetail);
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
            errorResult = Forbidden(StudyAccessDeniedDetail);
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
        out IResult? errorResult)
    {
        var principal = ResolvePrincipal(httpContext);
        if (principal is null)
        {
            allowedSites = [];
            errorResult = Forbidden(SiteAccessDeniedDetail);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestedSiteId))
        {
            string trimmed = requestedSiteId.Trim();
            if (!SiteAccessEvaluator.CanAccessSite(principal, trimmed))
            {
                allowedSites = [];
                errorResult = Forbidden(SiteAccessDeniedDetail);
                return false;
            }

            allowedSites = new HashSet<string>([trimmed], StringComparer.Ordinal);
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
            .ToHashSet(StringComparer.Ordinal);

        if (sites.Count == 0)
        {
            allowedSites = [];
            errorResult = Forbidden(SiteAccessDeniedDetail);
            return false;
        }

        allowedSites = sites;
        errorResult = null;
        return true;
    }

    public static bool RequireStudyAndResolveSiteFilter(
        HttpContext? httpContext,
        string? studyId,
        string? requestedSiteId,
        out HashSet<string>? allowedSites,
        out IResult? errorResult)
    {
        if (RequireStudy(httpContext, studyId) is { } studyDenied)
        {
            allowedSites = [];
            errorResult = studyDenied;
            return false;
        }

        return ResolveSiteFilter(httpContext, requestedSiteId, out allowedSites, out errorResult);
    }

    public static bool ResolveStudyAndSiteFilters(
        HttpContext? httpContext,
        string? requestedStudyId,
        string? requestedSiteId,
        out HashSet<string>? allowedStudies,
        out HashSet<string>? allowedSites,
        out IResult? errorResult)
    {
        if (!ResolveStudyFilter(httpContext, requestedStudyId, out allowedStudies, out errorResult))
        {
            allowedSites = [];
            return false;
        }

        return ResolveSiteFilter(httpContext, requestedSiteId, out allowedSites, out errorResult);
    }

    public static IEnumerable<T> ApplyFilters<T>(
        IEnumerable<T> items,
        HashSet<string>? studyFilter,
        HashSet<string>? siteFilter,
        Func<T, string?> studySelector,
        Func<T, string?> siteSelector)
    {
        if (studyFilter is not null)
        {
            items = items.Where(item => studySelector(item) is { } study && studyFilter.Contains(study));
        }

        if (siteFilter is not null)
        {
            items = items.Where(item => siteSelector(item) is { } site && siteFilter.Contains(site));
        }

        return items;
    }

    public static IResult Forbidden(string detail) =>
        Results.Problem(
            title: "Forbidden",
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden);
}
