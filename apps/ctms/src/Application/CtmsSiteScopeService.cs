using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Kernel.References;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Application;

public interface ICtmsSiteScopeService
{
    Task<bool> SiteExistsAsync(string siteId, CancellationToken ct);

    Task<HashSet<string>> GetKnownStudiesForSiteAsync(string trimmedSite, CancellationToken ct);

    Task<ProblemHttpResult?> RequireDoaSiteScopeAsync(
        HttpContext? httpContext,
        string? siteId,
        CancellationToken ct,
        string? studyId = null);

    Task<CtmsScopeGuard.DoaSiteScopeResult> ResolveDoaSiteScopeAsync(
        HttpContext? httpContext,
        string? siteId,
        string? studyId,
        CancellationToken ct);
}

public sealed class CtmsSiteScopeService(CtmsDbContext db) : ICtmsSiteScopeService
{
    public async Task<bool> SiteExistsAsync(string siteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(siteId))
        {
            return false;
        }

        string trimmedSite = siteId.Trim();
        return await db.CtmsSiteGreenlightGates.AnyAsync(g => g.SiteId == trimmedSite, ct)
            || await db.CtmsDelegations.AnyAsync(d => d.SiteId == trimmedSite, ct)
            || await db.CtmsSiteMilestones.AnyAsync(m => m.SiteId == trimmedSite, ct)
            || await db.CtmsMonitoringVisits.AnyAsync(v => v.SiteId == trimmedSite, ct)
            || await db.CtmsCraAllocations.AnyAsync(a => a.SiteId == trimmedSite, ct)
            || await db.CtmsClinicalQueries.AnyAsync(q => q.SiteId == trimmedSite, ct)
            || await db.CtmsInvestigatorGrants.AnyAsync(g => g.SiteId == trimmedSite, ct)
            || await db.CtmsRecruitmentRecords.AnyAsync(r => r.SiteId == trimmedSite, ct)
            || await db.CtmsRegulatoryForms.AnyAsync(f => f.SiteId == trimmedSite, ct)
            || await db.CtmsGeneratedLetters.AnyAsync(l => l.SiteId == trimmedSite, ct)
            || await db.DoaDelegationRecords.AnyAsync(r => r.SiteId == trimmedSite, ct);
    }

    public async Task<ProblemHttpResult?> RequireDoaSiteScopeAsync(
        HttpContext? httpContext,
        string? siteId,
        CancellationToken ct,
        string? studyId = null)
    {
        var result = await ResolveDoaSiteScopeAsync(httpContext, siteId, studyId, ct);
        return result.Error;
    }

    public async Task<CtmsScopeGuard.DoaSiteScopeResult> ResolveDoaSiteScopeAsync(
        HttpContext? httpContext,
        string? siteId,
        string? studyId,
        CancellationToken ct)
    {
        if (CtmsScopeGuard.RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return new CtmsScopeGuard.DoaSiteScopeResult(missingScope, []);
        }

        if (!string.IsNullOrWhiteSpace(siteId) && CtmsScopeGuard.RequireSite(httpContext, siteId) is { } siteDenied)
        {
            return new CtmsScopeGuard.DoaSiteScopeResult(siteDenied, []);
        }

        if (!string.IsNullOrWhiteSpace(studyId) && CtmsScopeGuard.RequireStudy(httpContext, studyId) is { } studyDenied)
        {
            return new CtmsScopeGuard.DoaSiteScopeResult(studyDenied, []);
        }

        if (string.IsNullOrWhiteSpace(siteId) || !await SiteExistsAsync(siteId.Trim(), ct))
        {
            var notFound = TypedResults.Problem(
                title: "Site not found",
                detail: $"Site '{siteId}' does not exist.",
                statusCode: StatusCodes.Status404NotFound);
            return new CtmsScopeGuard.DoaSiteScopeResult(notFound, []);
        }

        string trimmedSite = siteId.Trim();
        var knownStudies = await GetKnownStudiesForSiteAsync(trimmedSite, ct);

        var authorizedStudies = knownStudies
            .Where(study => CtmsScopeGuard.CanAccessStudy(httpContext, study))
            .ToHashSet(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            string trimmedStudy = studyId.Trim();
            authorizedStudies.RemoveWhere(study => !string.Equals(study, trimmedStudy, StringComparison.Ordinal));
        }

        if (authorizedStudies.Count == 0)
        {
            var forbidden = CtmsScopeGuard.Forbidden(
                $"Caller is not authorized for the study governing site '{trimmedSite}'.",
                "Outside your studies",
                RbacExplanationBuilder.StudyScopeCondition,
                NodeKinds.StudySite,
                trimmedSite);
            return new CtmsScopeGuard.DoaSiteScopeResult(forbidden, []);
        }

        return new CtmsScopeGuard.DoaSiteScopeResult(null, authorizedStudies);
    }

    public async Task<HashSet<string>> GetKnownStudiesForSiteAsync(string trimmedSite, CancellationToken ct)
    {
        var studies = new HashSet<string>(StringComparer.Ordinal);

        AddNonEmpty(studies, await db.CtmsSiteGreenlightGates.AsNoTracking()
            .Where(g => g.SiteId == trimmedSite && g.StudyId != string.Empty)
            .Select(g => g.StudyId).Distinct().ToListAsync(ct));
        AddNonEmpty(studies, await db.CtmsSiteMilestones.AsNoTracking()
            .Where(m => m.SiteId == trimmedSite && m.StudyId != string.Empty)
            .Select(m => m.StudyId).Distinct().ToListAsync(ct));
        AddNonEmpty(studies, await db.CtmsMonitoringVisits.AsNoTracking()
            .Where(v => v.SiteId == trimmedSite && v.StudyId != string.Empty)
            .Select(v => v.StudyId).Distinct().ToListAsync(ct));
        AddNonEmpty(studies, await db.CtmsCraAllocations.AsNoTracking()
            .Where(a => a.SiteId == trimmedSite && a.StudyId != string.Empty)
            .Select(a => a.StudyId).Distinct().ToListAsync(ct));
        AddNonEmpty(studies, await db.CtmsDelegations.AsNoTracking()
            .Where(d => d.SiteId == trimmedSite && d.StudyId != string.Empty)
            .Select(d => d.StudyId).Distinct().ToListAsync(ct));
        AddNonEmpty(studies, await db.CtmsClinicalQueries.AsNoTracking()
            .Where(q => q.SiteId == trimmedSite && q.StudyId != string.Empty)
            .Select(q => q.StudyId).Distinct().ToListAsync(ct));
        AddNonEmpty(studies, await db.CtmsInvestigatorGrants.AsNoTracking()
            .Where(g => g.SiteId == trimmedSite && g.StudyId != string.Empty)
            .Select(g => g.StudyId).Distinct().ToListAsync(ct));
        AddNonEmpty(studies, await db.CtmsRecruitmentRecords.AsNoTracking()
            .Where(r => r.SiteId == trimmedSite && r.StudyId != string.Empty)
            .Select(r => r.StudyId).Distinct().ToListAsync(ct));
        AddNonEmpty(studies, await db.CtmsRegulatoryForms.AsNoTracking()
            .Where(f => f.SiteId == trimmedSite && f.StudyId != string.Empty)
            .Select(f => f.StudyId).Distinct().ToListAsync(ct));
        AddNonEmpty(studies, await db.CtmsGeneratedLetters.AsNoTracking()
            .Where(l => l.SiteId == trimmedSite && l.StudyId != string.Empty)
            .Select(l => l.StudyId).Distinct().ToListAsync(ct));

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
}
