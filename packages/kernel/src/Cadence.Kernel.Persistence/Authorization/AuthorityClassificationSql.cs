using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Persistence.Authorization;

internal static class AuthorityClassificationSql
{
    // SqlQueryRaw passes SQL through string.Format, so regex quantifiers double their braces ({{n}} -> {n} in SQL).
    public const string UuidPatternSql = "[0-9a-f]{{8}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{12}}";

    public const string FindingTargetParseColumnsSql = $"""
            CASE
                WHEN d.target_kind = 'DatasetRecord'
                 AND d.target_key ~* '^{UuidPatternSql}$'
                    THEN d.target_key::uuid
                WHEN d.target_key ~* '^collection_instance:{UuidPatternSql}(@[0-9]+)?$'
                    THEN split_part(split_part(d.target_key, ':', 2), '@', 1)::uuid
            END AS collection_instance_id,
            CASE
                WHEN d.target_key ~* '^observation:{UuidPatternSql}(@[0-9]+)?$'
                    THEN split_part(split_part(d.target_key, ':', 2), '@', 1)::uuid
                WHEN d.target_kind = 'Observation'
                 AND d.target_key ~* '^{UuidPatternSql}$'
                    THEN d.target_key::uuid
            END AS observation_id,
            CASE
                WHEN d.target_key ~* '^enrollment:{UuidPatternSql}(@[0-9]+)?$'
                    THEN split_part(split_part(d.target_key, ':', 2), '@', 1)::uuid
                WHEN d.target_kind = 'DatasetRecord'
                 AND d.target_key ~* '^{UuidPatternSql}$'
                    THEN d.target_key::uuid
            END AS enrollment_id,
            CASE
                WHEN d.target_key ~* '^study_site:{UuidPatternSql}(@[0-9]+)?$'
                    THEN split_part(split_part(d.target_key, ':', 2), '@', 1)::uuid
            END AS direct_study_site_id,
            CASE
                WHEN d.target_kind = 'DatasetRecord' AND d.target_key IS NOT NULL
                    THEN split_part(d.target_key, ':', 1)
            END AS subject_code_candidate
        """;

    public const string ObservationClassificationSql = """
        SELECT
            o.id AS "Id",
            s.sponsor_id AS "OrganizationId",
            o.study_id AS "StudyId",
            ss_site.country AS "Country",
            ss.id AS "StudySiteId",
            o.concept_id AS "ConceptId",
            CASE WHEN @includeAllValues OR o.value ->> 'type' = 'missing' THEN o.value::text END AS "ValueJson",
            c.external_code AS "ConceptExternalCode",
            c.preferred_label AS "ConceptPreferredLabel",
            c.definition AS "ConceptDefinition",
            ci.collection_spec_id AS "CollectionSpecId",
            cim.item_id AS "ItemId"
        FROM kernel.observations AS o
        JOIN kernel.studies AS s
            ON s.tenant_id = o.tenant_id AND s.id = o.study_id
        LEFT JOIN kernel.enrollments AS e
            ON e.tenant_id = o.tenant_id AND e.id = o.enrollment_id
        LEFT JOIN kernel.study_sites AS ss
            ON ss.tenant_id = e.tenant_id AND ss.id = e.study_site_id
        LEFT JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        LEFT JOIN kernel.concepts AS c
            ON c.tenant_id = o.tenant_id AND c.id = o.concept_id
        LEFT JOIN kernel.collection_instance_members AS cim
            ON cim.tenant_id = o.tenant_id AND cim.observation_id = o.id
        LEFT JOIN kernel.collection_instances AS ci
            ON ci.tenant_id = cim.tenant_id AND ci.id = cim.collection_instance_id
        WHERE o.tenant_id = @tenant AND o.id = ANY(@ids)
        """;

    public const string FindingTargetSiteJoinsSql = """
        LEFT JOIN kernel.collection_instances AS vf_ci
            ON d.collection_instance_id IS NOT NULL
           AND vf_ci.tenant_id = d.tenant_id AND vf_ci.id = d.collection_instance_id AND vf_ci.study_id = d.study_id
        LEFT JOIN kernel.observations AS vf_obs
            ON d.observation_id IS NOT NULL
           AND vf_obs.tenant_id = d.tenant_id AND vf_obs.id = d.observation_id AND vf_obs.study_id = d.study_id
        LEFT JOIN kernel.enrollments AS vf_enr
            ON vf_ci.id IS NULL
           AND COALESCE(vf_obs.enrollment_id, d.enrollment_id) IS NOT NULL
           AND vf_enr.tenant_id = d.tenant_id AND vf_enr.id = COALESCE(vf_obs.enrollment_id, d.enrollment_id) AND vf_enr.study_id = d.study_id
        LEFT JOIN kernel.enrollments AS vf_subj_enr
            ON vf_ci.id IS NULL AND vf_enr.id IS NULL AND d.subject_code_candidate IS NOT NULL
           AND vf_subj_enr.tenant_id = d.tenant_id AND vf_subj_enr.study_id = d.study_id AND vf_subj_enr.subject_code = d.subject_code_candidate
        """;

