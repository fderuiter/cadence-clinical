using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Authorization;

/// <summary>
/// Reads grants, roles and scope paths for the authority evaluator through a context bound to the request's tenant
/// (ADR-2250). Read-only: no tracking.
/// </summary>
internal sealed class KernelAuthorityStore(KernelTenantDbContextFactory contexts) : IAuthorityStore
{
    public async Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = contexts.Create(tenant);
        return await db.Grants.AsNoTracking()
            .Where(g => g.TenantId == tenant && g.GranteeKind == actor.Kind && g.GranteeId == actor.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.Grants.AsNoTracking()
            .SingleOrDefaultAsync(g => g.TenantId == tenant && g.Id == grant, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);
        await using var db = contexts.Create(tenant);
        var ids = roles.ToList();
        return await db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenant && ids.Contains(r.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        await using var db = contexts.Create(tenant);
        return await PathAsync(db, tenant, resource.Kind, resource.Id, cancellationToken).ConfigureAwait(false) ?? ScopePath.TenantWide;
    }

    private static async Task<ScopePath?> PathAsync(KernelDbContext db, TenantId tenant, string kind, Guid id, CancellationToken ct)
    {
        switch (kind)
        {
            case NodeKinds.Organization:
                var organization = OrganizationId.FromGuid(id);
                return await db.Organizations.AsNoTracking().AnyAsync(o => o.TenantId == tenant && o.Id == organization, ct).ConfigureAwait(false)
                    ? new ScopePath(organization)
                    : null;

            case NodeKinds.Site:
                var siteId = SiteId.FromGuid(id);
                var institution = await db.Sites.AsNoTracking()
                    .Where(s => s.TenantId == tenant && s.Id == siteId).Select(s => (OrganizationId?)s.InstitutionId)
                    .SingleOrDefaultAsync(ct).ConfigureAwait(false);
                return institution is null ? null : new ScopePath(institution);

            case NodeKinds.Study:
                return await StudyPathAsync(db, tenant, StudyId.FromGuid(id), country: null, ct).ConfigureAwait(false);

            case NodeKinds.StudySite:
                return await StudySitePathAsync(db, tenant, StudySiteId.FromGuid(id), ct).ConfigureAwait(false);

            case NodeKinds.Enrollment:
                var enrollmentId = EnrollmentId.FromGuid(id);
                var enrollmentSite = await db.Enrollments.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.Id == enrollmentId).Select(e => (StudySiteId?)e.StudySiteId)
                    .SingleOrDefaultAsync(ct).ConfigureAwait(false);
                return enrollmentSite is { } site ? await StudySitePathAsync(db, tenant, site, ct).ConfigureAwait(false) : null;

            case NodeKinds.ProtocolVersion:
                var versionId = ProtocolVersionId.FromGuid(id);
                var versionStudy = await db.ProtocolVersions.AsNoTracking()
                    .Where(v => v.TenantId == tenant && v.Id == versionId).Select(v => (StudyId?)v.StudyId)
                    .SingleOrDefaultAsync(ct).ConfigureAwait(false);
                return versionStudy is { } study ? await StudyPathAsync(db, tenant, study, country: null, ct).ConfigureAwait(false) : null;

            case NodeKinds.Artifact:
                var artifactId = ArtifactId.FromGuid(id);
                var artifactStudy = await db.Artifacts.AsNoTracking()
                    .Where(a => a.TenantId == tenant && a.Id == artifactId).Select(a => a.StudyId)
                    .SingleOrDefaultAsync(ct).ConfigureAwait(false);
                return artifactStudy is { } evidenceStudy ? await StudyPathAsync(db, tenant, evidenceStudy, country: null, ct).ConfigureAwait(false) : null;

            case NodeKinds.ArtifactVersion:
                var artifactVersionId = ArtifactVersionId.FromGuid(id);
                var owner = await db.ArtifactVersions.AsNoTracking()
                    .Where(v => v.TenantId == tenant && v.Id == artifactVersionId).Select(v => (ArtifactId?)v.ArtifactId)
                    .SingleOrDefaultAsync(ct).ConfigureAwait(false);
                return owner is { } artifact ? await PathAsync(db, tenant, NodeKinds.Artifact, artifact.Value, ct).ConfigureAwait(false) : null;

            case NodeKinds.Grant:
                var grantId = GrantId.FromGuid(id);
                var scope = await db.Grants.AsNoTracking()
                    .Where(g => g.TenantId == tenant && g.Id == grantId)
                    .Select(g => new { g.ScopeLevel, g.ScopeTargetId, g.ScopeCountry })
                    .SingleOrDefaultAsync(ct).ConfigureAwait(false);
                return scope is null ? null : await ScopePathAsync(db, tenant, new AuthorityScope(scope.ScopeLevel, scope.ScopeTargetId, scope.ScopeCountry), ct).ConfigureAwait(false);

            case NodeKinds.Effectivity:
                var effectivityId = EffectivityId.FromGuid(id);
                var effectivity = await db.Effectivities.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.Id == effectivityId)
                    .Select(e => new { e.StudyId, e.ScopeCountry, e.ScopeStudySiteId })
                    .SingleOrDefaultAsync(ct).ConfigureAwait(false);
                return effectivity switch
                {
                    null => null,
                    { ScopeStudySiteId: { } scopedSite } => await StudySitePathAsync(db, tenant, scopedSite, ct).ConfigureAwait(false),
                    _ => await StudyPathAsync(db, tenant, effectivity.StudyId, effectivity.ScopeCountry, ct).ConfigureAwait(false),
                };

            default:
                // Tenants, participants (who may be in several studies), roles, attestations and reference data belong
                // to no organization or study.
                return null;
        }
    }

    private static Task<ScopePath?> ScopePathAsync(KernelDbContext db, TenantId tenant, AuthorityScope scope, CancellationToken ct) => scope.Level switch
    {
        ScopeLevel.Organization => PathAsync(db, tenant, NodeKinds.Organization, scope.TargetId!.Value, ct),
        ScopeLevel.Study => StudyPathAsync(db, tenant, StudyId.FromGuid(scope.TargetId!.Value), country: null, ct),
        ScopeLevel.StudyCountry => StudyPathAsync(db, tenant, StudyId.FromGuid(scope.TargetId!.Value), scope.Country, ct),
        ScopeLevel.StudySite => StudySitePathAsync(db, tenant, StudySiteId.FromGuid(scope.TargetId!.Value), ct),
        _ => Task.FromResult<ScopePath?>(null),
    };

    private static async Task<ScopePath?> StudyPathAsync(KernelDbContext db, TenantId tenant, StudyId study, string? country, CancellationToken ct)
    {
        var sponsor = await db.Studies.AsNoTracking()
            .Where(s => s.TenantId == tenant && s.Id == study).Select(s => (OrganizationId?)s.SponsorId)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return sponsor is null ? null : new ScopePath(sponsor, study, country);
    }

    private static async Task<ScopePath?> StudySitePathAsync(KernelDbContext db, TenantId tenant, StudySiteId studySite, CancellationToken ct)
    {
        var row = await (
                from ss in db.StudySites.AsNoTracking()
                join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                where ss.TenantId == tenant && ss.Id == studySite
                select new { study.SponsorId, ss.StudyId, site.Country })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return row is null ? null : new ScopePath(row.SponsorId, row.StudyId, row.Country, studySite);
    }
}
