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
/// Fail-closed study and site scope facade over <see cref="ClinicalScopeGuard"/> for CTMS Minimal API endpoints,
/// emitting RFC 7807 <see cref="ProblemHttpResult"/> responses with <c>code = "FORBIDDEN_SCOPE"</c> (ADR-2516, ADR-2548, ADR-2642).
/// @req:PRD-SYS-192
/// </summary>
public static class CtmsScopeGuard
{
    public const string ForbiddenCode = "FORBIDDEN_SCOPE";

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
            return Forbidden(ClinicalScopeGuard.SiteAccessDeniedDetail);
        }

        return null;
    }

    public static ProblemHttpResult? RequireStudy(HttpContext? httpContext, string? studyId) =>
        CanAccessStudy(httpContext, studyId)
            ? null
            : Forbidden($"Caller is not authorized for study '{studyId ?? string.Empty}'.", "Outside your studies");

    public static ProblemHttpResult? RequireSite(HttpContext? httpContext, string? siteId) =>
        CanAccessSite(httpContext, siteId)
            ? null
            : Forbidden($"Caller is not authorized for site '{siteId ?? string.Empty}'.", "Outside your sites");

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
        if (ClinicalScopeGuard.ResolveSiteFilter(httpContext, siteId, out var allowedSites, out _))
        {
            siteFilter = allowedSites is null
                ? null
                : new HashSet<string>(allowedSites, StringComparer.OrdinalIgnoreCase);
            return null;
        }

        siteFilter = null;
        return string.IsNullOrWhiteSpace(siteId)
            ? Forbidden(ClinicalScopeGuard.SiteAccessDeniedDetail, "Outside your sites")
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

    public sealed record DoaSiteScopeResult(ProblemHttpResult? Error, HashSet<string> AuthorizedStudies);

    public static async Task<ProblemHttpResult?> RequireDoaSiteScopeAsync(
        HttpContext? httpContext,
        CtmsDbContext db,
        string? siteId,
        CancellationToken ct,
        string? studyId = null)
    {
        var result = await ResolveDoaSiteScopeAsync(httpContext, db, siteId, studyId, ct);
        return result.Error;
    }

    public static async Task<DoaSiteScopeResult> ResolveDoaSiteScopeAsync(
        HttpContext? httpContext,
        CtmsDbContext db,
        string? siteId,
        string? studyId,
        CancellationToken ct)
    {
        if (RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return new DoaSiteScopeResult(missingScope, []);
        }

        if (!string.IsNullOrWhiteSpace(siteId) && RequireSite(httpContext, siteId) is { } siteDenied)
        {
            return new DoaSiteScopeResult(siteDenied, []);
        }

        if (!string.IsNullOrWhiteSpace(studyId) && RequireStudy(httpContext, studyId) is { } studyDenied)
        {
            return new DoaSiteScopeResult(studyDenied, []);
        }

        if (string.IsNullOrWhiteSpace(siteId) || !await DoaEndpoints.SiteExistsAsync(db, siteId.Trim(), ct))
        {
            var notFound = TypedResults.Problem(
                title: "Site not found",
                detail: $"Site '{siteId}' does not exist.",
                statusCode: StatusCodes.Status404NotFound);
            return new DoaSiteScopeResult(notFound, []);
        }

        string trimmedSite = siteId.Trim();
        var knownStudies = await GetKnownStudiesForSiteAsync(db, trimmedSite, ct);

        var authorizedStudies = knownStudies
            .Where(study => CanAccessStudy(httpContext, study))
            .ToHashSet(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            string trimmedStudy = studyId.Trim();
            authorizedStudies.RemoveWhere(study => !string.Equals(study, trimmedStudy, StringComparison.Ordinal));
        }

        if (authorizedStudies.Count == 0)
        {
            var forbidden = Forbidden(
                $"Caller is not authorized for the study governing site '{trimmedSite}'.",
                "Outside your studies");
            return new DoaSiteScopeResult(forbidden, []);
        }

        return new DoaSiteScopeResult(null, authorizedStudies);
    }

    public static async Task<HashSet<string>> GetKnownStudiesForSiteAsync(
        CtmsDbContext db,
        string trimmedSite,
        CancellationToken ct)
    {
        var studies = new HashSet<string>(StringComparer.Ordinal);

        var gateStudies = await db.CtmsSiteGreenlightGates.AsNoTracking()
            .Where(g => g.SiteId == trimmedSite && g.StudyId != string.Empty)
            .Select(g => g.StudyId)
            .Distinct()
            .ToListAsync(ct);
        AddNonEmpty(studies, gateStudies);

        var milestoneStudies = await db.CtmsSiteMilestones.AsNoTracking()
            .Where(m => m.SiteId == trimmedSite && m.StudyId != string.Empty)
            .Select(m => m.StudyId)
            .Distinct()
            .ToListAsync(ct);
        AddNonEmpty(studies, milestoneStudies);

        var visitStudies = await db.CtmsMonitoringVisits.AsNoTracking()
            .Where(v => v.SiteId == trimmedSite && v.StudyId != string.Empty)
            .Select(v => v.StudyId)
            .Distinct()
            .ToListAsync(ct);
        AddNonEmpty(studies, visitStudies);

        var craStudies = await db.CtmsCraAllocations.AsNoTracking()
            .Where(a => a.SiteId == trimmedSite && a.StudyId != string.Empty)
            .Select(a => a.StudyId)
            .Distinct()
            .ToListAsync(ct);
        AddNonEmpty(studies, craStudies);

        var delegationStudies = await db.CtmsDelegations.AsNoTracking()
            .Where(d => d.SiteId == trimmedSite && d.StudyId != string.Empty)
            .Select(d => d.StudyId)
            .Distinct()
            .ToListAsync(ct);
        AddNonEmpty(studies, delegationStudies);

        var queryStudies = await db.CtmsClinicalQueries.AsNoTracking()
            .Where(q => q.SiteId == trimmedSite && q.StudyId != string.Empty)
            .Select(q => q.StudyId)
            .Distinct()
            .ToListAsync(ct);
        AddNonEmpty(studies, queryStudies);

        var grantStudies = await db.CtmsInvestigatorGrants.AsNoTracking()
            .Where(g => g.SiteId == trimmedSite && g.StudyId != string.Empty)
            .Select(g => g.StudyId)
            .Distinct()
            .ToListAsync(ct);
        AddNonEmpty(studies, grantStudies);

        var recruitmentStudies = await db.CtmsRecruitmentRecords.AsNoTracking()
            .Where(r => r.SiteId == trimmedSite && r.StudyId != string.Empty)
            .Select(r => r.StudyId)
            .Distinct()
            .ToListAsync(ct);
        AddNonEmpty(studies, recruitmentStudies);

        var regulatoryStudies = await db.CtmsRegulatoryForms.AsNoTracking()
            .Where(f => f.SiteId == trimmedSite && f.StudyId != string.Empty)
            .Select(f => f.StudyId)
            .Distinct()
            .ToListAsync(ct);
        AddNonEmpty(studies, regulatoryStudies);

        var letterStudies = await db.CtmsGeneratedLetters.AsNoTracking()
            .Where(l => l.SiteId == trimmedSite && l.StudyId != string.Empty)
            .Select(l => l.StudyId)
            .Distinct()
            .ToListAsync(ct);
        AddNonEmpty(studies, letterStudies);

        return studies;
    }

    private static void AddNonEmpty(HashSet<string> target, IEnumerable<string> values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Add(value.Trim());
            }
        }
    }

    public static List<T> ApplyFilters<T>(
        IEnumerable<T> items,
        HashSet<string>? studyFilter,
        Func<T, string?> studySelector,
        HashSet<string>? siteFilter,
        Func<T, string?> siteSelector) =>
        ClinicalScopeGuard.ApplyFilters(
                items,
                studyFilter,
                siteFilter,
                item => studySelector(item)?.Trim(),
                item => siteSelector(item) is { } site && !string.IsNullOrWhiteSpace(site) ? site.Trim() : null)
            .ToList();

    public static ProblemHttpResult Forbidden(string detail, string title = "Forbidden") =>
        TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            title: title,
            extensions: new Dictionary<string, object?> { ["code"] = ForbiddenCode });
}