    public static readonly string ValidationFindingPathsSql = $"""
        WITH detected AS (
            SELECT DISTINCT ON (d.finding_id)
                d.tenant_id,
                d.finding_id,
                d.study_id,
        {FindingTargetParseColumnsSql}
            FROM kernel.validation_detections AS d
            WHERE d.tenant_id = @tenant AND d.finding_id = ANY(@ids)
            ORDER BY d.finding_id, d.run_id DESC
        ),
        finding_base AS (
            SELECT * FROM detected
            UNION ALL
            SELECT
                vf.tenant_id,
                vf.id AS finding_id,
                vf.study_id,
                NULL::uuid AS collection_instance_id,
                NULL::uuid AS observation_id,
                NULL::uuid AS enrollment_id,
                NULL::uuid AS direct_study_site_id,
                NULL::text AS subject_code_candidate
            FROM kernel.validation_findings AS vf
            WHERE vf.tenant_id = @tenant
              AND vf.id = ANY(@ids)
              AND NOT EXISTS (SELECT 1 FROM detected AS d WHERE d.finding_id = vf.id)
        )
        SELECT
            d.finding_id AS "Id",
            s.sponsor_id AS "OrganizationId",
            s.id AS "StudyId",
            ss_site.country AS "Country",
            ss.id AS "StudySiteId",
            (
                {AuthorityScopePathSql.StudyMayHaveBlindedEvidenceSql("s.id")}
                OR EXISTS (
                    SELECT 1
                    FROM kernel.validation_detections AS vd_blind
                    WHERE vd_blind.tenant_id = @tenant
                      AND vd_blind.finding_id = d.finding_id
                      AND (
                          vd_blind.dataset ~* '{AuthorityScopePathSql.BlindedMarkerRegexSql}'
                          OR COALESCE(vd_blind.variable, '') ~* '{AuthorityScopePathSql.BlindedMarkerRegexSql}'
                          OR vd_blind.observed::text ~* '{AuthorityScopePathSql.BlindedMarkerRegexSql}'
                      )
                )
            ) AS "MayBeBlinded"
        FROM finding_base AS d
        JOIN kernel.studies AS s
            ON s.tenant_id = d.tenant_id AND s.id = d.study_id
        {FindingTargetSiteJoinsSql}
        LEFT JOIN kernel.study_sites AS ss
            ON ss.tenant_id = d.tenant_id
           AND ss.study_id = d.study_id
           AND ss.id = COALESCE(vf_ci.study_site_id, vf_enr.study_site_id, vf_subj_enr.study_site_id, d.direct_study_site_id)
        LEFT JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        """;

    public const string ValidationFindingClassificationSql = $"""
        WITH detected AS (
            SELECT DISTINCT ON (d.finding_id)
                d.tenant_id,
                d.finding_id,
                d.study_id,
                d.target_kind,
                d.dataset,
                d.target_key,
                d.variable,
                d.observed::text AS observed_json,
        {FindingTargetParseColumnsSql}
            FROM kernel.validation_detections AS d
            JOIN kernel.validation_runs AS r
                ON r.tenant_id = d.tenant_id AND r.id = d.run_id
            WHERE d.tenant_id = @tenant AND d.finding_id = ANY(@ids)
            ORDER BY d.finding_id, r.completed_at DESC, r.created_at DESC, r.id DESC
        )
        SELECT
            d.finding_id AS "Id",
            s.sponsor_id AS "OrganizationId",
            s.id AS "StudyId",
            ss_site.country AS "Country",
            ss.id AS "StudySiteId",
            d.target_kind AS "TargetKind",
            d.dataset AS "Dataset",
            d.target_key AS "TargetKey",
            d.variable AS "Variable",
            d.observed_json AS "ObservedJson",
            vf_ci.id AS "TargetFormId",
            vf_ci.collection_spec_id AS "TargetFormSpecId",
            vf_ci.enrollment_id AS "TargetFormEnrollmentId",
            vf_obs.id AS "TargetObservationId",
            vf_obs.concept_id AS "TargetObsConceptId",
            vf_obs.value::text AS "TargetObsValueJson",
            vf_obs_c.external_code AS "TargetObsConceptExternalCode",
            vf_obs_c.preferred_label AS "TargetObsConceptPreferredLabel",
            vf_obs_c.definition AS "TargetObsConceptDefinition",
            vf_obs_ci.collection_spec_id AS "TargetObsSpecId",
            vf_obs_cim.item_id AS "TargetObsItemId"
        FROM detected AS d
        JOIN kernel.studies AS s
            ON s.tenant_id = d.tenant_id AND s.id = d.study_id
        {FindingTargetSiteJoinsSql}
        LEFT JOIN kernel.study_sites AS ss
            ON ss.tenant_id = d.tenant_id
           AND ss.study_id = d.study_id
           AND ss.id = COALESCE(vf_ci.study_site_id, vf_enr.study_site_id, vf_subj_enr.study_site_id, d.direct_study_site_id)
        LEFT JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        LEFT JOIN kernel.concepts AS vf_obs_c
            ON vf_obs.id IS NOT NULL AND vf_obs_c.tenant_id = vf_obs.tenant_id AND vf_obs_c.id = vf_obs.concept_id
        LEFT JOIN kernel.collection_instance_members AS vf_obs_cim
            ON vf_obs.id IS NOT NULL AND vf_obs_cim.tenant_id = vf_obs.tenant_id AND vf_obs_cim.observation_id = vf_obs.id
        LEFT JOIN kernel.collection_instances AS vf_obs_ci
            ON vf_obs_cim.id IS NOT NULL AND vf_obs_ci.tenant_id = vf_obs_cim.tenant_id AND vf_obs_ci.id = vf_obs_cim.collection_instance_id
        """;

