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
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cadence.Kernel.Persistence.Authorization;

/// <summary>
/// Reads grants, roles and scope paths for the authority evaluator through a context bound to the request's tenant
/// (ADR-2250). Read-only: no tracking. Bulk reads hold one connection lease across per-kind set queries so
/// <see cref="KernelTenantConnectionInterceptor"/> sets and clears the tenant once per bulk call (K-ID-2).
/// </summary>
internal sealed class KernelAuthorityStore(
    KernelTenantDbContextFactory contexts,
    IBlindedTreatmentClassifier? blindingClassifier = null) : IAuthorityStore
{
    private readonly IBlindedTreatmentClassifier _blinding = blindingClassifier ?? BlindedTreatmentClassifier.Instance;
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
        NodeKinds.CollectionInstance,
        NodeKinds.Observation,
        NodeKinds.Assertion,
        NodeKinds.Attestation,
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

    private const string CollectionInstancePathsSql = """
        SELECT
            ci.id AS "Id",
            s.sponsor_id AS "OrganizationId",
            ci.study_id AS "StudyId",
            ss_site.country AS "Country",
            ss.id AS "StudySiteId"
        FROM kernel.collection_instances AS ci
        JOIN kernel.studies AS s
            ON s.tenant_id = ci.tenant_id AND s.id = ci.study_id
        JOIN kernel.study_sites AS ss
            ON ss.tenant_id = ci.tenant_id AND ss.id = ci.study_site_id
        JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        WHERE ci.tenant_id = @tenant AND ci.id = ANY(@ids)
        """;

    private const string ObservationPathsSql = """
        SELECT
            o.id AS "Id",
            s.sponsor_id AS "OrganizationId",
            o.study_id AS "StudyId",
            ss_site.country AS "Country",
            ss.id AS "StudySiteId"
        FROM kernel.observations AS o
        JOIN kernel.enrollments AS e
            ON e.tenant_id = o.tenant_id AND e.id = o.enrollment_id
        JOIN kernel.studies AS s
            ON s.tenant_id = o.tenant_id AND s.id = o.study_id
        JOIN kernel.study_sites AS ss
            ON ss.tenant_id = e.tenant_id AND ss.id = e.study_site_id
        JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        WHERE o.tenant_id = @tenant AND o.id = ANY(@ids)
        """;

    private const string AssertionPathsSql = """
        SELECT
            a.id AS "Id",
            s.sponsor_id AS "OrganizationId",
            s.id AS "StudyId",
            ss_site.country AS "Country",
            ss.id AS "StudySiteId"
        FROM kernel.assertions AS a
        JOIN kernel.studies AS s
            ON s.tenant_id = a.tenant_id AND s.id = a.study_id
        LEFT JOIN kernel.enrollments AS e
            ON a.enrollment_id IS NOT NULL AND e.tenant_id = a.tenant_id AND e.id = a.enrollment_id
        LEFT JOIN kernel.study_sites AS ss
            ON ss.tenant_id = a.tenant_id
           AND ss.study_id = a.study_id
           AND ss.id = COALESCE(e.study_site_id, CASE WHEN a.target_kind = 'study_site' THEN a.target_id END)
        LEFT JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        WHERE a.tenant_id = @tenant AND a.id = ANY(@ids)
        """;

    // SqlQueryRaw passes SQL through string.Format, so regex quantifiers double their braces ({{n}} -> {n} in SQL).
    private const string UuidPatternSql = "[0-9a-f]{{8}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{12}}";

    private const string FindingTargetParseColumnsSql = $"""
            CASE
                WHEN d.target_kind = 'DatasetRecord'
                 AND d.target_key ~* '^{UuidPatternSql}$'
                    THEN d.target_key::uuid
                WHEN d.target_key ~* '^collection_instance:{UuidPatternSql}$'
                    THEN split_part(d.target_key, ':', 2)::uuid
            END AS collection_instance_id,
            CASE
                WHEN d.target_key ~* '^observation:{UuidPatternSql}$'
                    THEN split_part(d.target_key, ':', 2)::uuid
            END AS observation_id,
            CASE
                WHEN d.target_key ~* '^enrollment:{UuidPatternSql}$'
                    THEN split_part(d.target_key, ':', 2)::uuid
            END AS enrollment_id,
            CASE
                WHEN d.target_key ~* '^study_site:{UuidPatternSql}$'
                    THEN split_part(d.target_key, ':', 2)::uuid
            END AS direct_study_site_id
        """;

    private const string FindingTargetSiteJoinsSql = """
        LEFT JOIN kernel.collection_instances AS vf_ci
            ON d.collection_instance_id IS NOT NULL
           AND vf_ci.tenant_id = d.tenant_id AND vf_ci.id = d.collection_instance_id AND vf_ci.study_id = d.study_id
        LEFT JOIN kernel.observations AS vf_obs
            ON d.observation_id IS NOT NULL
           AND vf_obs.tenant_id = d.tenant_id AND vf_obs.id = d.observation_id AND vf_obs.study_id = d.study_id
        LEFT JOIN kernel.enrollments AS vf_enr
            ON COALESCE(vf_obs.enrollment_id, d.enrollment_id) IS NOT NULL
           AND vf_enr.tenant_id = d.tenant_id AND vf_enr.id = COALESCE(vf_obs.enrollment_id, d.enrollment_id) AND vf_enr.study_id = d.study_id
        """;

    private const string ValidationFindingPathsSql = $"""
        WITH detected AS (
            SELECT DISTINCT ON (d.finding_id)
                d.tenant_id,
                d.finding_id,
                d.study_id,
        {FindingTargetParseColumnsSql}
            FROM kernel.validation_detections AS d
            WHERE d.tenant_id = @tenant AND d.finding_id = ANY(@ids)
            ORDER BY d.finding_id
        )
        SELECT
            d.finding_id AS "Id",
            s.sponsor_id AS "OrganizationId",
            s.id AS "StudyId",
            ss_site.country AS "Country",
            ss.id AS "StudySiteId"
        FROM detected AS d
        JOIN kernel.studies AS s
            ON s.tenant_id = d.tenant_id AND s.id = d.study_id
        {FindingTargetSiteJoinsSql}
        LEFT JOIN kernel.study_sites AS ss
            ON ss.tenant_id = d.tenant_id
           AND ss.study_id = d.study_id
           AND ss.id = COALESCE(vf_ci.study_site_id, vf_enr.study_site_id, d.direct_study_site_id)
        LEFT JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        """;

    private const string AttestationPathsSql = $"""
        WITH requested AS (
            SELECT
                att.id,
                att.tenant_id,
                att.target_kind,
                att.target_id
            FROM kernel.attestations AS att
            WHERE att.tenant_id = @tenant AND att.id = ANY(@ids)
        ),
        vf_detected AS (
            SELECT DISTINCT ON (d.finding_id)
                d.tenant_id,
                d.finding_id,
                d.study_id,
        {FindingTargetParseColumnsSql}
            FROM requested AS req
            JOIN kernel.validation_detections AS d
                ON req.target_kind = 'validation_finding'
               AND d.tenant_id = req.tenant_id AND d.finding_id = req.target_id
            ORDER BY d.finding_id
        ),
        vf_scoped AS (
            SELECT
                d.tenant_id,
                d.finding_id,
                d.study_id,
                COALESCE(vf_ci.study_site_id, vf_enr.study_site_id, d.direct_study_site_id) AS study_site_id
            FROM vf_detected AS d
        {FindingTargetSiteJoinsSql}
        ),
        resolved_targets AS (
            SELECT
                req.id,
                req.tenant_id,
                COALESCE(pv.study_id, enr.study_id, art.study_id, ci.study_id, asrt.study_id, prop.study_id, vf.study_id) AS study_id,
                COALESCE(
                    enr.study_site_id,
                    ci.study_site_id,
                    asrt_enr.study_site_id,
                    CASE WHEN asrt.id IS NOT NULL AND asrt.target_kind = 'study_site' THEN asrt.target_id END,
                    vf.study_site_id) AS study_site_id
            FROM requested AS req
            LEFT JOIN kernel.protocol_versions AS pv
                ON req.target_kind = 'protocol_version' AND pv.tenant_id = req.tenant_id AND pv.id = req.target_id
            LEFT JOIN kernel.enrollments AS enr
                ON req.target_kind = 'enrollment' AND enr.tenant_id = req.tenant_id AND enr.id = req.target_id
            LEFT JOIN kernel.artifact_versions AS av
                ON req.target_kind = 'artifact_version' AND av.tenant_id = req.tenant_id AND av.id = req.target_id
            LEFT JOIN kernel.artifacts AS art
                ON art.tenant_id = av.tenant_id AND art.id = av.artifact_id AND art.study_id IS NOT NULL
            LEFT JOIN kernel.collection_instances AS ci
                ON req.target_kind = 'collection_instance' AND ci.tenant_id = req.tenant_id AND ci.id = req.target_id
            LEFT JOIN kernel.assertions AS asrt
                ON req.target_kind = 'assertion' AND asrt.tenant_id = req.tenant_id AND asrt.id = req.target_id
            LEFT JOIN kernel.enrollments AS asrt_enr
                ON asrt.enrollment_id IS NOT NULL AND asrt_enr.tenant_id = asrt.tenant_id AND asrt_enr.id = asrt.enrollment_id
            LEFT JOIN kernel.proposals AS prop
                ON req.target_kind = 'proposal' AND prop.tenant_id = req.tenant_id AND prop.id = req.target_id
            LEFT JOIN vf_scoped AS vf
                ON req.target_kind = 'validation_finding' AND vf.tenant_id = req.tenant_id AND vf.finding_id = req.target_id
        )
        SELECT
            t.id AS "Id",
            s.sponsor_id AS "OrganizationId",
            s.id AS "StudyId",
            ss_site.country AS "Country",
            ss.id AS "StudySiteId"
        FROM resolved_targets AS t
        JOIN kernel.studies AS s
            ON t.study_id IS NOT NULL AND s.tenant_id = t.tenant_id AND s.id = t.study_id
        LEFT JOIN kernel.study_sites AS ss
            ON t.study_site_id IS NOT NULL AND ss.tenant_id = t.tenant_id AND ss.study_id = t.study_id AND ss.id = t.study_site_id
        LEFT JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
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

    public async Task<IReadOnlyDictionary<NodeRef, EvidenceNodeClassification>> InspectEvidenceNodesAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var distinct = nodes.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<NodeRef, EvidenceNodeClassification>();
        }

        var requestedObsIds = distinct
            .Where(n => n.Kind == NodeKinds.Observation)
            .Select(n => ObservationId.FromGuid(n.Id))
            .Distinct()
            .ToList();
        var requestedFindingIds = distinct
            .Where(n => n.Kind == NodeKinds.ValidationFinding)
            .Select(n => ValidationFindingId.FromGuid(n.Id))
            .Distinct()
            .ToList();
        if (requestedObsIds.Count == 0 && requestedFindingIds.Count == 0)
        {
            return new Dictionary<NodeRef, EvidenceNodeClassification>();
        }

        var resolved = new Dictionary<(string Kind, Guid Id), EvidenceNodeClassification>();
        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var latestDetections = new Dictionary<ValidationFindingId, ValidationDetectionRecord>();
            var targetFormIds = new HashSet<CollectionInstanceId>();
            var directEnrollmentIds = new HashSet<EnrollmentId>();
            var directStudySiteIds = new HashSet<StudySiteId>();
            var obsIdsToLoad = new HashSet<ObservationId>(requestedObsIds);

            if (requestedFindingIds.Count > 0)
            {
                var detectionRows = await (
                        from d in db.Set<ValidationDetectionRecord>().AsNoTracking()
                        join r in db.Set<ValidationRun>().AsNoTracking() on d.RunId equals r.Id
                        where requestedFindingIds.Contains(d.FindingId)
                        orderby r.CompletedAt descending, r.CreatedAt descending, r.Id descending
                        select d)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var row in detectionRows)
                {
                    if (!latestDetections.TryAdd(row.FindingId, row))
                    {
                        continue;
                    }

                    if (TryParseCollectionInstanceTarget(row.Target, out var formId))
                    {
                        targetFormIds.Add(formId);
                    }
                    else if (TryParseObservationTarget(row.Target, out var targetObsId))
                    {
                        obsIdsToLoad.Add(targetObsId);
                    }
                    else if (TryParseEnrollmentTarget(row.Target, out var targetEnrollmentId))
                    {
                        directEnrollmentIds.Add(targetEnrollmentId);
                    }
                    else if (TryParseStudySiteTarget(row.Target, out var targetStudySiteId))
                    {
                        directStudySiteIds.Add(targetStudySiteId);
                    }
                }
            }

            var enrollmentFormsByEnrollment = new Dictionary<EnrollmentId, List<CollectionInstance>>();
            var targetFormsById = new Dictionary<CollectionInstanceId, CollectionInstance>();
            if (targetFormIds.Count > 0)
            {
                var formIdList = targetFormIds.ToList();
                var initialForms = await db.CollectionInstances.AsNoTracking()
                    .Include(c => c.Members)
                    .Where(c => c.TenantId == tenant && formIdList.Contains(c.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var findingEnrollmentIds = initialForms.Select(c => c.EnrollmentId).Distinct().ToList();
                var siblingForms = findingEnrollmentIds.Count == 0
                    ? initialForms
                    : await db.CollectionInstances.AsNoTracking()
                        .Include(c => c.Members)
                        .Where(c => c.TenantId == tenant && findingEnrollmentIds.Contains(c.EnrollmentId))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                foreach (var form in siblingForms)
                {
                    targetFormsById[form.Id] = form;
                    if (!enrollmentFormsByEnrollment.TryGetValue(form.EnrollmentId, out var list))
                    {
                        enrollmentFormsByEnrollment[form.EnrollmentId] = list = [];
                    }

                    list.Add(form);
                    foreach (var member in form.Members)
                    {
                        obsIdsToLoad.Add(member.ObservationId);
                    }
                }
            }

            var observationsById = new Dictionary<ObservationId, Observation>();
            var membershipByObservation = new Dictionary<ObservationId, (string SpecId, string ItemId)>();
            foreach (var form in targetFormsById.Values)
            {
                foreach (var member in form.Members)
                {
                    membershipByObservation[member.ObservationId] = (form.CollectionSpecId, member.ItemId);
                }
            }

            if (obsIdsToLoad.Count > 0)
            {
                var allObsIdList = obsIdsToLoad.ToList();
                var loadedObservations = await db.Observations.AsNoTracking()
                    .Where(o => o.TenantId == tenant && allObsIdList.Contains(o.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var obs in loadedObservations)
                {
                    observationsById[obs.Id] = obs;
                }

                var unmappedObsIds = loadedObservations
                    .Select(o => o.Id)
                    .Where(id => !membershipByObservation.ContainsKey(id))
                    .ToList();
                if (unmappedObsIds.Count > 0)
                {
                    var governingForms = await db.CollectionInstances.AsNoTracking()
                        .Include(c => c.Members)
                        .Where(c => c.TenantId == tenant && c.Members.Any(m => unmappedObsIds.Contains(m.ObservationId)))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var form in governingForms)
                    {
                        foreach (var member in form.Members)
                        {
                            membershipByObservation[member.ObservationId] = (form.CollectionSpecId, member.ItemId);
                        }
                    }
                }
            }

            var studyIds = observationsById.Values.Select(o => o.StudyId)
                .Concat(latestDetections.Values.Select(d => d.StudyId))
                .Distinct()
                .ToList();
            var enrollmentIds = observationsById.Values.Select(o => o.EnrollmentId)
                .Concat(targetFormsById.Values.Select(c => c.EnrollmentId))
                .Concat(directEnrollmentIds)
                .Distinct()
                .ToList();

            var studyScopes = new Dictionary<StudyId, ScopePath>();
            if (studyIds.Count > 0)
            {
                var studyRows = await db.Studies.AsNoTracking()
                    .Where(s => s.TenantId == tenant && studyIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.SponsorId })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var row in studyRows)
                {
                    studyScopes[row.Id] = new ScopePath(row.SponsorId, row.Id);
                }
            }

            var enrollmentScopes = new Dictionary<EnrollmentId, (StudyId StudyId, ScopePath Scope)>();
            if (enrollmentIds.Count > 0)
            {
                var enrollmentRows = await (
                        from e in db.Enrollments.AsNoTracking()
                        join ss in db.StudySites.AsNoTracking() on new { e.TenantId, Id = e.StudySiteId } equals new { ss.TenantId, ss.Id }
                        join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                        join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                        where e.TenantId == tenant && enrollmentIds.Contains(e.Id)
                        select new { e.Id, study.SponsorId, ss.StudyId, site.Country, StudySiteId = ss.Id })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var row in enrollmentRows)
                {
                    enrollmentScopes[row.Id] = (row.StudyId, new ScopePath(row.SponsorId, row.StudyId, row.Country, row.StudySiteId));
                }
            }

            var studySiteScopes = new Dictionary<StudySiteId, (StudyId StudyId, ScopePath Scope)>();
            if (directStudySiteIds.Count > 0)
            {
                var siteIdList = directStudySiteIds.ToList();
                var studySiteRows = await (
                        from ss in db.StudySites.AsNoTracking()
                        join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                        join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                        where ss.TenantId == tenant && siteIdList.Contains(ss.Id)
                        select new { ss.Id, study.SponsorId, ss.StudyId, site.Country })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var row in studySiteRows)
                {
                    studySiteScopes[row.Id] = (row.StudyId, new ScopePath(row.SponsorId, row.StudyId, row.Country, row.Id));
                }
            }

            var designsByStudy = new Dictionary<StudyId, List<ProtocolDesign>>();
            var conceptIdsToLoad = new HashSet<ConceptId>(observationsById.Values.Select(o => o.ConceptId));
            if (studyIds.Count > 0)
            {
                var protocolRows = await db.ProtocolVersions.AsNoTracking()
                    .Where(v => v.TenantId == tenant && studyIds.Contains(v.StudyId))
                    .Select(v => new { v.StudyId, v.Content })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var row in protocolRows)
                {
                    if (ProtocolDesignDocument.Read(row.Content).Design is not { } design)
                    {
                        continue;
                    }

                    if (!designsByStudy.TryGetValue(row.StudyId, out var list))
                    {
                        designsByStudy[row.StudyId] = list = [];
                    }

                    list.Add(design);
                    foreach (var spec in design.CollectionSpecs)
                    {
                        foreach (var item in spec.Items)
                        {
                            conceptIdsToLoad.Add(item.Concept.ConceptId);
                        }
                    }
                }
            }

            var conceptsById = new Dictionary<ConceptId, BlindedConceptMetadata>();
            if (conceptIdsToLoad.Count > 0)
            {
                var conceptIdList = conceptIdsToLoad.ToList();
                var conceptRows = await db.Concepts.AsNoTracking()
                    .Where(c => c.TenantId == tenant && conceptIdList.Contains(c.Id))
                    .Select(c => new { c.Id, c.ExternalCode, c.PreferredLabel, c.Definition })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var row in conceptRows)
                {
                    conceptsById[row.Id] = new BlindedConceptMetadata(row.ExternalCode, row.PreferredLabel, row.Definition);
                }
            }

            var blindedObservations = new Dictionary<ObservationId, bool>(observationsById.Count);
            foreach (var obs in observationsById.Values)
            {
                var designs = designsByStudy.TryGetValue(obs.StudyId, out var d) ? d : (IReadOnlyList<ProtocolDesign>)[];
                BlindedConceptMetadata? concept = conceptsById.TryGetValue(obs.ConceptId, out var meta) ? meta : null;
                membershipByObservation.TryGetValue(obs.Id, out var membership);
                blindedObservations[obs.Id] = _blinding.IsBlindedObservation(
                    obs.ConceptId,
                    obs.Value,
                    concept,
                    membership.SpecId,
                    membership.ItemId,
                    designs);
            }

            foreach (var obsId in requestedObsIds)
            {
                if (!observationsById.TryGetValue(obsId, out var obs))
                {
                    continue;
                }

                var scope = enrollmentScopes.TryGetValue(obs.EnrollmentId, out var enrollmentScope)
                    ? enrollmentScope.Scope
                    : studyScopes.GetValueOrDefault(obs.StudyId, ScopePath.TenantWide);
                resolved[(NodeKinds.Observation, obsId.Value)] = new EvidenceNodeClassification(scope, blindedObservations[obsId]);
            }

            foreach (var (findingId, detection) in latestDetections)
            {
                var studyScope = studyScopes.GetValueOrDefault(detection.StudyId, ScopePath.TenantWide);
                var designs = designsByStudy.TryGetValue(detection.StudyId, out var d) ? d : (IReadOnlyList<ProtocolDesign>)[];
                BlindedConceptMetadata? ResolveConcept(ConceptId id) => conceptsById.TryGetValue(id, out var meta) ? meta : null;

                var scope = studyScope;
                var isBlinded = _blinding.IsBlindedFinding(detection.Target, detection.ObservedValues, designs, ResolveConcept);

                if (TryParseObservationTarget(detection.Target, out var targetObsId)
                    && observationsById.TryGetValue(targetObsId, out var targetObs)
                    && targetObs.StudyId == detection.StudyId)
                {
                    if (enrollmentScopes.TryGetValue(targetObs.EnrollmentId, out var enrollmentScope)
                        && enrollmentScope.StudyId == detection.StudyId)
                    {
                        scope = enrollmentScope.Scope;
                    }

                    isBlinded |= blindedObservations[targetObs.Id];
                }
                else if (TryParseCollectionInstanceTarget(detection.Target, out var targetFormId)
                    && targetFormsById.TryGetValue(targetFormId, out var targetForm)
                    && targetForm.StudyId == detection.StudyId)
                {
                    if (enrollmentScopes.TryGetValue(targetForm.EnrollmentId, out var enrollmentScope)
                        && enrollmentScope.StudyId == detection.StudyId)
                    {
                        scope = enrollmentScope.Scope;
                    }

                    if (!isBlinded && enrollmentFormsByEnrollment.TryGetValue(targetForm.EnrollmentId, out var siblingForms))
                    {
                        isBlinded = ReferencesBlindedFormMember(detection, targetForm, siblingForms, blindedObservations);
                    }
                }
                else if (TryParseEnrollmentTarget(detection.Target, out var targetEnrollmentId)
                    && enrollmentScopes.TryGetValue(targetEnrollmentId, out var directEnrollmentScope)
                    && directEnrollmentScope.StudyId == detection.StudyId)
                {
                    scope = directEnrollmentScope.Scope;
                }
                else if (TryParseStudySiteTarget(detection.Target, out var targetStudySiteId)
                    && studySiteScopes.TryGetValue(targetStudySiteId, out var directSiteScope)
                    && directSiteScope.StudyId == detection.StudyId)
                {
                    scope = directSiteScope.Scope;
                }

                resolved[(NodeKinds.ValidationFinding, findingId.Value)] = new EvidenceNodeClassification(scope, isBlinded);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        var result = new Dictionary<NodeRef, EvidenceNodeClassification>();
        foreach (var node in distinct)
        {
            if (resolved.TryGetValue((node.Kind, node.Id), out var info))
            {
                result[node] = info;
            }
        }

        return result;
    }

    private static bool TryParseCollectionInstanceTarget(FindingTarget target, out CollectionInstanceId id)
    {
        if (target.Kind == FindingTargetKind.DatasetRecord && Guid.TryParse(target.Key, out var rawGuid))
        {
            id = CollectionInstanceId.FromGuid(rawGuid);
            return true;
        }

        if (NodeRef.TryParse(target.Key, out var parsed)
            && parsed is { Kind: NodeKinds.CollectionInstance, Version: null })
        {
            id = CollectionInstanceId.FromGuid(parsed.Id);
            return true;
        }

        id = default;
        return false;
    }

    private static bool TryParseObservationTarget(FindingTarget target, out ObservationId id)
    {
        if (NodeRef.TryParse(target.Key, out var parsed)
            && parsed is { Kind: NodeKinds.Observation, Version: null })
        {
            id = ObservationId.FromGuid(parsed.Id);
            return true;
        }

        id = default;
        return false;
    }

    private static bool TryParseEnrollmentTarget(FindingTarget target, out EnrollmentId id)
    {
        if (NodeRef.TryParse(target.Key, out var parsed)
            && parsed is { Kind: NodeKinds.Enrollment, Version: null })
        {
            id = EnrollmentId.FromGuid(parsed.Id);
            return true;
        }

        id = default;
        return false;
    }

    private static bool TryParseStudySiteTarget(FindingTarget target, out StudySiteId id)
    {
        if (NodeRef.TryParse(target.Key, out var parsed)
            && parsed is { Kind: NodeKinds.StudySite, Version: null })
        {
            id = StudySiteId.FromGuid(parsed.Id);
            return true;
        }

        id = default;
        return false;
    }

    private static bool ReferencesBlindedFormMember(
        ValidationDetectionRecord detection,
        CollectionInstance targetForm,
        IReadOnlyList<CollectionInstance> siblingForms,
        IReadOnlyDictionary<ObservationId, bool> blindedObservations) =>
        MatchesBlindedMember(targetForm.CollectionSpecId, detection.Target.Variable, targetForm, siblingForms, blindedObservations)
        || detection.ObservedValues.Any(observed =>
            MatchesBlindedMember(targetForm.CollectionSpecId, observed.Variable, targetForm, siblingForms, blindedObservations));

    private static bool MatchesBlindedMember(
        string defaultSpecId,
        string? variable,
        CollectionInstance targetForm,
        IReadOnlyList<CollectionInstance> siblingForms,
        IReadOnlyDictionary<ObservationId, bool> blindedObservations)
    {
        if (string.IsNullOrWhiteSpace(variable))
        {
            return false;
        }

        var dot = variable.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot < variable.Length - 1)
        {
            var specId = variable[..dot];
            var itemId = variable[(dot + 1)..];
            return siblingForms
                .Where(form => string.Equals(form.CollectionSpecId, specId, StringComparison.Ordinal))
                .SelectMany(form => form.Members)
                .Any(member => string.Equals(member.ItemId, itemId, StringComparison.Ordinal)
                    && blindedObservations.GetValueOrDefault(member.ObservationId));
        }

        if (targetForm.Members.Any(member =>
                string.Equals(member.ItemId, variable, StringComparison.Ordinal)
                && blindedObservations.GetValueOrDefault(member.ObservationId)))
        {
            return true;
        }

        return siblingForms
            .Where(form => !string.Equals(form.CollectionSpecId, defaultSpecId, StringComparison.Ordinal))
            .SelectMany(form => form.Members)
            .Any(member => string.Equals(member.ItemId, variable, StringComparison.Ordinal)
                && blindedObservations.GetValueOrDefault(member.ObservationId));
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

            case NodeKinds.CollectionInstance:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, CollectionInstancePathsSql, resolved, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Observation:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, ObservationPathsSql, resolved, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Assertion:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AssertionPathsSql, resolved, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Attestation:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AttestationPathsSql, resolved, ct).ConfigureAwait(false);
                break;

            case NodeKinds.ValidationFinding:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, ValidationFindingPathsSql, resolved, ct).ConfigureAwait(false);
                break;

            default:
                // Tenants, participants (who may be in several studies), roles, qualifications (held by a person across
                // studies), task configurations and reference data belong to no organization or study.
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
