using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cadence.Kernel.Persistence.Authorization;

/// <summary>
/// Reads grants, roles and scope paths for the authority evaluator through a context bound to the request's tenant
/// (ADR-2250). Read-only: no tracking. Bulk reads hold one connection lease across per-kind set queries so
/// <see cref="KernelTenantConnectionInterceptor"/> sets and clears the tenant once per bulk call (K-ID-2).
/// </summary>
internal sealed class KernelAuthorityStore(KernelTenantDbContextFactory contexts) : IAuthorityStore
{
    private static readonly FrozenSet<string> PathBearingKinds = FrozenSet.Create(
        StringComparer.Ordinal,
        NodeKinds.Organization,
        NodeKinds.Site,
        NodeKinds.Study,
        NodeKinds.StudySite,
        NodeKinds.Enrollment,
        NodeKinds.ProtocolVersion,
        NodeKinds.ProtocolElement,
        NodeKinds.Artifact,
        NodeKinds.ArtifactVersion,
        NodeKinds.Grant,
        NodeKinds.QualificationRequirement,
        NodeKinds.Effectivity,
        NodeKinds.Proposal);

    private const string ProtocolElementPathsSql = """
        SELECT DISTINCT ON (le.to_id)
            le.to_id AS "Id",
            s.sponsor_id AS "OrganizationId",
            pv.study_id AS "StudyId",
            NULL::text AS "Country",
            NULL::uuid AS "StudySiteId"
        FROM kernel.lineage_edges AS le
        JOIN kernel.protocol_versions AS pv
            ON pv.tenant_id = le.tenant_id AND pv.id = le.from_id
        JOIN kernel.studies AS s
            ON s.tenant_id = pv.tenant_id AND s.id = pv.study_id
        WHERE le.tenant_id = @tenant
          AND le.to_kind = 'protocol_element'
          AND le.to_id = ANY(@ids)
          AND le.from_kind = 'protocol_version'
          AND le.kind = 'GovernedBy'
        ORDER BY le.to_id, le.recorded_at DESC, le.id DESC
        """;

    private const string GrantPathsSql = """
        SELECT
            g.id AS "Id",
            CASE
                WHEN g.scope_level = 'Organization' THEN o.id
                WHEN g.scope_level IN ('Study', 'StudyCountry') THEN s.sponsor_id
                WHEN g.scope_level = 'StudySite' AND ss_site.id IS NOT NULL THEN ss_study.sponsor_id
            END AS "OrganizationId",
            CASE
                WHEN g.scope_level IN ('Study', 'StudyCountry') AND s.id IS NOT NULL THEN s.id
                WHEN g.scope_level = 'StudySite' AND ss_study.id IS NOT NULL AND ss_site.id IS NOT NULL THEN ss.study_id
            END AS "StudyId",
            CASE
                WHEN g.scope_level = 'StudyCountry' AND s.id IS NOT NULL THEN g.scope_country
                WHEN g.scope_level = 'StudySite' AND ss_study.id IS NOT NULL THEN ss_site.country
            END AS "Country",
            CASE
                WHEN g.scope_level = 'StudySite' AND ss_study.id IS NOT NULL AND ss_site.id IS NOT NULL THEN ss.id
            END AS "StudySiteId"
        FROM kernel.grants AS g
        LEFT JOIN kernel.organizations AS o
            ON g.scope_level = 'Organization' AND o.tenant_id = g.tenant_id AND o.id = g.scope_target_id
        LEFT JOIN kernel.studies AS s
            ON g.scope_level IN ('Study', 'StudyCountry') AND s.tenant_id = g.tenant_id AND s.id = g.scope_target_id
        LEFT JOIN kernel.study_sites AS ss
            ON g.scope_level = 'StudySite' AND ss.tenant_id = g.tenant_id AND ss.id = g.scope_target_id
        LEFT JOIN kernel.studies AS ss_study
            ON ss_study.tenant_id = ss.tenant_id AND ss_study.id = ss.study_id
        LEFT JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        WHERE g.tenant_id = @tenant AND g.id = ANY(@ids)
        """;