    public const string EnrollmentSiblingMembersSql = """
        SELECT
            sib.enrollment_id AS "EnrollmentId",
            sib.study_id AS "StudyId",
            sib.id AS "FormId",
            sib.collection_spec_id AS "CollectionSpecId",
            cim.item_id AS "ItemId",
            obs.id AS "ObservationId",
            obs.concept_id AS "ConceptId",
            obs.value::text AS "ValueJson",
            c.external_code AS "ConceptExternalCode",
            c.preferred_label AS "ConceptPreferredLabel",
            c.definition AS "ConceptDefinition"
        FROM kernel.collection_instances AS sib
        JOIN kernel.collection_instance_members AS cim
            ON cim.tenant_id = sib.tenant_id AND cim.collection_instance_id = sib.id
        JOIN kernel.observations AS obs
            ON obs.tenant_id = cim.tenant_id AND obs.id = cim.observation_id
        LEFT JOIN kernel.concepts AS c
            ON c.tenant_id = obs.tenant_id AND c.id = obs.concept_id
        WHERE sib.tenant_id = @tenant AND sib.enrollment_id = ANY(@enrollmentIds)
        """;

    public const string AttestationPathsSql = $"""
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
            ORDER BY d.finding_id, d.run_id DESC
        ),
        vf_scoped AS (
            SELECT
                d.tenant_id,
                d.finding_id,
                d.study_id,
                COALESCE(vf_ci.study_site_id, vf_enr.study_site_id, vf_subj_enr.study_site_id, d.direct_study_site_id) AS study_site_id
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
            ss.id AS "StudySiteId",
            FALSE AS "MayBeBlinded"
        FROM resolved_targets AS t
        JOIN kernel.studies AS s
            ON t.study_id IS NOT NULL AND s.tenant_id = t.tenant_id AND s.id = t.study_id
        LEFT JOIN kernel.study_sites AS ss
            ON t.study_site_id IS NOT NULL AND ss.tenant_id = t.tenant_id AND ss.study_id = t.study_id AND ss.id = t.study_site_id
        LEFT JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        """;
}

internal sealed record ObservationClassificationRow(
    Guid Id,
    Guid? OrganizationId,
    Guid? StudyId,
    string? Country,
    Guid? StudySiteId,
    Guid ConceptId,
    string? ValueJson,
    string? ConceptExternalCode,
    string? ConceptPreferredLabel,
    string? ConceptDefinition,
    string? CollectionSpecId,
    string? ItemId)
{
    public ScopePath? ToScopePath() => AuthorityScopePathSql.BuildScopePath(OrganizationId, StudyId, Country, StudySiteId);
}

internal sealed record ValidationFindingClassificationRow(
    Guid Id,
    Guid? OrganizationId,
    Guid? StudyId,
    string? Country,
    Guid? StudySiteId,
    string TargetKind,
    string Dataset,
    string TargetKey,
    string? Variable,
    string ObservedJson,
    Guid? TargetFormId,
    string? TargetFormSpecId,
    Guid? TargetFormEnrollmentId,
    Guid? TargetObservationId,
    Guid? TargetObsConceptId,
    string? TargetObsValueJson,
    string? TargetObsConceptExternalCode,
    string? TargetObsConceptPreferredLabel,
    string? TargetObsConceptDefinition,
    string? TargetObsSpecId,
    string? TargetObsItemId)
{
    public ScopePath? ToScopePath() => AuthorityScopePathSql.BuildScopePath(OrganizationId, StudyId, Country, StudySiteId);
}

internal sealed record SiblingMemberRow(
    Guid EnrollmentId,
    Guid StudyId,
    Guid FormId,
    string CollectionSpecId,
    string ItemId,
    Guid ObservationId,
    Guid ConceptId,
    string ValueJson,
    string? ConceptExternalCode,
    string? ConceptPreferredLabel,
    string? ConceptDefinition);

internal sealed record PendingSiblingFinding(
    Guid FindingId,
    ScopePath Scope,
    string TargetFormSpecId,
    string? Variable,
    IReadOnlyList<ObservedVariable> ObservedValues,
    Guid TargetFormId,
    Guid EnrollmentId);

internal sealed record ClassifiedSiblingMember(
    Guid FormId,
    string CollectionSpecId,
    string ItemId,
    bool IsBlinded);
