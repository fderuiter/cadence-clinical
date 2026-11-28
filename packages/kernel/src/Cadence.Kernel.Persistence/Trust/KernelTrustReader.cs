using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Trust;

/// <summary>
/// Reads canonical Kernel roles, grants, qualifications, and qualification requirements with full bitemporal period
/// evaluation (<c>K-TIME-1</c>) under the request tenant's Row-Level Security context.
/// </summary>
internal sealed class KernelTrustReader(KernelTenantDbContextFactory contexts) : IKernelTrustReader
{
    public async Task<IReadOnlyList<KernelRoleReadModel>> ListRolesAsync(
        TenantId tenant,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var roles = await db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenant)
            .OrderBy(r => r.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. roles.Select(r => new KernelRoleReadModel(r.Id, r.VersionIndex, r.Code, r.Name, r.Capabilities))];
    }

    public async Task<KernelRoleReadModel?> FindRoleByCodeAsync(
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
        var role = await db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenant && r.Code == normalized, cancellationToken)
            .ConfigureAwait(false);

        return role is null
            ? null
            : new KernelRoleReadModel(role.Id, role.VersionIndex, role.Code, role.Name, role.Capabilities);
    }

    public async Task<IReadOnlyList<KernelGrantReadModel>> ListGrantsAsync(
        TenantId tenant,
        KernelGrantQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validAt = KernelTimestamps.ToStoredPrecision(query.ValidAt);
        var knownAt = KernelTimestamps.ToStoredPrecision(query.KnownAt);

        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var grantsQuery = db.Grants.AsNoTracking()
                .Include(g => g.Periods)
                .Where(g => g.TenantId == tenant);

            if (!string.IsNullOrWhiteSpace(query.GranteeId))
            {
                var grantee = query.GranteeId.Trim();
                grantsQuery = grantsQuery.Where(g => g.GranteeId == grantee);
            }

            var grants = await grantsQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
            if (grants.Count == 0)
            {
                return [];
            }

            var roleIds = grants.Where(g => g.RoleId is not null).Select(g => g.RoleId!.Value).Distinct().ToList();
            var rolesById = roleIds.Count == 0
                ? new Dictionary<RoleId, Role>()
                : await db.Roles.AsNoTracking()
                    .Where(r => r.TenantId == tenant && roleIds.Contains(r.Id))
                    .ToDictionaryAsync(r => r.Id, cancellationToken)
                    .ConfigureAwait(false);

            var studySites = await (
                    from ss in db.StudySites.AsNoTracking()
                    join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                    join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                    where ss.TenantId == tenant
                    select new { ss.Id, ss.StudyId, study.StudyCode, ss.SiteId, site.SiteCode, site.Country })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var studySitesById = studySites.ToDictionary(x => x.Id);

            var studies = await db.Studies.AsNoTracking()
                .Where(s => s.TenantId == tenant)
                .Select(s => new { s.Id, s.StudyCode })
                .ToDictionaryAsync(s => s.Id, s => s.StudyCode, cancellationToken)
                .ConfigureAwait(false);

            var result = new List<KernelGrantReadModel>(grants.Count);
            foreach (var grant in grants)
            {
                Role? role = grant.RoleId is { } rid && rolesById.TryGetValue(rid, out var foundRole) ? foundRole : null;
                if (!string.IsNullOrWhiteSpace(query.RoleCode)
                    && !string.Equals(role?.Code, query.RoleCode.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                StudyId? resolvedStudyId = null;
                string? resolvedStudyCode = null;
                SiteId? resolvedSiteId = null;
                string? resolvedSiteCode = null;
                StudySiteId? resolvedStudySiteId = null;

                switch (grant.ScopeLevel)
                {
                    case ScopeLevel.Study or ScopeLevel.StudyCountry when grant.ScopeTargetId is { } targetStudyGuid:
                        var sid = StudyId.FromGuid(targetStudyGuid);
                        resolvedStudyId = sid;
                        studies.TryGetValue(sid, out resolvedStudyCode);
                        break;
                    case ScopeLevel.StudySite when grant.ScopeTargetId is { } targetStudySiteGuid:
                        var ssid = StudySiteId.FromGuid(targetStudySiteGuid);
                        resolvedStudySiteId = ssid;
                        if (studySitesById.TryGetValue(ssid, out var ssInfo))
                        {
                            resolvedStudyId = ssInfo.StudyId;
                            resolvedStudyCode = ssInfo.StudyCode;
                            resolvedSiteId = ssInfo.SiteId;
                            resolvedSiteCode = ssInfo.SiteCode;
                        }

                        break;
                }

                if (query.StudySiteId is { } filterStudySiteId && resolvedStudySiteId != filterStudySiteId)
                {
                    var coversViaStudyOrTenant = grant.ScopeLevel == ScopeLevel.Tenant
                        || (grant.ScopeLevel is ScopeLevel.Study or ScopeLevel.StudyCountry
                            && studySitesById.TryGetValue(filterStudySiteId, out var targetSs)
                            && resolvedStudyId == targetSs.StudyId
                            && (grant.ScopeLevel == ScopeLevel.Study
                                || string.Equals(grant.ScopeCountry, targetSs.Country, StringComparison.OrdinalIgnoreCase)));
                    if (!coversViaStudyOrTenant)
                    {
                        continue;
                    }
                }
                else
                {
                    if (query.SiteId is { } filterSiteId && resolvedSiteId != filterSiteId)
                    {
                        continue;
                    }

                    if (query.StudyId is { } filterStudyId && resolvedStudyId != filterStudyId && grant.ScopeLevel != ScopeLevel.Tenant)
                    {
                        continue;
                    }
                }

                var asOfPeriod = grant.Periods
                    .FirstOrDefault(p => p.Period.Recorded.Contains(knownAt));
                var inEffect = grant.Periods.Any(p => p.Period.IsEffectiveAsOf(validAt, knownAt));
                if (!query.IncludeInactive && !inEffect)
                {
                    continue;
                }

                var validityAsKnown = asOfPeriod?.Period.Valid;
                var revokedAsKnown = asOfPeriod is null && grant.Periods.Any(p => p.Period.Recorded.Start <= knownAt);
                IReadOnlyList<string> capabilities = grant.Capability is { } singleCap
                    ? [singleCap]
                    : role?.Capabilities ?? [];

                var orderedPeriods = grant.Periods
                    .OrderBy(p => p.Period.Recorded.Start)
                    .Select(p => MapPeriod(p.Period))
                    .ToList();

                result.Add(new KernelGrantReadModel(
                    grant.Id,
                    grant.VersionIndex,
                    grant.ParentGrantId,
                    grant.GranteeKind,
                    grant.GranteeId,
                    grant.Capability,
                    grant.RoleId,
                    role?.Code,
                    role?.Name,
                    capabilities,
                    grant.ScopeLevel,
                    grant.ScopeTargetId,
                    grant.ScopeCountry,
                    resolvedStudyId,
                    resolvedStudyCode,
                    resolvedSiteId,
                    resolvedSiteCode,
                    resolvedStudySiteId,
                    grant.WithGrantOption,
                    grant.IssuedBy,
                    inEffect,
                    validityAsKnown?.Start,
                    validityAsKnown?.End,
                    revokedAsKnown,
                    orderedPeriods));
            }

            return [.. result.OrderBy(g => g.GranteeId, StringComparer.Ordinal).ThenBy(g => g.GrantId.Value)];
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<KernelQualificationReadModel>> ListQualificationsAsync(
        TenantId tenant,
        KernelQualificationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validAt = KernelTimestamps.ToStoredPrecision(query.ValidAt);
        var knownAt = KernelTimestamps.ToStoredPrecision(query.KnownAt);

        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var qualQuery = db.Qualifications.AsNoTracking()
                .Include(q => q.Periods)
                .Where(q => q.TenantId == tenant);

            if (!string.IsNullOrWhiteSpace(query.HolderId))
            {
                var holder = query.HolderId.Trim();
                qualQuery = qualQuery.Where(q => q.HolderId == holder);
            }

            if (!string.IsNullOrWhiteSpace(query.Code))
            {
                var code = query.Code.Trim();
                qualQuery = qualQuery.Where(q => q.Code == code);
            }

            if (query.ProtocolVersionId is { } filterVersionId)
            {
                qualQuery = qualQuery.Where(q => q.ProtocolVersionId == filterVersionId);
            }

            var qualifications = await qualQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
            if (qualifications.Count == 0)
            {
                return [];
            }

            var versionIds = qualifications.Where(q => q.ProtocolVersionId is not null).Select(q => q.ProtocolVersionId!.Value).Distinct().ToList();
            var versionsById = versionIds.Count == 0
                ? new Dictionary<ProtocolVersionId, (StudyId StudyId, int Number)>()
                : (await db.ProtocolVersions.AsNoTracking()
                        .Where(v => v.TenantId == tenant && versionIds.Contains(v.Id))
                        .Select(v => new { v.Id, v.StudyId, v.Number })
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false))
                    .ToDictionary(v => v.Id, v => (v.StudyId, v.Number));

            var artifactVersionIds = qualifications.Select(q => q.EvidenceArtifactVersionId).Distinct().ToList();
            var artifactVersions = artifactVersionIds.Count == 0
                ? new Dictionary<ArtifactVersionId, (ArtifactId ArtifactId, StudyId? StudyId)>()
                : (await (
                            from av in db.ArtifactVersions.AsNoTracking()
                            join a in db.Artifacts.AsNoTracking() on new { av.TenantId, Id = av.ArtifactId } equals new { a.TenantId, a.Id }
                            where av.TenantId == tenant && artifactVersionIds.Contains(av.Id)
                            select new { av.Id, av.ArtifactId, a.StudyId })
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false))
                    .ToDictionary(x => x.Id, x => (x.ArtifactId, x.StudyId));

            var studies = await db.Studies.AsNoTracking()
                .Where(s => s.TenantId == tenant)
                .Select(s => new { s.Id, s.StudyCode })
                .ToDictionaryAsync(s => s.Id, s => s.StudyCode, cancellationToken)
                .ConfigureAwait(false);

            var result = new List<KernelQualificationReadModel>(qualifications.Count);
            foreach (var qual in qualifications)
            {
                StudyId? studyId = null;
                int? versionNumber = null;
                if (qual.ProtocolVersionId is { } pvId && versionsById.TryGetValue(pvId, out var pvInfo))
                {
                    studyId = pvInfo.StudyId;
                    versionNumber = pvInfo.Number;
                }

                ArtifactId? artifactId = null;
                if (artifactVersions.TryGetValue(qual.EvidenceArtifactVersionId, out var artInfo))
                {
                    artifactId = artInfo.ArtifactId;
                    studyId ??= artInfo.StudyId;
                }

                if (query.StudyId is { } filterStudyId && studyId is not null && studyId != filterStudyId)
                {
                    continue;
                }

                string? studyCode = studyId is { } sid && studies.TryGetValue(sid, out var foundCode) ? foundCode : null;
                var asOfPeriod = qual.Periods.FirstOrDefault(p => p.Period.Recorded.Contains(knownAt));
                var inEffect = qual.Periods.Any(p => p.Period.IsEffectiveAsOf(validAt, knownAt));
                if (!query.IncludeInactive && !inEffect)
                {
                    continue;
                }

                var validityAsKnown = asOfPeriod?.Period.Valid;
                var revokedAsKnown = asOfPeriod is null && qual.Periods.Any(p => p.Period.Recorded.Start <= knownAt);
                var orderedPeriods = qual.Periods
                    .OrderBy(p => p.Period.Recorded.Start)
                    .Select(p => MapPeriod(p.Period))
                    .ToList();

                result.Add(new KernelQualificationReadModel(
                    qual.Id,
                    qual.VersionIndex,
                    qual.HolderKind,
                    qual.HolderId,
                    qual.Kind,
                    qual.Code,
                    qual.ProtocolVersionId,
                    versionNumber,
                    studyId,
                    studyCode,
                    artifactId,
                    qual.EvidenceArtifactVersionId,
                    qual.RecordedBy,
                    inEffect,
                    validityAsKnown?.Start,
                    validityAsKnown?.End,
                    revokedAsKnown,
                    orderedPeriods));
            }

            return [.. result.OrderBy(q => q.HolderId, StringComparer.Ordinal).ThenBy(q => q.QualificationId.Value)];
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<KernelQualificationRequirementReadModel>> ListQualificationRequirementsAsync(
        TenantId tenant,
        KernelQualificationRequirementQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validAt = KernelTimestamps.ToStoredPrecision(query.ValidAt);
        var knownAt = KernelTimestamps.ToStoredPrecision(query.KnownAt);

        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var reqQuery = db.QualificationRequirements.AsNoTracking()
                .Include(r => r.Periods)
                .Where(r => r.TenantId == tenant);

            if (!string.IsNullOrWhiteSpace(query.Capability))
            {
                var cap = query.Capability.Trim();
                reqQuery = reqQuery.Where(r => r.Capability == cap);
            }

            if (!string.IsNullOrWhiteSpace(query.QualificationCode))
            {
                var code = query.QualificationCode.Trim();
                reqQuery = reqQuery.Where(r => r.QualificationCode == code);
            }

            var requirements = await reqQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
            if (requirements.Count == 0)
            {
                return [];
            }

            var versionIds = requirements.Where(r => r.ProtocolVersionId is not null).Select(r => r.ProtocolVersionId!.Value).Distinct().ToList();
            var versionsById = versionIds.Count == 0
                ? new Dictionary<ProtocolVersionId, (StudyId StudyId, int Number)>()
                : (await db.ProtocolVersions.AsNoTracking()
                        .Where(v => v.TenantId == tenant && versionIds.Contains(v.Id))
                        .Select(v => new { v.Id, v.StudyId, v.Number })
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false))
                    .ToDictionary(v => v.Id, v => (v.StudyId, v.Number));

            var studySites = await db.StudySites.AsNoTracking()
                .Where(ss => ss.TenantId == tenant)
                .Select(ss => new { ss.Id, ss.StudyId })
                .ToDictionaryAsync(ss => ss.Id, ss => ss.StudyId, cancellationToken)
                .ConfigureAwait(false);

            var studies = await db.Studies.AsNoTracking()
                .Where(s => s.TenantId == tenant)
                .Select(s => new { s.Id, s.StudyCode })
                .ToDictionaryAsync(s => s.Id, s => s.StudyCode, cancellationToken)
                .ConfigureAwait(false);

            var result = new List<KernelQualificationRequirementReadModel>(requirements.Count);
            foreach (var req in requirements)
            {
                StudyId? studyId = null;
                int? versionNumber = null;
                if (req.ProtocolVersionId is { } pvId && versionsById.TryGetValue(pvId, out var pvInfo))
                {
                    studyId = pvInfo.StudyId;
                    versionNumber = pvInfo.Number;
                }

                if (studyId is null)
                {
                    switch (req.ScopeLevel)
                    {
                        case ScopeLevel.Study or ScopeLevel.StudyCountry when req.ScopeTargetId is { } targetStudyGuid:
                            studyId = StudyId.FromGuid(targetStudyGuid);
                            break;
                        case ScopeLevel.StudySite when req.ScopeTargetId is { } targetStudySiteGuid:
                            studySites.TryGetValue(StudySiteId.FromGuid(targetStudySiteGuid), out var linkedStudy);
                            if (!linkedStudy.IsEmpty)
                            {
                                studyId = linkedStudy;
                            }

                            break;
                    }
                }

                if (query.StudyId is { } filterStudyId && studyId is not null && studyId != filterStudyId)
                {
                    continue;
                }

                string? studyCode = studyId is { } sid && studies.TryGetValue(sid, out var foundCode) ? foundCode : null;
                var asOfPeriod = req.Periods.FirstOrDefault(p => p.Period.Recorded.Contains(knownAt));
                var inEffect = req.Periods.Any(p => p.Period.IsEffectiveAsOf(validAt, knownAt));
                if (!query.IncludeInactive && !inEffect)
                {
                    continue;
                }

                var validityAsKnown = asOfPeriod?.Period.Valid;
                var retiredAsKnown = asOfPeriod is null && req.Periods.Any(p => p.Period.Recorded.Start <= knownAt);
                var orderedPeriods = req.Periods
                    .OrderBy(p => p.Period.Recorded.Start)
                    .Select(p => MapPeriod(p.Period))
                    .ToList();

                result.Add(new KernelQualificationRequirementReadModel(
                    req.Id,
                    req.VersionIndex,
                    req.Capability,
                    req.QualificationCode,
                    req.ProtocolVersionId,
                    versionNumber,
                    req.ScopeLevel,
                    req.ScopeTargetId,
                    req.ScopeCountry,
                    studyId,
                    studyCode,
                    req.DefinedBy,
                    inEffect,
                    validityAsKnown?.Start,
                    validityAsKnown?.End,
                    retiredAsKnown,
                    orderedPeriods));
            }

            return [.. result.OrderBy(r => r.Capability, StringComparer.Ordinal).ThenBy(r => r.RequirementId.Value)];
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static KernelBitemporalPeriodReadModel MapPeriod(TemporalPeriod period) =>
        new(period.Valid.Start, period.Valid.End, period.Recorded.Start, period.Recorded.End);
}