    private const string QualificationRequirementPathsSql = """
        SELECT
            r.id AS "Id",
            CASE
                WHEN r.scope_level = 'Organization' THEN o.id
                WHEN r.scope_level IN ('Study', 'StudyCountry') THEN s.sponsor_id
                WHEN r.scope_level = 'StudySite' AND ss_site.id IS NOT NULL THEN ss_study.sponsor_id
            END AS "OrganizationId",
            CASE
                WHEN r.scope_level IN ('Study', 'StudyCountry') AND s.id IS NOT NULL THEN s.id
                WHEN r.scope_level = 'StudySite' AND ss_study.id IS NOT NULL AND ss_site.id IS NOT NULL THEN ss.study_id
            END AS "StudyId",
            CASE
                WHEN r.scope_level = 'StudyCountry' AND s.id IS NOT NULL THEN r.scope_country
                WHEN r.scope_level = 'StudySite' AND ss_study.id IS NOT NULL THEN ss_site.country
            END AS "Country",
            CASE
                WHEN r.scope_level = 'StudySite' AND ss_study.id IS NOT NULL AND ss_site.id IS NOT NULL THEN ss.id
            END AS "StudySiteId"
        FROM kernel.qualification_requirements AS r
        LEFT JOIN kernel.organizations AS o
            ON r.scope_level = 'Organization' AND o.tenant_id = r.tenant_id AND o.id = r.scope_target_id
        LEFT JOIN kernel.studies AS s
            ON r.scope_level IN ('Study', 'StudyCountry') AND s.tenant_id = r.tenant_id AND s.id = r.scope_target_id
        LEFT JOIN kernel.study_sites AS ss
            ON r.scope_level = 'StudySite' AND ss.tenant_id = r.tenant_id AND ss.id = r.scope_target_id
        LEFT JOIN kernel.studies AS ss_study
            ON ss_study.tenant_id = ss.tenant_id AND ss_study.id = ss.study_id
        LEFT JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        WHERE r.tenant_id = @tenant AND r.id = ANY(@ids)
        """;

    private const string EffectivityPathsSql = """
        SELECT
            e.id AS "Id",
            CASE
                WHEN e.scope_study_site_id IS NOT NULL THEN
                    CASE WHEN ss_site.id IS NOT NULL THEN ss_study.sponsor_id END
                ELSE s.sponsor_id
            END AS "OrganizationId",
            CASE
                WHEN e.scope_study_site_id IS NOT NULL THEN
                    CASE WHEN ss_study.id IS NOT NULL AND ss_site.id IS NOT NULL THEN ss.study_id END
                WHEN s.id IS NOT NULL THEN e.study_id
            END AS "StudyId",
            CASE
                WHEN e.scope_study_site_id IS NOT NULL THEN
                    CASE WHEN ss_study.id IS NOT NULL THEN ss_site.country END
                WHEN s.id IS NOT NULL THEN e.scope_country
            END AS "Country",
            CASE
                WHEN e.scope_study_site_id IS NOT NULL AND ss_study.id IS NOT NULL AND ss_site.id IS NOT NULL THEN ss.id
            END AS "StudySiteId"
        FROM kernel.effectivities AS e
        LEFT JOIN kernel.studies AS s
            ON e.scope_study_site_id IS NULL AND s.tenant_id = e.tenant_id AND s.id = e.study_id
        LEFT JOIN kernel.study_sites AS ss
            ON e.scope_study_site_id IS NOT NULL AND ss.tenant_id = e.tenant_id AND ss.id = e.scope_study_site_id
        LEFT JOIN kernel.studies AS ss_study
            ON ss_study.tenant_id = ss.tenant_id AND ss_study.id = ss.study_id
        LEFT JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        WHERE e.tenant_id = @tenant AND e.id = ANY(@ids)
        """;

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

