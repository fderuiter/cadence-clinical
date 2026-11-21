using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Authorization;

/// <summary>
/// Implements <see cref="ITeamDirectoryReader"/> over the tenant-scoped kernel tables and
/// pseudonymous <c>identity_vault</c> record existence checks (ADR-2610, ADR-2611).
/// </summary>
internal sealed class KernelTeamDirectoryReader(KernelTenantDbContextFactory contexts) : ITeamDirectoryReader
{
    public async Task<TeamDirectorySnapshot> ReadSnapshotAsync(TenantId tenant, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tenantRow = await db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenant)
                .Select(t => new { t.Code, t.Name })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            var rawMemberships = await db.TenantMemberships.AsNoTracking()
                .Where(m => m.TenantId == tenant)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new { m.PersonId, m.CreatedAt, m.IsActive })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var roles = await db.Roles.AsNoTracking()
                .Where(r => r.TenantId == tenant)
                .OrderBy(r => r.Name)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var grants = await db.Grants.AsNoTracking()
                .Where(g => g.TenantId == tenant)
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var organizations = await db.Organizations.AsNoTracking()
                .Where(o => o.TenantId == tenant)
                .OrderBy(o => o.Name)
                .Select(o => new { o.Id, o.Code, o.Name })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var studies = await db.Studies.AsNoTracking()
                .Where(s => s.TenantId == tenant)
                .OrderBy(s => s.StudyCode)
                .Select(s => new { s.Id, s.SponsorId, s.StudyCode, s.Title })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var studySites = await (
                    from ss in db.StudySites.AsNoTracking()
                    join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                    join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                    where ss.TenantId == tenant
                    orderby study.StudyCode, site.SiteCode
                    select new
                    {
                        ss.Id,
                        study.SponsorId,
                        ss.StudyId,
                        study.StudyCode,
                        site.SiteCode,
                        site.Name,
                        site.Country,
                    })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var memberships = new List<TeamMemberRecord>(rawMemberships.Count);
            foreach (var m in rawMemberships)
            {
                bool hasSealedIdentity = await db.Database
                    .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM identity_vault.get_record('person', {m.PersonId.Value}) WHERE erased_at IS NULL) AS \"Value\"")
                    .SingleAsync(cancellationToken)
                    .ConfigureAwait(false);
                memberships.Add(new TeamMemberRecord(
                    m.PersonId,
                    new DateTimeOffset(DateTime.SpecifyKind(m.CreatedAt, DateTimeKind.Utc)),
                    m.IsActive,
                    hasSealedIdentity));
            }

            var grantsById = grants.ToDictionary(g => g.Id);
            var inEffectIds = grants
                .Where(g => IsChainInEffect(g, grantsById, at))
                .Select(g => g.Id)
                .ToHashSet();

            var scopeOptions = new List<TeamScopeOption>
            {
                new(AuthorityScope.Tenant(), ScopePath.TenantWide, tenantRow?.Code ?? "TENANT", $"Tenant ({tenantRow?.Name ?? "Workspace"})"),
            };

            foreach (var org in organizations)
            {
                scopeOptions.Add(new TeamScopeOption(
                    AuthorityScope.Organization(org.Id),
                    new ScopePath(org.Id),
                    org.Code,
                    $"{org.Code} — {org.Name}"));
            }

            foreach (var study in studies)
            {
                scopeOptions.Add(new TeamScopeOption(
                    AuthorityScope.Study(study.Id),
                    new ScopePath(study.SponsorId, study.Id),
                    study.StudyCode,
                    $"{study.StudyCode} — {study.Title}"));
            }

            foreach (var countryScope in studySites
                         .Where(ss => !string.IsNullOrWhiteSpace(ss.Country))
                         .Select(ss => (ss.StudyId, ss.SponsorId, ss.StudyCode, ss.Country))
                         .Distinct()
                         .OrderBy(c => c.StudyCode)
                         .ThenBy(c => c.Country))
            {
                scopeOptions.Add(new TeamScopeOption(
                    AuthorityScope.StudyCountry(countryScope.StudyId, countryScope.Country),
                    new ScopePath(countryScope.SponsorId, countryScope.StudyId, countryScope.Country),
                    $"{countryScope.StudyCode}-{countryScope.Country}",
                    $"{countryScope.StudyCode} ({countryScope.Country})",
                    ParentLabel: countryScope.StudyCode));
            }

            foreach (var ss in studySites)
            {
                scopeOptions.Add(new TeamScopeOption(
                    AuthorityScope.StudySite(ss.Id),
                    new ScopePath(ss.SponsorId, ss.StudyId, ss.Country, ss.Id),
                    ss.SiteCode,
                    $"{ss.SiteCode} — {ss.Name} ({ss.StudyCode})",
                    ParentLabel: ss.StudyCode));
            }

            var roleRecords = roles
                .Select(r => new TeamRoleRecord(r.Id, r.Code, r.Name, r.Capabilities))
                .ToList();

            var grantRecords = grants
                .Select(g =>
                {
                    var state = g.State();
                    var createdUtc = new DateTimeOffset(DateTime.SpecifyKind(g.CreatedAt, DateTimeKind.Utc));
                    var closedAt = g.Periods
                        .Where(p => !p.Period.Recorded.IsOpen)
                        .Select(p => p.Period.Recorded.End)
                        .OrderByDescending(e => e)
                        .FirstOrDefault();
                    var isRevoked = state.Revoked || closedAt is not null;
                    return new TeamGrantRecord(
                        g.Id,
                        g.Grantee,
                        g.Capability,
                        g.RoleId,
                        g.Scope,
                        g.ParentGrantId,
                        g.WithGrantOption,
                        g.IssuedBy,
                        createdUtc,
                        g.ReasonForChange,
                        state.ValidFrom,
                        state.ValidTo,
                        isRevoked,
                        inEffectIds.Contains(g.Id));
                })
                .ToList();

            return new TeamDirectorySnapshot(
                tenant,
                tenantRow?.Code ?? string.Empty,
                tenantRow?.Name ?? string.Empty,
                memberships,
                roleRecords,
                grantRecords,
                scopeOptions);
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static bool IsChainInEffect(Grant grant, IReadOnlyDictionary<GrantId, Grant> byId, DateTimeOffset at)
    {
        var current = grant;
        for (var depth = 0; depth <= AuthorityEvaluator.MaxDelegationDepth; depth++)
        {
            if (!current.IsInEffect(at, at))
            {
                return false;
            }

            if (current.ParentGrantId is not { } parentId)
            {
                return true;
            }

            if (!byId.TryGetValue(parentId, out var parent))
            {
                return false;
            }

            current = parent;
        }

        return false;
    }
}
