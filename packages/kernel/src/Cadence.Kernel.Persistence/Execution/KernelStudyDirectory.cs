using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Execution;

/// <summary>
/// Reads studies and organizations through a context bound to the caller's tenant (ADR-2250). Read-only and untracked.
/// </summary>
internal sealed class KernelStudyDirectory(KernelTenantDbContextFactory contexts) : IStudyDirectory
{
    public async Task<IReadOnlyList<StudyDirectoryEntry>> ListStudiesAsync(TenantId tenant, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var rows = await (
                from s in db.Studies.AsNoTracking()
                join o in db.Organizations.AsNoTracking() on new { s.TenantId, Id = s.SponsorId } equals new { o.TenantId, o.Id }
                where s.TenantId == tenant
                orderby s.StudyCode
                select new StudyDirectoryEntry(
                    s.Id,
                    s.VersionIndex,
                    s.StudyCode,
                    s.Title,
                    s.Phase,
                    o.Id,
                    o.VersionIndex,
                    o.Code,
                    o.Name,
                    db.StudySites.Count(ss => ss.TenantId == tenant && ss.StudyId == s.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows;
    }

    public async Task<StudyDirectoryEntry?> FindStudyAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        return await (
                from s in db.Studies.AsNoTracking()
                join o in db.Organizations.AsNoTracking() on new { s.TenantId, Id = s.SponsorId } equals new { o.TenantId, o.Id }
                where s.TenantId == tenant && s.Id == studyId
                select new StudyDirectoryEntry(
                    s.Id,
                    s.VersionIndex,
                    s.StudyCode,
                    s.Title,
                    s.Phase,
                    o.Id,
                    o.VersionIndex,
                    o.Code,
                    o.Name,
                    db.StudySites.Count(ss => ss.TenantId == tenant && ss.StudyId == s.Id)))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<StudyDirectoryEntry?> FindStudyByCodeAsync(TenantId tenant, string studyCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studyCode))
        {
            return null;
        }

        var normalized = studyCode.Trim().ToUpperInvariant();
        await using var db = contexts.Create(tenant);
        return await (
                from s in db.Studies.AsNoTracking()
                join o in db.Organizations.AsNoTracking() on new { s.TenantId, Id = s.SponsorId } equals new { o.TenantId, o.Id }
                where s.TenantId == tenant && s.StudyCode.ToUpper() == normalized
                select new StudyDirectoryEntry(
                    s.Id,
                    s.VersionIndex,
                    s.StudyCode,
                    s.Title,
                    s.Phase,
                    o.Id,
                    o.VersionIndex,
                    o.Code,
                    o.Name,
                    db.StudySites.Count(ss => ss.TenantId == tenant && ss.StudyId == s.Id)))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OrganizationDirectoryEntry>> ListOrganizationsAsync(TenantId tenant, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        return await db.Organizations.AsNoTracking()
            .Where(o => o.TenantId == tenant)
            .OrderBy(o => o.Name)
            .ThenBy(o => o.Code)
            .Select(o => new OrganizationDirectoryEntry(o.Id, o.VersionIndex, o.Code, o.Name, o.Kind))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OrganizationDirectoryEntry?> FindOrganizationAsync(
        TenantId tenant,
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        return await db.Organizations.AsNoTracking()
            .Where(o => o.TenantId == tenant && o.Id == organizationId)
            .Select(o => new OrganizationDirectoryEntry(o.Id, o.VersionIndex, o.Code, o.Name, o.Kind))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OrganizationDirectoryEntry?> FindOrganizationByCodeAsync(
        TenantId tenant,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalized = code.Trim().ToUpperInvariant();
        await using var db = contexts.Create(tenant);
        return await db.Organizations.AsNoTracking()
            .Where(o => o.TenantId == tenant && o.Code.ToUpper() == normalized)
            .Select(o => new OrganizationDirectoryEntry(o.Id, o.VersionIndex, o.Code, o.Name, o.Kind))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SiteDirectoryEntry>> ListSitesAsync(TenantId tenant, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        return await (
                from s in db.Sites.AsNoTracking()
                join o in db.Organizations.AsNoTracking() on new { s.TenantId, Id = s.InstitutionId } equals new { o.TenantId, o.Id }
                where s.TenantId == tenant && s.IsActive
                orderby s.SiteCode
                select new SiteDirectoryEntry(
                    s.Id,
                    s.VersionIndex,
                    o.Id,
                    o.VersionIndex,
                    o.Code,
                    o.Name,
                    s.SiteCode,
                    s.Name,
                    s.Country))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SiteDirectoryEntry?> FindSiteAsync(TenantId tenant, SiteId siteId, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        return await (
                from s in db.Sites.AsNoTracking()
                join o in db.Organizations.AsNoTracking() on new { s.TenantId, Id = s.InstitutionId } equals new { o.TenantId, o.Id }
                where s.TenantId == tenant && s.Id == siteId && s.IsActive
                select new SiteDirectoryEntry(
                    s.Id,
                    s.VersionIndex,
                    o.Id,
                    o.VersionIndex,
                    o.Code,
                    o.Name,
                    s.SiteCode,
                    s.Name,
                    s.Country))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SiteDirectoryEntry?> FindSiteByCodeAsync(TenantId tenant, string siteCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(siteCode))
        {
            return null;
        }

        var normalized = siteCode.Trim().ToUpperInvariant();
        await using var db = contexts.Create(tenant);
        return await (
                from s in db.Sites.AsNoTracking()
                join o in db.Organizations.AsNoTracking() on new { s.TenantId, Id = s.InstitutionId } equals new { o.TenantId, o.Id }
                where s.TenantId == tenant && s.IsActive && s.SiteCode.ToUpper() == normalized
                orderby s.SiteCode, s.Id
                select new SiteDirectoryEntry(
                    s.Id,
                    s.VersionIndex,
                    o.Id,
                    o.VersionIndex,
                    o.Code,
                    o.Name,
                    s.SiteCode,
                    s.Name,
                    s.Country))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StudySiteDirectoryEntry>> ListStudySitesAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        return await (
                from ss in db.StudySites.AsNoTracking()
                join s in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { s.TenantId, s.Id }
                join o in db.Organizations.AsNoTracking() on new { s.TenantId, Id = s.InstitutionId } equals new { o.TenantId, o.Id }
                where ss.TenantId == tenant && ss.StudyId == studyId && ss.IsActive
                orderby s.SiteCode
                select new StudySiteDirectoryEntry(
                    ss.Id,
                    ss.VersionIndex,
                    ss.StudyId,
                    s.Id,
                    s.VersionIndex,
                    s.SiteCode,
                    s.Name,
                    s.Country,
                    o.Id,
                    o.VersionIndex,
                    o.Code,
                    o.Name,
                    ss.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<StudySiteDirectoryEntry?> FindStudySiteAsync(
        TenantId tenant,
        StudySiteId studySiteId,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        return await (
                from ss in db.StudySites.AsNoTracking()
                join s in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { s.TenantId, s.Id }
                join o in db.Organizations.AsNoTracking() on new { s.TenantId, Id = s.InstitutionId } equals new { o.TenantId, o.Id }
                where ss.TenantId == tenant && ss.Id == studySiteId && ss.IsActive
                select new StudySiteDirectoryEntry(
                    ss.Id,
                    ss.VersionIndex,
                    ss.StudyId,
                    s.Id,
                    s.VersionIndex,
                    s.SiteCode,
                    s.Name,
                    s.Country,
                    o.Id,
                    o.VersionIndex,
                    o.Code,
                    o.Name,
                    ss.Status))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
