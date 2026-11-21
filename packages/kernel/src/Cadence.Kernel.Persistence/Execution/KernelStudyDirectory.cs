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
}
