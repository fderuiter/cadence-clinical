using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Execution;

/// <summary>
/// Tenant-scoped, untracked reads over sites, study sites, artifacts, and protocol effectivities (ADR-2250).
/// </summary>
internal sealed class KernelStudyGovernanceReader(KernelTenantDbContextFactory contexts) : IStudyGovernanceReader
{
    public async Task<IReadOnlyList<SiteDirectoryEntry>> ListSitesAsync(TenantId tenant, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        return await (
                from site in db.Sites.AsNoTracking()
                join org in db.Organizations.AsNoTracking() on new { site.TenantId, Id = site.InstitutionId } equals new { org.TenantId, org.Id }
                where site.TenantId == tenant
                orderby site.SiteCode
                select new SiteDirectoryEntry(
                    site.Id,
                    site.VersionIndex,
                    org.Id,
                    org.Code,
                    org.Name,
                    site.SiteCode,
                    site.Name,
                    site.Country))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SiteDirectoryEntry?> FindSiteAsync(TenantId tenant, SiteId siteId, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        return await (
                from site in db.Sites.AsNoTracking()
                join org in db.Organizations.AsNoTracking() on new { site.TenantId, Id = site.InstitutionId } equals new { org.TenantId, org.Id }
                where site.TenantId == tenant && site.Id == siteId
                select new SiteDirectoryEntry(
                    site.Id,
                    site.VersionIndex,
                    org.Id,
                    org.Code,
                    org.Name,
                    site.SiteCode,
                    site.Name,
                    site.Country))
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
                from site in db.Sites.AsNoTracking()
                join org in db.Organizations.AsNoTracking() on new { site.TenantId, Id = site.InstitutionId } equals new { org.TenantId, org.Id }
                where site.TenantId == tenant && site.SiteCode.ToUpper() == normalized
                orderby site.SiteCode
                select new SiteDirectoryEntry(
                    site.Id,
                    site.VersionIndex,
                    org.Id,
                    org.Code,
                    org.Name,
                    site.SiteCode,
                    site.Name,
                    site.Country))
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
                join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                join org in db.Organizations.AsNoTracking() on new { site.TenantId, Id = site.InstitutionId } equals new { org.TenantId, org.Id }
                where ss.TenantId == tenant && ss.StudyId == studyId
                orderby site.SiteCode
                select new StudySiteDirectoryEntry(
                    ss.Id,
                    ss.VersionIndex,
                    study.Id,
                    study.StudyCode,
                    site.Id,
                    site.SiteCode,
                    site.Name,
                    site.Country,
                    org.Id,
                    org.Name,
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
                join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                join org in db.Organizations.AsNoTracking() on new { site.TenantId, Id = site.InstitutionId } equals new { org.TenantId, org.Id }
                where ss.TenantId == tenant && ss.Id == studySiteId
                select new StudySiteDirectoryEntry(
                    ss.Id,
                    ss.VersionIndex,
                    study.Id,
                    study.StudyCode,
                    site.Id,
                    site.SiteCode,
                    site.Name,
                    site.Country,
                    org.Id,
                    org.Name,
                    ss.Status))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<StudySiteDirectoryEntry?> FindStudySiteByCodeAsync(
        TenantId tenant,
        string studyCode,
        string siteCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studyCode) || string.IsNullOrWhiteSpace(siteCode))
        {
            return null;
        }

        var normalizedStudy = studyCode.Trim().ToUpperInvariant();
        var normalizedSite = siteCode.Trim().ToUpperInvariant();
        await using var db = contexts.Create(tenant);
        return await (
                from ss in db.StudySites.AsNoTracking()
                join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                join org in db.Organizations.AsNoTracking() on new { site.TenantId, Id = site.InstitutionId } equals new { org.TenantId, org.Id }
                where ss.TenantId == tenant
                      && study.StudyCode.ToUpper() == normalizedStudy
                      && site.SiteCode.ToUpper() == normalizedSite
                select new StudySiteDirectoryEntry(
                    ss.Id,
                    ss.VersionIndex,
                    study.Id,
                    study.StudyCode,
                    site.Id,
                    site.SiteCode,
                    site.Name,
                    site.Country,
                    org.Id,
                    org.Name,
                    ss.Status))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ArtifactDirectoryEntry>> ListStudyArtifactsAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var artifacts = await db.Artifacts.AsNoTracking()
            .Include(a => a.Versions)
            .Where(a => a.TenantId == tenant && a.StudyId == studyId)
            .OrderBy(a => a.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. artifacts.Where(a => a.Versions.Count > 0).Select(MapArtifact)];
    }

    public async Task<ArtifactDirectoryEntry?> FindArtifactAsync(
        TenantId tenant,
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var artifact = await db.Artifacts.AsNoTracking()
            .Include(a => a.Versions)
            .FirstOrDefaultAsync(a => a.TenantId == tenant && a.Id == artifactId, cancellationToken)
            .ConfigureAwait(false);

        return artifact is { Versions.Count: > 0 } ? MapArtifact(artifact) : null;
    }

    public async Task<IReadOnlyList<EffectivityDirectoryEntry>> ListStudyEffectivitiesAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var rows = await (
                from eff in db.Effectivities.AsNoTracking().Include(e => e.Periods)
                join pv in db.ProtocolVersions.AsNoTracking() on new { eff.TenantId, Id = eff.ProtocolVersionId } equals new { pv.TenantId, pv.Id }
                where eff.TenantId == tenant && eff.StudyId == studyId
                orderby pv.Number descending, eff.StatusSince descending
                select new { Effectivity = eff, ProtocolNumber = pv.Number })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(r => MapEffectivity(r.Effectivity, r.ProtocolNumber))];
    }

    public async Task<EffectivityDirectoryEntry?> FindEffectivityAsync(
        TenantId tenant,
        EffectivityId effectivityId,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var row = await (
                from eff in db.Effectivities.AsNoTracking().Include(e => e.Periods)
                join pv in db.ProtocolVersions.AsNoTracking() on new { eff.TenantId, Id = eff.ProtocolVersionId } equals new { pv.TenantId, pv.Id }
                where eff.TenantId == tenant && eff.Id == effectivityId
                select new { Effectivity = eff, ProtocolNumber = pv.Number })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : MapEffectivity(row.Effectivity, row.ProtocolNumber);
    }

    private static ArtifactDirectoryEntry MapArtifact(Artifact artifact)
    {
        var versions = artifact.Versions
            .OrderBy(v => v.Number)
            .Select(v => new ArtifactVersionDirectoryEntry(
                v.Id,
                v.Number,
                v.ContentHash,
                v.SizeBytes,
                v.MediaType,
                v.StorageRef,
                v.RecordedAt))
            .ToList();

        return new ArtifactDirectoryEntry(
            artifact.Id,
            artifact.VersionIndex,
            artifact.Kind,
            artifact.StudyId,
            artifact.Title,
            versions[^1],
            versions);
    }

    private static EffectivityDirectoryEntry MapEffectivity(Effectivity effectivity, int protocolVersionNumber)
    {
        IReadOnlyList<ArtifactVersionId> evidence = effectivity.Periods.Count > 0
            ? effectivity.CurrentEvidence()
            : [];
        return new EffectivityDirectoryEntry(
            effectivity.Id,
            effectivity.VersionIndex,
            effectivity.StudyId,
            effectivity.ProtocolVersionId,
            protocolVersionNumber,
            effectivity.Scope,
            effectivity.Status,
            effectivity.StatusSince,
            evidence);
    }
}
