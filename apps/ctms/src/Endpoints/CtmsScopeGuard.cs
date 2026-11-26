using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.References;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Fail-closed study and site scope facade over <see cref="ClinicalScopeGuard"/> for CTMS Minimal API endpoints,
/// emitting RFC 7807 <see cref="ProblemHttpResult"/> responses with <c>code = "FORBIDDEN_SCOPE"</c> (ADR-2516, ADR-2548, ADR-2642).
/// @req:PRD-SYS-192
/// </summary>
public static class CtmsScopeGuard
{
    public const string ForbiddenCode = "FORBIDDEN_SCOPE";

    public sealed record DoaSiteScopeResult(ProblemHttpResult? Error, HashSet<string> AuthorizedStudies);

    public static Principal? ResolvePrincipal(HttpContext? httpContext) =>
        ClinicalScopeGuard.ResolvePrincipal(httpContext);

    public static bool CanAccessStudy(HttpContext? httpContext, string? studyId) =>
        ClinicalScopeGuard.CanAccessStudy(httpContext, studyId);

    public static bool CanAccessSite(HttpContext? httpContext, string? siteId) =>
        ClinicalScopeGuard.CanAccessSite(httpContext, siteId);

    public static ProblemHttpResult? RequireAnyScopeForLookup(HttpContext? httpContext)
    {
        var principal = ResolvePrincipal(httpContext);
        if (!ClinicalScopeGuard.HasAnyStudyAssignment(principal))
        {
            return Forbidden(ClinicalScopeGuard.StudyAccessDeniedDetail);
        }

        if (!ClinicalScopeGuard.HasValidSiteScopeIfRequired(principal))
        {
            return Forbidden(ClinicalScopeGuard.SiteAccessDeniedDetail, conditionCode: RbacExplanationBuilder.SiteScopeCondition);
        }

        return null;
    }

    public static ProblemHttpResult? RequireStudy(HttpContext? httpContext, string? studyId) =>
        CanAccessStudy(httpContext, studyId)
            ? null
            : Forbidden(
                $"Caller is not authorized for study '{studyId ?? string.Empty}'.",
                "Outside your studies",
                RbacExplanationBuilder.StudyScopeCondition,
                NodeKinds.Study,
                studyId);

    public static ProblemHttpResult? RequireSite(HttpContext? httpContext, string? siteId) =>
        CanAccessSite(httpContext, siteId)
            ? null
            : Forbidden(
                $"Caller is not authorized for site '{siteId ?? string.Empty}'.",
                "Outside your sites",
                RbacExplanationBuilder.SiteScopeCondition,
                NodeKinds.StudySite,
                siteId);

    public static ProblemHttpResult? RequireStudyAndSite(HttpContext? httpContext, string? studyId, string? siteId) =>
        RequireStudy(httpContext, studyId) ?? RequireSite(httpContext, siteId);

    public static ProblemHttpResult? ResolveStudyFilter(
        HttpContext? httpContext,
        string? studyId,
        out HashSet<string>? studyFilter)
    {
        if (ClinicalScopeGuard.ResolveStudyFilter(httpContext, studyId, out studyFilter, out _))
        {
            return null;
        }

        studyFilter = null;
        return string.IsNullOrWhiteSpace(studyId)
            ? Forbidden(ClinicalScopeGuard.StudyAccessDeniedDetail)
            : RequireStudy(httpContext, studyId);
    }

    public static ProblemHttpResult? ResolveSiteFilter(
        HttpContext? httpContext,
        string? siteId,
        out HashSet<string>? siteFilter)
    {
        if (ClinicalScopeGuard.ResolveSiteFilter(httpContext, siteId, out siteFilter, out _))
        {
            return null;
        }

        siteFilter = null;
        return string.IsNullOrWhiteSpace(siteId)
            ? Forbidden(ClinicalScopeGuard.SiteAccessDeniedDetail, "Outside your sites", RbacExplanationBuilder.SiteScopeCondition)
            : RequireSite(httpContext, siteId);
    }

    public static ProblemHttpResult? ResolveStudyAndSiteFilters(
        HttpContext? httpContext,
        string? studyId,
        string? siteId,
        out HashSet<string>? studyFilter,
        out HashSet<string>? siteFilter)
    {
        siteFilter = null;
        return ResolveStudyFilter(httpContext, studyId, out studyFilter)
            ?? ResolveSiteFilter(httpContext, siteId, out siteFilter);
    }

    public static List<T> ApplyFilters<T>(
        IEnumerable<T> items,
        HashSet<string>? studyFilter,
        Func<T, string?> studySelector,
        HashSet<string>? siteFilter,
        Func<T, string?> siteSelector) =>
        ClinicalScopeGuard.ApplyFilters(items, studyFilter, siteFilter, studySelector, siteSelector)
            .ToList();

    public static ProblemHttpResult Forbidden(
        string detail,
        string title = "Forbidden",
        string conditionCode = RbacExplanationBuilder.StudyScopeCondition,
        string? targetKind = null,
        string? targetIdentifier = null) =>
        ClinicalScopeGuard.Forbidden(detail, title, ForbiddenCode, conditionCode, targetKind, targetIdentifier);
}
