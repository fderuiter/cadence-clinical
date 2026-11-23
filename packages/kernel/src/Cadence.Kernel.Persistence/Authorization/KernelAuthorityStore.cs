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
using Cadence.Kernel.Persistence.Evidence;
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
        NodeKinds.FormQuery,
        NodeKinds.Artifact,
        NodeKinds.ArtifactVersion,
        NodeKinds.Grant,
        NodeKinds.QualificationRequirement,
        NodeKinds.Effectivity,
        NodeKinds.Proposal,
        NodeKinds.Observation,
        NodeKinds.CollectionInstance,
        NodeKinds.Assertion,
        NodeKinds.ValidationFinding);

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

    private const string FormQueryPathsSql = """
        SELECT DISTINCT ON (le.to_id)
            le.to_id AS "Id",
            study.sponsor_id AS "OrganizationId",
            ss.study_id AS "StudyId",
            site.country AS "Country",
            ss.id AS "StudySiteId"
        FROM kernel.lineage_edges AS le
        JOIN kernel.enrollments AS e
            ON e.tenant_id = le.tenant_id AND e.id = le.from_id
        JOIN kernel.study_sites AS ss
            ON ss.tenant_id = e.tenant_id AND ss.id = e.study_site_id
        JOIN kernel.studies AS study
            ON study.tenant_id = ss.tenant_id AND study.id = ss.study_id
        JOIN kernel.sites AS site
            ON site.tenant_id = ss.tenant_id AND site.id = ss.site_id
        WHERE le.tenant_id = @tenant
          AND le.to_kind = 'form_query'
          AND le.to_id = ANY(@ids)
          AND le.from_kind = 'enrollment'
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

    private const string AssertionPathsSql = """
        SELECT
            a.id AS "Id",
            s.sponsor_id AS "OrganizationId",
            a.study_id AS "StudyId",
            CASE WHEN ss.id IS NOT NULL AND site.id IS NOT NULL THEN site.country END AS "Country",
            CASE WHEN ss.id IS NOT NULL AND site.id IS NOT NULL THEN ss.id END AS "StudySiteId"
        FROM kernel.assertions AS a
        JOIN kernel.studies AS s
            ON s.tenant_id = a.tenant_id AND s.id = a.study_id
        LEFT JOIN kernel.assertions AS pa
            ON a.enrollment_id IS NULL
            AND (a.target_kind = 'assertion' OR a.retracts_id IS NOT NULL)
            AND pa.tenant_id = a.tenant_id
            AND pa.id = COALESCE(a.retracts_id, CASE WHEN a.target_kind = 'assertion' THEN a.target_id END)
        LEFT JOIN kernel.enrollments AS e
            ON e.tenant_id = a.tenant_id
            AND e.id = COALESCE(a.enrollment_id, pa.enrollment_id)
        LEFT JOIN kernel.study_sites AS ss
            ON ss.tenant_id = a.tenant_id
            AND ss.id = COALESCE(
                e.study_site_id,
                CASE WHEN a.target_kind = 'study_site' THEN a.target_id END,
                CASE WHEN pa.target_kind = 'study_site' THEN pa.target_id END)
        LEFT JOIN kernel.sites AS site
            ON site.tenant_id = ss.tenant_id AND site.id = ss.site_id
        WHERE a.tenant_id = @tenant AND a.id = ANY(@ids)
        """;

    private const string ValidationFindingPathsSql = """
        WITH latest_detection AS (
            SELECT DISTINCT ON (d.finding_id)
                d.finding_id,
                d.tenant_id,
                d.study_id,
                d.target_kind,
                d.target_key,
                substring(d.target_key FROM '([0-9a-fA-F]{{8}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{12}})')::uuid AS target_uuid
            FROM kernel.validation_detections AS d
            WHERE d.tenant_id = @tenant AND d.finding_id = ANY(@ids)
            ORDER BY d.finding_id, d.run_id DESC
        ),
        finding_base AS (
            SELECT
                ld.finding_id AS id,
                ld.tenant_id,
                ld.study_id,
                ld.target_kind,
                ld.target_key,
                ld.target_uuid
            FROM latest_detection AS ld
            UNION ALL
            SELECT
                vf.id,
                vf.tenant_id,
                vf.study_id,
                NULL::text AS target_kind,
                NULL::text AS target_key,
                NULL::uuid AS target_uuid
            FROM kernel.validation_findings AS vf
            WHERE vf.tenant_id = @tenant
              AND vf.id = ANY(@ids)
              AND NOT EXISTS (SELECT 1 FROM latest_detection AS ld WHERE ld.finding_id = vf.id)
        )
        SELECT
            fb.id AS "Id",
            s.sponsor_id AS "OrganizationId",
            fb.study_id AS "StudyId",
            CASE WHEN ss.id IS NOT NULL AND site.id IS NOT NULL THEN site.country END AS "Country",
            CASE WHEN ss.id IS NOT NULL AND site.id IS NOT NULL THEN ss.id END AS "StudySiteId"
        FROM finding_base AS fb
        JOIN kernel.studies AS s
            ON s.tenant_id = fb.tenant_id AND s.id = fb.study_id
        LEFT JOIN kernel.collection_instances AS ci
            ON fb.target_kind = 'DatasetRecord' AND fb.target_uuid IS NOT NULL
            AND ci.tenant_id = fb.tenant_id AND ci.id = fb.target_uuid
        LEFT JOIN kernel.observations AS obs
            ON fb.target_kind = 'Observation' AND fb.target_uuid IS NOT NULL
            AND obs.tenant_id = fb.tenant_id AND obs.id = fb.target_uuid
        LEFT JOIN kernel.enrollments AS e
            ON e.tenant_id = fb.tenant_id
            AND e.id = COALESCE(
                ci.enrollment_id,
                obs.enrollment_id,
                CASE WHEN fb.target_kind = 'DatasetRecord' THEN fb.target_uuid END)
        LEFT JOIN kernel.enrollments AS esubj
            ON e.id IS NULL AND fb.target_kind = 'DatasetRecord' AND fb.target_key IS NOT NULL
            AND esubj.tenant_id = fb.tenant_id AND esubj.study_id = fb.study_id
            AND esubj.subject_code = split_part(fb.target_key, ':', 1)
        LEFT JOIN kernel.study_sites AS ss
            ON ss.tenant_id = fb.tenant_id
            AND ss.id = COALESCE(ci.study_site_id, e.study_site_id, esubj.study_site_id)
        LEFT JOIN kernel.sites AS site
            ON site.tenant_id = ss.tenant_id AND site.id = ss.site_id
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

    public async Task<IReadOnlyDictionary<NodeRef, EnrollmentId>> BlindedNodeEnrollmentsAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var distinct = nodes.Distinct().ToList();
        var obsNodeIds = distinct
            .Where(n => n.Kind == NodeKinds.Observation)
            .Select(n => ObservationId.FromGuid(n.Id))
            .Distinct()
            .ToList();
        var assertionNodeIds = distinct
            .Where(n => n.Kind == NodeKinds.Assertion)
            .Select(n => AssertionId.FromGuid(n.Id))
            .Distinct()
            .ToList();
        var formQueryNodeIds = distinct
            .Where(n => n.Kind == NodeKinds.FormQuery)
            .Select(n => n.Id)
            .Distinct()
            .ToList();
        if (obsNodeIds.Count == 0 && assertionNodeIds.Count == 0 && formQueryNodeIds.Count == 0)
        {
            return new Dictionary<NodeRef, EnrollmentId>();
        }

        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var assertionTargetObs = new Dictionary<Guid, ObservationId>();
            if (assertionNodeIds.Count > 0)
            {
                var assertions = await db.Assertions.AsNoTracking()
                    .Where(a => a.TenantId == tenant && assertionNodeIds.Contains(a.Id))
                    .Select(a => new { a.Id, a.TargetKind, a.TargetId, a.RetractsId })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                var parentAssertionIds = assertions
                    .Select(a => a.RetractsId ?? (a.TargetKind == NodeKinds.Assertion ? AssertionId.FromGuid(a.TargetId) : (AssertionId?)null))
                    .Where(id => id is not null)
                    .Select(id => id!.Value)
                    .Distinct()
                    .ToList();

                var parentById = parentAssertionIds.Count == 0
                    ? new Dictionary<AssertionId, (string TargetKind, Guid TargetId)>()
                    : (await db.Assertions.AsNoTracking()
                            .Where(a => a.TenantId == tenant && parentAssertionIds.Contains(a.Id))
                            .Select(a => new { a.Id, a.TargetKind, a.TargetId })
                            .ToListAsync(cancellationToken).ConfigureAwait(false))
                        .ToDictionary(a => a.Id, a => (a.TargetKind, a.TargetId));

                foreach (var a in assertions)
                {
                    if (a.TargetKind == NodeKinds.Observation)
                    {
                        assertionTargetObs[a.Id.Value] = ObservationId.FromGuid(a.TargetId);
                    }
                    else
                    {
                        var parentId = a.RetractsId ?? (a.TargetKind == NodeKinds.Assertion ? AssertionId.FromGuid(a.TargetId) : (AssertionId?)null);
                        if (parentId is { } pid && parentById.TryGetValue(pid, out var parent) && parent.TargetKind == NodeKinds.Observation)
                        {
                            assertionTargetObs[a.Id.Value] = ObservationId.FromGuid(parent.TargetId);
                        }
                    }
                }
            }

            var formQueryTargetObs = new Dictionary<Guid, ObservationId>();
            if (formQueryNodeIds.Count > 0)
            {
                var queryEdges = await db.LineageEdges.AsNoTracking()
                    .Where(e => e.TenantId == tenant
                        && e.ToKind == NodeKinds.FormQuery
                        && formQueryNodeIds.Contains(e.ToId)
                        && e.FromKind == NodeKinds.Observation
                        && e.Kind == LineageEdgeKind.DerivedFrom)
                    .Select(e => new { e.ToId, e.FromId })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var edge in queryEdges)
                {
                    formQueryTargetObs[edge.ToId] = ObservationId.FromGuid(edge.FromId);
                }
            }

            var allObsIds = obsNodeIds
                .Concat(assertionTargetObs.Values)
                .Concat(formQueryTargetObs.Values)
                .Distinct()
                .ToList();
            if (allObsIds.Count == 0)
            {
                return new Dictionary<NodeRef, EnrollmentId>();
            }

            var observations = await db.Observations.AsNoTracking()
                .Where(o => o.TenantId == tenant && allObsIds.Contains(o.Id))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (observations.Count == 0)
            {
                return new Dictionary<NodeRef, EnrollmentId>();
            }

            var blindedObsToEnrollment = new Dictionary<ObservationId, EnrollmentId>();
            foreach (var studyGroup in observations.GroupBy(o => o.StudyId))
            {
                var blindedInStudy = await KernelLockRecords
                    .ResolveBlindedObservationsAsync(db, studyGroup.Key, studyGroup, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var obs in studyGroup.Where(obs => blindedInStudy.Contains(obs.Id)))
                {
                    blindedObsToEnrollment[obs.Id] = obs.EnrollmentId;
                }
            }

            if (blindedObsToEnrollment.Count == 0)
            {
                return new Dictionary<NodeRef, EnrollmentId>();
            }

            var result = new Dictionary<NodeRef, EnrollmentId>();
            foreach (var node in distinct)
            {
                if (node.Kind == NodeKinds.Observation
                    && blindedObsToEnrollment.TryGetValue(ObservationId.FromGuid(node.Id), out var enrollmentId))
                {
                    result[node] = enrollmentId;
                }
                else if (node.Kind == NodeKinds.Assertion
                    && assertionTargetObs.TryGetValue(node.Id, out var targetObsId)
                    && blindedObsToEnrollment.TryGetValue(targetObsId, out var assertionEnrollmentId))
                {
                    result[node] = assertionEnrollmentId;
                }
                else if (node.Kind == NodeKinds.FormQuery
                    && formQueryTargetObs.TryGetValue(node.Id, out var queryObsId)
                    && blindedObsToEnrollment.TryGetValue(queryObsId, out var queryEnrollmentId))
                {
                    result[node] = queryEnrollmentId;
                }
            }

            return result;
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
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

            case NodeKinds.FormQuery:
                // A discrepancy query belongs to the study site and study of its governing enrollment (ADR-2625).
                await LoadRawPathsAsync(db, tenant, kind, rawIds, FormQueryPathsSql, resolved, ct).ConfigureAwait(false);
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

            case NodeKinds.Observation:
                {
                    var ids = rawIds.Select(ObservationId.FromGuid).ToList();
                    var rows = await (
                            from o in db.Observations.AsNoTracking()
                            join e in db.Enrollments.AsNoTracking() on new { o.TenantId, Id = o.EnrollmentId } equals new { e.TenantId, e.Id }
                            join ss in db.StudySites.AsNoTracking() on new { e.TenantId, Id = e.StudySiteId } equals new { ss.TenantId, ss.Id }
                            join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                            join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                            where o.TenantId == tenant && ids.Contains(o.Id)
                            select new { o.Id, study.SponsorId, ss.StudyId, site.Country, StudySiteId = ss.Id })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId, row.Country, row.StudySiteId);
                    }

                    break;
                }

            case NodeKinds.CollectionInstance:
                {
                    var ids = rawIds.Select(CollectionInstanceId.FromGuid).ToList();
                    var rows = await (
                            from c in db.CollectionInstances.AsNoTracking()
                            join ss in db.StudySites.AsNoTracking() on new { c.TenantId, Id = c.StudySiteId } equals new { ss.TenantId, ss.Id }
                            join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                            join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                            where c.TenantId == tenant && ids.Contains(c.Id)
                            select new { c.Id, study.SponsorId, ss.StudyId, site.Country, StudySiteId = ss.Id })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId, row.Country, row.StudySiteId);
                    }

                    break;
                }

            case NodeKinds.Assertion:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AssertionPathsSql, resolved, ct).ConfigureAwait(false);
                break;

            case NodeKinds.ValidationFinding:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, ValidationFindingPathsSql, resolved, ct).ConfigureAwait(false);
                break;

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
