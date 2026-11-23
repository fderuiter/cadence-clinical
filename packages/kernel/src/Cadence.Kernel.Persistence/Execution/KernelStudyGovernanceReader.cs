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
/// Reads study artifacts, effectivities, and study-site governance status under tenant Row-Level Security (ADR-2250).
/// Read-only: all queries use <c>AsNoTracking()</c>.
/// </summary>
internal sealed class KernelStudyGovernanceReader(KernelTenantDbContextFactory contexts) : IStudyGovernanceReader
{
    public async Task<IReadOnlyList<StudyArtifactSummary>> ListStudyArtifactsAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var artifacts = await db.Artifacts.AsNoTracking()
            .Include(a => a.Versions)
            .Where(a => a.TenantId == tenant && a.StudyId == studyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. artifacts
            .Where(a => a.Versions.Count > 0)
            .Select(a =>
            {
                var latest = a.LatestVersion;
                return new StudyArtifactSummary(
                    a.Id,
                    a.Kind,
                    studyId,
                    a.Title,
                    latest.Id,
                    latest.Number,
                    latest.ContentHash,
                    latest.SizeBytes,
                    latest.MediaType,
                    latest.StorageRef,
                    latest.RecordedAt);
            })
            .OrderByDescending(a => a.RecordedAt)
            .ThenBy(a => a.Title, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<StudyEffectivitySummary>> ListStudyEffectivitiesAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var effectivities = await db.Effectivities.AsNoTracking()
            .Include(e => e.Periods)
            .Where(e => e.TenantId == tenant && e.StudyId == studyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (effectivities.Count == 0)
        {
            return [];
        }

        var versionIds = effectivities.Select(e => e.ProtocolVersionId).Distinct().ToList();
        var numbers = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && versionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Number, cancellationToken)
            .ConfigureAwait(false);

        return [.. effectivities
            .Select(e => new StudyEffectivitySummary(
                e.Id,
                e.StudyId,
                e.ProtocolVersionId,
                numbers.GetValueOrDefault(e.ProtocolVersionId),
                e.Scope,
                e.Status,
                e.StatusSince,
                e.CurrentEvidence()))
            .OrderByDescending(e => e.ProtocolVersionNumber)
            .ThenBy(e => (int)e.Scope.Level)
            .ThenBy(e => e.StatusSince)];
    }

    public async Task<StudyEffectivitySummary?> FindEffectivityAsync(
        TenantId tenant,
        EffectivityId effectivityId,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var effectivity = await db.Effectivities.AsNoTracking()
            .Include(e => e.Periods)
            .SingleOrDefaultAsync(e => e.TenantId == tenant && e.Id == effectivityId, cancellationToken)
            .ConfigureAwait(false);
        if (effectivity is null)
        {
            return null;
        }

        var versionNumber = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && v.Id == effectivity.ProtocolVersionId)
            .Select(v => v.Number)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new StudyEffectivitySummary(
            effectivity.Id,
            effectivity.StudyId,
            effectivity.ProtocolVersionId,
            versionNumber,
            effectivity.Scope,
            effectivity.Status,
            effectivity.StatusSince,
            effectivity.CurrentEvidence());
    }

    public async Task<IReadOnlyList<StudySiteGovernanceSummary>> ListStudySitesAsync(
        TenantId tenant,
        StudyId studyId,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var siteRows = await (
                from ss in db.StudySites.AsNoTracking()
                join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                where ss.TenantId == tenant && ss.StudyId == studyId
                orderby site.SiteCode
                select new
                {
                    ss.Id,
                    ss.StudyId,
                    ss.SiteId,
                    Code = site.SiteCode,
                    site.Name,
                    site.Country,
                    ss.Status,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (siteRows.Count == 0)
        {
            return [];
        }

        var effectivities = await db.Effectivities.AsNoTracking()
            .Include(e => e.Periods)
            .Where(e => e.TenantId == tenant && e.StudyId == studyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var versionNumbers = effectivities.Count > 0
            ? await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.TenantId == tenant && v.StudyId == studyId)
                .ToDictionaryAsync(v => v.Id, v => v.Number, cancellationToken)
                .ConfigureAwait(false)
            : [];

        return [.. siteRows.Select(row =>
        {
            var deciding = EffectivityResolution.EffectiveAt(effectivities, row.Id, row.Country, validAt, knownAt);
            var effectiveVersions = deciding
                .Select(e => new EffectiveVersion(
                    e.ProtocolVersionId,
                    versionNumbers.GetValueOrDefault(e.ProtocolVersionId),
                    e.Id,
                    e.ScopeLevel))
                .OrderByDescending(v => v.Number)
                .ToList();
            return new StudySiteGovernanceSummary(
                row.Id,
                row.StudyId,
                row.SiteId,
                row.Code,
                row.Name,
                row.Country,
                row.Status,
                effectiveVersions);
        })];
    }

    public async Task<StudySiteGovernanceSummary?> FindStudySiteAsync(
        TenantId tenant,
        StudySiteId studySiteId,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var row = await (
                from ss in db.StudySites.AsNoTracking()
                join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                where ss.TenantId == tenant && ss.Id == studySiteId
                select new
                {
                    ss.Id,
                    ss.StudyId,
                    ss.SiteId,
                    Code = site.SiteCode,
                    site.Name,
                    site.Country,
                    ss.Status,
                })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var effectivities = await db.Effectivities.AsNoTracking()
            .Include(e => e.Periods)
            .Where(e => e.TenantId == tenant && e.StudyId == row.StudyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var deciding = EffectivityResolution.EffectiveAt(effectivities, row.Id, row.Country, validAt, knownAt);
        var versionIds = deciding.Select(e => e.ProtocolVersionId).ToList();
        var versionNumbers = versionIds.Count > 0
            ? await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.TenantId == tenant && versionIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Number, cancellationToken)
                .ConfigureAwait(false)
            : [];

        var effectiveVersions = deciding
            .Select(e => new EffectiveVersion(
                e.ProtocolVersionId,
                versionNumbers.GetValueOrDefault(e.ProtocolVersionId),
                e.Id,
                e.ScopeLevel))
            .OrderByDescending(v => v.Number)
            .ToList();

        return new StudySiteGovernanceSummary(
            row.Id,
            row.StudyId,
            row.SiteId,
            row.Code,
            row.Name,
            row.Country,
            row.Status,
            effectiveVersions);
    }

    public async Task<SiteLookupEntry?> FindSiteByCodeAsync(
        TenantId tenant,
        string siteCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(siteCode))
        {
            return null;
        }

        var normalized = siteCode.Trim().ToUpperInvariant();
        await using var db = contexts.Create(tenant);
        return await db.Sites.AsNoTracking()
            .Where(s => s.TenantId == tenant && s.SiteCode.ToUpper() == normalized)
            .Select(s => new SiteLookupEntry(s.Id, s.InstitutionId, s.SiteCode, s.Name, s.Country))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