    public async Task<IReadOnlyDictionary<GrantId, Grant>> FindGrantsAsync(
        TenantId tenant, IReadOnlyCollection<GrantId> grants, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);
        var ids = grants.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<GrantId, Grant>();
        }

        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rows = await db.Grants.AsNoTracking()
                .Where(g => g.TenantId == tenant && ids.Contains(g.Id))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return rows.ToDictionary(g => g.Id);
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var ids = roles.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        await using var db = contexts.Create(tenant);
        return await db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenant && ids.Contains(r.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = contexts.Create(tenant);
        return await db.Qualifications.AsNoTracking()
            .Where(q => q.TenantId == tenant && q.HolderKind == actor.Kind && q.HolderId == actor.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.QualificationRequirements.AsNoTracking()
            .Where(r => r.TenantId == tenant && r.Capability == capability)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var paths = await PathsOfAsync(tenant, [resource], cancellationToken).ConfigureAwait(false);
        return paths.GetValueOrDefault(resource, ScopePath.TenantWide);
    }

    public async Task<IReadOnlyDictionary<NodeRef, ScopePath>> PathsOfAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> resources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var distinct = resources.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<NodeRef, ScopePath>();
        }

        var byKind = distinct
            .Select(r => r.Unversioned())
            .Distinct()
            .Where(r => PathBearingKinds.Contains(r.Kind))
            .GroupBy(r => r.Kind, StringComparer.Ordinal)
            .ToList();

        var resolved = new Dictionary<(string Kind, Guid Id), ScopePath>();
        if (byKind.Count > 0)
        {
            await using var db = contexts.Create(tenant);
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var group in byKind)
                {
                    await LoadPathsForKindAsync(db, tenant, group.Key, [.. group.Select(r => r.Id)], resolved, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }

        var result = new Dictionary<NodeRef, ScopePath>(distinct.Count);
        foreach (var resource in distinct)
        {
            result[resource] = resolved.GetValueOrDefault((resource.Kind, resource.Id), ScopePath.TenantWide);
        }

        return result;
    }

    public async Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        var paths = await StudySitePathsAsync(tenant, [node], cancellationToken).ConfigureAwait(false);
        return paths.GetValueOrDefault(node, []);
    }

    public async Task<IReadOnlyDictionary<NodeRef, IReadOnlyList<ScopePath>>> StudySitePathsAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var distinct = nodes.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<NodeRef, IReadOnlyList<ScopePath>>();
        }

        var participantIds = distinct
            .Where(n => n.Kind == NodeKinds.Participant)
            .Select(n => ParticipantId.FromGuid(n.Id))
            .Distinct()
            .ToList();
        var siteIds = distinct
            .Where(n => n.Kind == NodeKinds.Site)
            .Select(n => SiteId.FromGuid(n.Id))
            .Distinct()
            .ToList();

        var reachedByObject = new Dictionary<(string Kind, Guid Id), List<ScopePath>>();
        if (participantIds.Count > 0 || siteIds.Count > 0)
        {
            await using var db = contexts.Create(tenant);
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (participantIds.Count > 0)
                {
                    var participantRows = await (
                            from e in db.Enrollments.AsNoTracking()
                            join ss in db.StudySites.AsNoTracking() on new { e.TenantId, Id = e.StudySiteId } equals new { ss.TenantId, ss.Id }
                            join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                            join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                            where e.TenantId == tenant && participantIds.Contains(e.ParticipantId)
                            select new { e.ParticipantId, study.SponsorId, ss.StudyId, site.Country, StudySiteId = ss.Id })
                        .Distinct()
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var row in participantRows)
                    {
                        AddReached(reachedByObject, NodeKinds.Participant, row.ParticipantId.Value, new ScopePath(row.SponsorId, row.StudyId, row.Country, row.StudySiteId));
                    }
                }

                if (siteIds.Count > 0)
                {
                    var siteRows = await (
                            from ss in db.StudySites.AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                            join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                            where ss.TenantId == tenant && siteIds.Contains(ss.SiteId)
                            select new { ss.SiteId, study.SponsorId, ss.StudyId, site.Country, StudySiteId = ss.Id })
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var row in siteRows)
                    {
                        AddReached(reachedByObject, NodeKinds.Site, row.SiteId.Value, new ScopePath(row.SponsorId, row.StudyId, row.Country, row.StudySiteId));
                    }
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }

        var result = new Dictionary<NodeRef, IReadOnlyList<ScopePath>>(distinct.Count);
        foreach (var node in distinct)
        {
            result[node] = reachedByObject.TryGetValue((node.Kind, node.Id), out var list) ? list : [];
        }

        return result;
    }

    private static void AddReached(
        Dictionary<(string Kind, Guid Id), List<ScopePath>> reachedByObject, string kind, Guid id, ScopePath path)
    {
        if (!reachedByObject.TryGetValue((kind, id), out var list))
        {
            reachedByObject[(kind, id)] = list = [];
        }

        list.Add(path);
    }

    private static async Task LoadPathsForKindAsync(
        KernelDbContext db,
        TenantId tenant,
        string kind,
        Guid[] rawIds,
        Dictionary<(string Kind, Guid Id), ScopePath> resolved,
        CancellationToken ct)
    {
        switch (kind)
        {
            case NodeKinds.Organization:
                {
                    var ids = rawIds.Select(OrganizationId.FromGuid).ToList();
                    var rows = await db.Organizations.AsNoTracking()
                        .Where(o => o.TenantId == tenant && ids.Contains(o.Id))
                        .Select(o => o.Id)
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var id in rows)
                    {
                        resolved[(kind, id.Value)] = new ScopePath(id);
                    }

                    break;
                }

            case NodeKinds.Site:
                {
                    var ids = rawIds.Select(SiteId.FromGuid).ToList();
                    var rows = await db.Sites.AsNoTracking()
                        .Where(s => s.TenantId == tenant && ids.Contains(s.Id))
                        .Select(s => new { s.Id, s.InstitutionId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.InstitutionId);
                    }

                    break;
                }

            case NodeKinds.Study:
                {
                    var ids = rawIds.Select(StudyId.FromGuid).ToList();
                    var rows = await db.Studies.AsNoTracking()
                        .Where(s => s.TenantId == tenant && ids.Contains(s.Id))
                        .Select(s => new { s.Id, s.SponsorId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.Id);
                    }

                    break;
                }

            case NodeKinds.StudySite:
                {
                    var ids = rawIds.Select(StudySiteId.FromGuid).ToList();
                    var rows = await (
                            from ss in db.StudySites.AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                            join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                            where ss.TenantId == tenant && ids.Contains(ss.Id)
                            select new { ss.Id, study.SponsorId, ss.StudyId, site.Country })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId, row.Country, row.Id);
                    }

                    break;
                }

            case NodeKinds.Enrollment:
                {
                    var ids = rawIds.Select(EnrollmentId.FromGuid).ToList();
                    var rows = await (
                            from e in db.Enrollments.AsNoTracking()
                            join ss in db.StudySites.AsNoTracking() on new { e.TenantId, Id = e.StudySiteId } equals new { ss.TenantId, ss.Id }
                            join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                            join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                            where e.TenantId == tenant && ids.Contains(e.Id)
                            select new { e.Id, study.SponsorId, ss.StudyId, site.Country, StudySiteId = ss.Id })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId, row.Country, row.StudySiteId);
                    }

                    break;
                }

            case NodeKinds.ProtocolVersion:
                {
                    var ids = rawIds.Select(ProtocolVersionId.FromGuid).ToList();
                    var rows = await (
                            from v in db.ProtocolVersions.AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { v.TenantId, Id = v.StudyId } equals new { study.TenantId, study.Id }
                            where v.TenantId == tenant && ids.Contains(v.Id)
                            select new { v.Id, study.SponsorId, v.StudyId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.ProtocolElement:
                // An element belongs to the study of the protocol versions that govern it (ADR-2474).
                await LoadRawPathsAsync(db, tenant, kind, rawIds, ProtocolElementPathsSql, resolved, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Artifact:
                {
                    var ids = rawIds.Select(ArtifactId.FromGuid).ToList();
                    var rows = await (
                            from a in db.Artifacts.AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { a.TenantId, Id = a.StudyId } equals new { study.TenantId, Id = (StudyId?)study.Id }
                            where a.TenantId == tenant && ids.Contains(a.Id)
                            select new { a.Id, study.SponsorId, StudyId = study.Id })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.ArtifactVersion:
                {
                    var ids = rawIds.Select(ArtifactVersionId.FromGuid).ToList();
                    var rows = await (
                            from v in db.ArtifactVersions.AsNoTracking()
                            join a in db.Artifacts.AsNoTracking() on new { v.TenantId, Id = v.ArtifactId } equals new { a.TenantId, a.Id }
                            join study in db.Studies.AsNoTracking() on new { a.TenantId, Id = a.StudyId } equals new { study.TenantId, Id = (StudyId?)study.Id }
                            where v.TenantId == tenant && ids.Contains(v.Id)
                            select new { v.Id, study.SponsorId, StudyId = study.Id })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.Grant:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, GrantPathsSql, resolved, ct).ConfigureAwait(false);
                break;

            case NodeKinds.QualificationRequirement:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, QualificationRequirementPathsSql, resolved, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Effectivity:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, EffectivityPathsSql, resolved, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Proposal:
                {
                    // A proposal is a study object (ADR-2489 §4.2): readers and deciders are authorized on its study (K-AI-3).
                    var ids = rawIds.Select(ProposalId.FromGuid).ToList();
                    var rows = await (
                            from p in db.Proposals.AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { p.TenantId, Id = p.StudyId } equals new { study.TenantId, study.Id }
                            where p.TenantId == tenant && ids.Contains(p.Id)
                            select new { p.Id, study.SponsorId, p.StudyId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            default:
                // Tenants, participants (who may be in several studies), roles, qualifications (held by a person across
                // studies), attestations and reference data belong to no organization or study.
                break;
        }
    }

    private static async Task LoadRawPathsAsync(
        KernelDbContext db,
        TenantId tenant,
        string kind,
        Guid[] rawIds,
        string sql,
        Dictionary<(string Kind, Guid Id), ScopePath> resolved,
        CancellationToken ct)
    {
        object[] parameters =
        [
            new NpgsqlParameter<Guid>("tenant", tenant.Value),
            new NpgsqlParameter<Guid[]>("ids", rawIds),
        ];
        var rows = await db.Database.SqlQueryRaw<ScopePathRow>(sql, parameters).ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in rows)
        {
            if (row.ToScopePath() is { } path)
            {
                resolved[(kind, row.Id)] = path;
            }
        }
    }

    private sealed record ScopePathRow(
        Guid Id,
        Guid? OrganizationId,
        Guid? StudyId,
        string? Country,
        Guid? StudySiteId)
    {
        public ScopePath? ToScopePath()
        {
            if (OrganizationId is not { } organization)
            {
                return null;
            }

            var study = StudyId is { } studyGuid ? Cadence.Kernel.StudyId.FromGuid(studyGuid) : (Cadence.Kernel.StudyId?)null;
            var studySite = StudySiteId is { } siteGuid ? Cadence.Kernel.StudySiteId.FromGuid(siteGuid) : (Cadence.Kernel.StudySiteId?)null;
            return new ScopePath(Cadence.Kernel.OrganizationId.FromGuid(organization), study, Country, studySite);
        }
    }
}
