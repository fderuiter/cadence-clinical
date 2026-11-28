using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
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
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
    };

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
        NodeKinds.FormVersionSnapshot,
        NodeKinds.StudyStandardsProfile,
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
        NodeKinds.ValidationRun,
        NodeKinds.ValidationFinding);

    private const string BlindedMarkerRegexSql =
        "(treatment[_ ]assignment|treatment[_ ]group|treatment[_ ]allocation|treatment[_ ]arm|rand_arm|randomi[sz]ation|randomized_to|assigned[_ ]intervention|investigational[_ ]product|kit[_ ]number|dispensation|unblind|blinded[_ ]treatment|study[_ ]drug)";

    private static string StudyMayHaveBlindedEvidenceSql(string studyIdExpr) => $"""
        (
            EXISTS (
                SELECT 1
                FROM kernel.protocol_versions AS pv_blind
                WHERE pv_blind.tenant_id = @tenant
                  AND pv_blind.study_id = {studyIdExpr}
                  AND (
                      CASE
                          WHEN LEFT(LTRIM(pv_blind.content), 1) = chr(123) THEN
                              COALESCE((pv_blind.content::jsonb -> 'collectionSpecs')::text, '') ~* '{BlindedMarkerRegexSql}'
                              OR jsonb_path_exists(pv_blind.content::jsonb, '$.interventions[*] ? (@.product != null)')
                          ELSE pv_blind.content ~* '{BlindedMarkerRegexSql}'
                      END
                  )
            )
            OR EXISTS (
                SELECT 1
                FROM kernel.concepts AS c_blind
                WHERE c_blind.tenant_id = @tenant
                  AND (COALESCE(c_blind.external_code, '') || ' ' || c_blind.preferred_label || ' ' || c_blind.definition) ~* '{BlindedMarkerRegexSql}'
            )
            OR EXISTS (
                SELECT 1
                FROM kernel.observations AS o_blind
                WHERE o_blind.tenant_id = @tenant
                  AND o_blind.study_id = {studyIdExpr}
                  AND o_blind.value::text ILIKE '%Masked%'
            )
            OR EXISTS (
                SELECT 1
                FROM kernel.collection_instances AS ci_blind
                WHERE ci_blind.tenant_id = @tenant
                  AND ci_blind.study_id = {studyIdExpr}
                  AND (
                      ci_blind.collection_spec_id ~* '{BlindedMarkerRegexSql}'
                      OR EXISTS (
                          SELECT 1
                          FROM kernel.collection_instance_members AS cim_blind
                          WHERE cim_blind.tenant_id = ci_blind.tenant_id
                            AND cim_blind.collection_instance_id = ci_blind.id
                            AND cim_blind.item_id ~* '{BlindedMarkerRegexSql}'
                      )
                  )
            )
        )
        """;

    private const string ProtocolElementPathsSql = """
        SELECT DISTINCT ON (le.to_id)
            le.to_id AS "Id",
            s.sponsor_id AS "OrganizationId",
            pv.study_id AS "StudyId",
            NULL::text AS "Country",
            NULL::uuid AS "StudySiteId",
            FALSE AS "MayBeBlinded"
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

    private static readonly string FormQueryPathsSql = $"""
        WITH base_rows AS (
            SELECT DISTINCT ON (le.to_id)
                le.to_id AS id,
                study.sponsor_id AS organization_id,
                ss.study_id AS study_id,
                site.country AS country,
                ss.id AS study_site_id
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
        ),
        study_blinding AS MATERIALIZED (
            SELECT
                ds.study_id,
                {StudyMayHaveBlindedEvidenceSql("ds.study_id")} AS may_be_blinded
            FROM (SELECT DISTINCT study_id FROM base_rows WHERE study_id IS NOT NULL) AS ds
        )
        SELECT
            b.id AS "Id",
            b.organization_id AS "OrganizationId",
            b.study_id AS "StudyId",
            b.country AS "Country",
            b.study_site_id AS "StudySiteId",
            COALESCE(sb.may_be_blinded, FALSE) AS "MayBeBlinded"
        FROM base_rows AS b
        LEFT JOIN study_blinding AS sb
            ON sb.study_id = b.study_id
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
            END AS "StudySiteId",
            FALSE AS "MayBeBlinded"
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
            END AS "StudySiteId",
            FALSE AS "MayBeBlinded"
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
            END AS "StudySiteId",
            FALSE AS "MayBeBlinded"
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
            ss.id AS "StudySiteId",
            FALSE AS "MayBeBlinded"
        FROM kernel.collection_instances AS ci
        JOIN kernel.studies AS s
            ON s.tenant_id = ci.tenant_id AND s.id = ci.study_id
        JOIN kernel.study_sites AS ss
            ON ss.tenant_id = ci.tenant_id AND ss.id = ci.study_site_id
        JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        WHERE ci.tenant_id = @tenant AND ci.id = ANY(@ids)
        """;

    private static readonly string ObservationPathsSql = $"""
        WITH base_rows AS (
            SELECT
                o.id AS id,
                s.sponsor_id AS organization_id,
                o.study_id AS study_id,
                ss_site.country AS country,
                ss.id AS study_site_id
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
        ),
        study_blinding AS MATERIALIZED (
            SELECT
                ds.study_id,
                {StudyMayHaveBlindedEvidenceSql("ds.study_id")} AS may_be_blinded
            FROM (SELECT DISTINCT study_id FROM base_rows WHERE study_id IS NOT NULL) AS ds
        )
        SELECT
            b.id AS "Id",
            b.organization_id AS "OrganizationId",
            b.study_id AS "StudyId",
            b.country AS "Country",
            b.study_site_id AS "StudySiteId",
            COALESCE(sb.may_be_blinded, FALSE) AS "MayBeBlinded"
        FROM base_rows AS b
        LEFT JOIN study_blinding AS sb
            ON sb.study_id = b.study_id
        """;

    private static readonly string AssertionPathsSql = $"""
        WITH base_rows AS (
            SELECT
                a.id AS id,
                s.sponsor_id AS organization_id,
                s.id AS study_id,
                ss_site.country AS country,
                ss.id AS study_site_id
            FROM kernel.assertions AS a
            JOIN kernel.studies AS s
                ON s.tenant_id = a.tenant_id AND s.id = a.study_id
            LEFT JOIN kernel.assertions AS pa
                ON a.enrollment_id IS NULL
               AND (a.target_kind = 'assertion' OR a.retracts_id IS NOT NULL)
               AND pa.tenant_id = a.tenant_id
               AND pa.id = COALESCE(a.retracts_id, CASE WHEN a.target_kind = 'assertion' THEN a.target_id END)
            LEFT JOIN kernel.enrollments AS e
                ON COALESCE(a.enrollment_id, pa.enrollment_id) IS NOT NULL
               AND e.tenant_id = a.tenant_id
               AND e.id = COALESCE(a.enrollment_id, pa.enrollment_id)
            LEFT JOIN kernel.study_sites AS ss
                ON ss.tenant_id = a.tenant_id
               AND ss.study_id = a.study_id
               AND ss.id = COALESCE(
                   e.study_site_id,
                   CASE WHEN a.target_kind = 'study_site' THEN a.target_id END,
                   CASE WHEN pa.target_kind = 'study_site' THEN pa.target_id END)
            LEFT JOIN kernel.sites AS ss_site
                ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
            WHERE a.tenant_id = @tenant AND a.id = ANY(@ids)
        ),
        study_blinding AS MATERIALIZED (
            SELECT
                ds.study_id,
                {StudyMayHaveBlindedEvidenceSql("ds.study_id")} AS may_be_blinded
            FROM (SELECT DISTINCT study_id FROM base_rows WHERE study_id IS NOT NULL) AS ds
        )
        SELECT
            b.id AS "Id",
            b.organization_id AS "OrganizationId",
            b.study_id AS "StudyId",
            b.country AS "Country",
            b.study_site_id AS "StudySiteId",
            COALESCE(sb.may_be_blinded, FALSE) AS "MayBeBlinded"
        FROM base_rows AS b
        LEFT JOIN study_blinding AS sb
            ON sb.study_id = b.study_id
        """;

    // SqlQueryRaw passes SQL through string.Format, so regex quantifiers double their braces ({{n}} -> {n} in SQL).
    private const string UuidPatternSql = "[0-9a-f]{{8}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{12}}";

    private const string FindingTargetParseColumnsSql = $"""
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

    private const string ObservationClassificationSql = """
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

    private const string FindingTargetSiteJoinsSql = """
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

    private static readonly string ValidationFindingPathsSql = $"""
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
        ),
        study_blinding AS MATERIALIZED (
            SELECT
                ds.study_id,
                {StudyMayHaveBlindedEvidenceSql("ds.study_id")} AS may_be_blinded
            FROM (SELECT DISTINCT study_id FROM finding_base WHERE study_id IS NOT NULL) AS ds
        )
        SELECT
            d.finding_id AS "Id",
            s.sponsor_id AS "OrganizationId",
            s.id AS "StudyId",
            ss_site.country AS "Country",
            ss.id AS "StudySiteId",
            (
                COALESCE(sb.may_be_blinded, FALSE)
                OR EXISTS (
                    SELECT 1
                    FROM kernel.validation_detections AS vd_blind
                    WHERE vd_blind.tenant_id = @tenant
                      AND vd_blind.finding_id = d.finding_id
                      AND (
                          vd_blind.dataset ~* '{BlindedMarkerRegexSql}'
                          OR COALESCE(vd_blind.variable, '') ~* '{BlindedMarkerRegexSql}'
                          OR vd_blind.observed::text ~* '{BlindedMarkerRegexSql}'
                      )
                )
            ) AS "MayBeBlinded"
        FROM finding_base AS d
        JOIN kernel.studies AS s
            ON s.tenant_id = d.tenant_id AND s.id = d.study_id
        LEFT JOIN study_blinding AS sb
            ON sb.study_id = s.id
        {FindingTargetSiteJoinsSql}
        LEFT JOIN kernel.study_sites AS ss
            ON ss.tenant_id = d.tenant_id
           AND ss.study_id = d.study_id
           AND ss.id = COALESCE(vf_ci.study_site_id, vf_enr.study_site_id, vf_subj_enr.study_site_id, d.direct_study_site_id)
        LEFT JOIN kernel.sites AS ss_site
            ON ss_site.tenant_id = ss.tenant_id AND ss_site.id = ss.site_id
        """;

    private const string ValidationFindingClassificationSql = $"""
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

    private const string EnrollmentSiblingMembersSql = """
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

    public async Task<IReadOnlyDictionary<ProtocolVersionId, int>> ProtocolVersionNumbersAsync(
        TenantId tenant, IReadOnlyCollection<ProtocolVersionId> versions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(versions);
        var ids = versions.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<ProtocolVersionId, int>();
        }

        await using var db = contexts.Create(tenant);
        var rows = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.TenantId == tenant && ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Number })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r => r.Number);
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

        var obsIds = distinct
            .Where(n => n.Kind == NodeKinds.Observation)
            .Select(n => n.Id)
            .Distinct()
            .ToArray();
        var findingIds = distinct
            .Where(n => n.Kind == NodeKinds.ValidationFinding)
            .Select(n => n.Id)
            .Distinct()
            .ToArray();
        var assertionIds = distinct
            .Where(n => n.Kind == NodeKinds.Assertion)
            .Select(n => AssertionId.FromGuid(n.Id))
            .Distinct()
            .ToList();
        var formQueryIds = distinct
            .Where(n => n.Kind == NodeKinds.FormQuery)
            .Select(n => n.Id)
            .Distinct()
            .ToList();
        if (obsIds.Length == 0
            && findingIds.Length == 0
            && assertionIds.Count == 0
            && formQueryIds.Count == 0)
        {
            return new Dictionary<NodeRef, EvidenceNodeClassification>();
        }

        var expectedEvidenceCount = obsIds.Length + findingIds.Length + assertionIds.Count + formQueryIds.Count;
        var resolvedPaths = new Dictionary<(string Kind, Guid Id), ScopePath>(expectedEvidenceCount);
        var resolvedEvidence = new Dictionary<(string Kind, Guid Id), EvidenceNodeClassification>(expectedEvidenceCount);
        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ClassifyEvidenceInOpenConnectionAsync(
                    db,
                    tenant,
                    obsIds,
                    findingIds,
                    assertionIds,
                    formQueryIds,
                    resolvedPaths,
                    resolvedEvidence,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        var result = new Dictionary<NodeRef, EvidenceNodeClassification>(distinct.Count);
        foreach (var node in distinct)
        {
            if (resolvedEvidence.TryGetValue((node.Kind, node.Id), out var info))
            {
                result[node] = info;
            }
        }

        return result;
    }

    public async Task<(IReadOnlyDictionary<NodeRef, ScopePath> Paths, IReadOnlyDictionary<NodeRef, EvidenceNodeClassification> Evidence)> ResolveVisibilityBatchAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> resources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var distinct = resources.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return (new Dictionary<NodeRef, ScopePath>(), new Dictionary<NodeRef, EvidenceNodeClassification>());
        }

        var byKind = distinct
            .Select(r => r.Unversioned())
            .Distinct()
            .Where(r => PathBearingKinds.Contains(r.Kind))
            .GroupBy(r => r.Kind, StringComparer.Ordinal)
            .ToList();

        var resolvedPaths = new Dictionary<(string Kind, Guid Id), ScopePath>(distinct.Count);
        var resolvedEvidence = new Dictionary<(string Kind, Guid Id), EvidenceNodeClassification>();
        var dummyPotentiallyBlinded = new HashSet<NodeRef>();
        if (byKind.Count > 0)
        {
            await using var db = contexts.Create(tenant);
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var obsIds = Array.Empty<Guid>();
                var findingIds = Array.Empty<Guid>();
                var assertionIds = new List<AssertionId>();
                var formQueryIds = new List<Guid>();

                foreach (var group in byKind)
                {
                    var ids = group.Select(r => r.Id).ToArray();
                    switch (group.Key)
                    {
                        case NodeKinds.Observation:
                            obsIds = ids;
                            break;
                        case NodeKinds.ValidationFinding:
                            findingIds = ids;
                            break;
                        case NodeKinds.Assertion:
                            assertionIds = [.. ids.Select(AssertionId.FromGuid)];
                            break;
                        case NodeKinds.FormQuery:
                            formQueryIds = [.. ids];
                            break;
                        default:
                            await LoadPathsForKindAsync(db, tenant, group.Key, ids, resolvedPaths, dummyPotentiallyBlinded, cancellationToken)
                                .ConfigureAwait(false);
                            break;
                    }
                }

                if (obsIds.Length > 0 || findingIds.Length > 0 || assertionIds.Count > 0 || formQueryIds.Count > 0)
                {
                    await ClassifyEvidenceInOpenConnectionAsync(
                            db,
                            tenant,
                            obsIds,
                            findingIds,
                            assertionIds,
                            formQueryIds,
                            resolvedPaths,
                            resolvedEvidence,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }

        var paths = new Dictionary<NodeRef, ScopePath>(distinct.Count);
        var evidence = new Dictionary<NodeRef, EvidenceNodeClassification>();
        foreach (var resource in distinct)
        {
            paths[resource] = resolvedPaths.GetValueOrDefault((resource.Kind, resource.Id), ScopePath.TenantWide);
            if (resolvedEvidence.TryGetValue((resource.Kind, resource.Id), out var info))
            {
                evidence[resource.Unversioned()] = info;
            }
        }

        return (paths, evidence);
    }

    private async Task ClassifyEvidenceInOpenConnectionAsync(
        KernelDbContext db,
        TenantId tenant,
        Guid[] requestedObsIds,
        Guid[] requestedFindingIds,
        List<AssertionId> requestedAssertionIds,
        List<Guid> requestedFormQueryIds,
        Dictionary<(string Kind, Guid Id), ScopePath> resolvedPaths,
        Dictionary<(string Kind, Guid Id), EvidenceNodeClassification> resolvedEvidence,
        CancellationToken cancellationToken)
    {
        var extraObsGuids = new HashSet<Guid>(requestedObsIds);
        var assertionTargetObs = new Dictionary<AssertionId, Guid>();
        var assertionScopeHints = new Dictionary<AssertionId, (StudyId StudyId, EnrollmentId? EnrollmentId, StudySiteId? DirectStudySiteId)>();
        var formQueryTargetObs = new Dictionary<Guid, Guid>();
        var formQueryEnrollments = new Dictionary<Guid, EnrollmentId>();

        if (requestedAssertionIds.Count > 0)
        {
            var assertions = await db.Assertions.AsNoTracking()
                .Where(a => a.TenantId == tenant && requestedAssertionIds.Contains(a.Id))
                .Select(a => new { a.Id, a.StudyId, a.EnrollmentId, a.TargetKind, a.TargetId, a.RetractsId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var parentAssertionIds = assertions
                .Select(a => a.RetractsId ?? (a.TargetKind == NodeKinds.Assertion ? AssertionId.FromGuid(a.TargetId) : (AssertionId?)null))
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            var parentById = parentAssertionIds.Count == 0
                ? new Dictionary<AssertionId, (EnrollmentId? EnrollmentId, string TargetKind, Guid TargetId)>()
                : (await db.Assertions.AsNoTracking()
                        .Where(a => a.TenantId == tenant && parentAssertionIds.Contains(a.Id))
                        .Select(a => new { a.Id, a.EnrollmentId, a.TargetKind, a.TargetId })
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false))
                    .ToDictionary(a => a.Id, a => (a.EnrollmentId, a.TargetKind, a.TargetId));

            foreach (var a in assertions)
            {
                var parentId = a.RetractsId ?? (a.TargetKind == NodeKinds.Assertion ? AssertionId.FromGuid(a.TargetId) : (AssertionId?)null);
                var hasParent = parentId is { } pid && parentById.TryGetValue(pid, out _);
                var parent = hasParent ? parentById[parentId!.Value] : default;

                var enrollmentId = a.EnrollmentId ?? (hasParent ? parent.EnrollmentId : null);
                StudySiteId? directSite = null;
                if (a.TargetKind == NodeKinds.StudySite)
                {
                    directSite = StudySiteId.FromGuid(a.TargetId);
                }
                else if (hasParent && parent.TargetKind == NodeKinds.StudySite)
                {
                    directSite = StudySiteId.FromGuid(parent.TargetId);
                }

                assertionScopeHints[a.Id] = (a.StudyId, enrollmentId, directSite);

                if (a.TargetKind == NodeKinds.Observation)
                {
                    assertionTargetObs[a.Id] = a.TargetId;
                    extraObsGuids.Add(a.TargetId);
                }
                else if (hasParent && parent.TargetKind == NodeKinds.Observation)
                {
                    assertionTargetObs[a.Id] = parent.TargetId;
                    extraObsGuids.Add(parent.TargetId);
                }
            }
        }

        if (requestedFormQueryIds.Count > 0)
        {
            var queryEdges = await db.LineageEdges.AsNoTracking()
                .Where(e => e.TenantId == tenant
                    && e.ToKind == NodeKinds.FormQuery
                    && requestedFormQueryIds.Contains(e.ToId)
                    && ((e.FromKind == NodeKinds.Observation && e.Kind == LineageEdgeKind.DerivedFrom)
                        || (e.FromKind == NodeKinds.Enrollment && e.Kind == LineageEdgeKind.GovernedBy)))
                .Select(e => new { e.ToId, e.FromKind, e.FromId, e.Kind })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var edge in queryEdges)
            {
                if (edge.FromKind == NodeKinds.Observation && edge.Kind == LineageEdgeKind.DerivedFrom)
                {
                    formQueryTargetObs[edge.ToId] = edge.FromId;
                    extraObsGuids.Add(edge.FromId);
                }
                else if (edge.FromKind == NodeKinds.Enrollment && edge.Kind == LineageEdgeKind.GovernedBy)
                {
                    formQueryEnrollments[edge.ToId] = EnrollmentId.FromGuid(edge.FromId);
                }
            }
        }

        var obsRows = extraObsGuids.Count > 0
            ? await QueryObservationClassificationRowsAsync(db, tenant, [.. extraObsGuids], cancellationToken).ConfigureAwait(false)
            : [];
        var findingRows = requestedFindingIds.Length > 0
            ? await QueryValidationFindingClassificationRowsAsync(db, tenant, requestedFindingIds, cancellationToken).ConfigureAwait(false)
            : [];

        var distinctStudyIds = new HashSet<StudyId>();
        foreach (var row in obsRows)
        {
            if (row.StudyId is { } studyGuid)
            {
                distinctStudyIds.Add(StudyId.FromGuid(studyGuid));
            }
        }

        foreach (var row in findingRows)
        {
            if (row.StudyId is { } studyGuid)
            {
                distinctStudyIds.Add(StudyId.FromGuid(studyGuid));
            }
        }

        var designsByStudy = await LoadStudyDesignsAsync(db, tenant, distinctStudyIds, cancellationToken)
            .ConfigureAwait(false);

        if (obsRows.Count > 0)
        {
            ClassifyObservationRows(obsRows, designsByStudy, resolvedPaths, resolvedEvidence);
        }

        if (findingRows.Count > 0)
        {
            await LoadValidationFindingClassificationsAsync(
                    db,
                    tenant,
                    findingRows,
                    designsByStudy,
                    resolvedPaths,
                    resolvedEvidence,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (assertionScopeHints.Count == 0 && requestedFormQueryIds.Count == 0)
        {
            return;
        }

        var neededStudyIds = assertionScopeHints.Values.Select(h => h.StudyId).Distinct().ToList();
        var neededEnrollmentIds = assertionScopeHints.Values
            .Where(h => h.EnrollmentId is not null)
            .Select(h => h.EnrollmentId!.Value)
            .Concat(formQueryEnrollments.Values)
            .Distinct()
            .ToList();
        var neededStudySiteIds = assertionScopeHints.Values
            .Where(h => h.DirectStudySiteId is not null)
            .Select(h => h.DirectStudySiteId!.Value)
            .Distinct()
            .ToList();

        var studyScopes = new Dictionary<StudyId, ScopePath>();
        if (neededStudyIds.Count > 0)
        {
            var studyRows = await db.Studies.AsNoTracking()
                .Where(s => s.TenantId == tenant && neededStudyIds.Contains(s.Id))
                .Select(s => new { s.Id, s.SponsorId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in studyRows)
            {
                studyScopes[row.Id] = new ScopePath(row.SponsorId, row.Id);
            }
        }

        var enrollmentScopes = new Dictionary<EnrollmentId, ScopePath>();
        if (neededEnrollmentIds.Count > 0)
        {
            var enrollmentRows = await (
                    from e in db.Enrollments.AsNoTracking()
                    join ss in db.StudySites.AsNoTracking() on new { e.TenantId, Id = e.StudySiteId } equals new { ss.TenantId, ss.Id }
                    join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                    join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                    where e.TenantId == tenant && neededEnrollmentIds.Contains(e.Id)
                    select new { e.Id, study.SponsorId, ss.StudyId, site.Country, StudySiteId = ss.Id })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in enrollmentRows)
            {
                enrollmentScopes[row.Id] = new ScopePath(row.SponsorId, row.StudyId, row.Country, row.StudySiteId);
            }
        }

        var studySiteScopes = new Dictionary<StudySiteId, ScopePath>();
        if (neededStudySiteIds.Count > 0)
        {
            var siteRows = await (
                    from ss in db.StudySites.AsNoTracking()
                    join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                    join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                    where ss.TenantId == tenant && neededStudySiteIds.Contains(ss.Id)
                    select new { ss.Id, study.SponsorId, ss.StudyId, site.Country })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in siteRows)
            {
                studySiteScopes[row.Id] = new ScopePath(row.SponsorId, row.StudyId, row.Country, row.Id);
            }
        }

        foreach (var (assertionId, hint) in assertionScopeHints)
        {
            var scope = studyScopes.GetValueOrDefault(hint.StudyId, ScopePath.TenantWide);
            if (hint.EnrollmentId is { } eid && enrollmentScopes.TryGetValue(eid, out var enrScope))
            {
                scope = enrScope;
            }
            else if (hint.DirectStudySiteId is { } sid && studySiteScopes.TryGetValue(sid, out var siteScope))
            {
                scope = siteScope;
            }

            var isBlinded = false;
            if (assertionTargetObs.TryGetValue(assertionId, out var targetObsGuid)
                && resolvedEvidence.TryGetValue((NodeKinds.Observation, targetObsGuid), out var obsInfo))
            {
                scope = obsInfo.Scope;
                isBlinded = obsInfo.IsBlinded;
            }

            resolvedPaths[(NodeKinds.Assertion, assertionId.Value)] = scope;
            resolvedEvidence[(NodeKinds.Assertion, assertionId.Value)] = new EvidenceNodeClassification(scope, isBlinded);
        }

        foreach (var queryId in requestedFormQueryIds)
        {
            EvidenceNodeClassification? targetObsInfo = null;
            if (formQueryTargetObs.TryGetValue(queryId, out var targetObsGuid)
                && resolvedEvidence.TryGetValue((NodeKinds.Observation, targetObsGuid), out var foundObsInfo))
            {
                targetObsInfo = foundObsInfo;
            }

            ScopePath? scope = null;
            if (formQueryEnrollments.TryGetValue(queryId, out var enrId)
                && enrollmentScopes.TryGetValue(enrId, out var enrScope))
            {
                scope = enrScope;
            }
            else if (targetObsInfo is not null)
            {
                scope = targetObsInfo.Scope;
            }

            if (scope is null)
            {
                continue;
            }

            var isBlinded = targetObsInfo?.IsBlinded ?? false;
            resolvedPaths[(NodeKinds.FormQuery, queryId)] = scope;
            resolvedEvidence[(NodeKinds.FormQuery, queryId)] = new EvidenceNodeClassification(scope, isBlinded);
        }
    }

    private Task<List<ObservationClassificationRow>> QueryObservationClassificationRowsAsync(
        KernelDbContext db,
        TenantId tenant,
        Guid[] rawIds,
        CancellationToken ct)
    {
        var includeAllValues = !ReferenceEquals(_blinding, BlindedTreatmentClassifier.Instance);
        object[] parameters =
        [
            new NpgsqlParameter<Guid>("tenant", tenant.Value),
            new NpgsqlParameter<Guid[]>("ids", rawIds),
            new NpgsqlParameter<bool>("includeAllValues", includeAllValues),
        ];
        return db.Database
            .SqlQueryRaw<ObservationClassificationRow>(ObservationClassificationSql, parameters)
            .ToListAsync(ct);
    }

    private static Task<List<ValidationFindingClassificationRow>> QueryValidationFindingClassificationRowsAsync(
        KernelDbContext db,
        TenantId tenant,
        Guid[] rawIds,
        CancellationToken ct)
    {
        object[] parameters =
        [
            new NpgsqlParameter<Guid>("tenant", tenant.Value),
            new NpgsqlParameter<Guid[]>("ids", rawIds),
        ];
        return db.Database
            .SqlQueryRaw<ValidationFindingClassificationRow>(ValidationFindingClassificationSql, parameters)
            .ToListAsync(ct);
    }

    private static async Task<Dictionary<Guid, List<ProtocolDesign>>> LoadStudyDesignsAsync(
        KernelDbContext db,
        TenantId tenant,
        HashSet<StudyId> studyIds,
        CancellationToken ct)
    {
        if (studyIds.Count == 0)
        {
            return [];
        }

        var ids = studyIds.ToList();
        var rows = await db.ProtocolVersions.AsNoTracking()
            .Where(pv => pv.TenantId == tenant && ids.Contains(pv.StudyId))
            .OrderBy(pv => pv.Number)
            .Select(pv => new { pv.StudyId, pv.Content })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var designsByStudy = new Dictionary<Guid, List<ProtocolDesign>>(ids.Count);
        foreach (var row in rows)
        {
            if (!designsByStudy.TryGetValue(row.StudyId.Value, out var designs))
            {
                designs = [];
                designsByStudy[row.StudyId.Value] = designs;
            }

            if (ProtocolDesignDocument.Read(row.Content).Design is { } design)
            {
                designs.Add(design);
            }
        }

        return designsByStudy;
    }

    private static async Task<Dictionary<ConceptId, BlindedConceptMetadata>> LoadConceptMetadataAsync(
        KernelDbContext db,
        TenantId tenant,
        HashSet<ConceptId> conceptIds,
        CancellationToken ct)
    {
        if (conceptIds.Count == 0)
        {
            return [];
        }

        var ids = conceptIds.ToList();
        var rows = await db.Concepts.AsNoTracking()
            .Where(c => c.TenantId == tenant && ids.Contains(c.Id))
            .Select(c => new { c.Id, c.ExternalCode, c.PreferredLabel, c.Definition })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var map = new Dictionary<ConceptId, BlindedConceptMetadata>(rows.Count);
        foreach (var row in rows)
        {
            map[row.Id] = new BlindedConceptMetadata(row.ExternalCode, row.PreferredLabel, row.Definition);
        }

        return map;
    }

    private void ClassifyObservationRows(
        List<ObservationClassificationRow> rows,
        Dictionary<Guid, List<ProtocolDesign>> designsByStudy,
        Dictionary<(string Kind, Guid Id), ScopePath> resolvedPaths,
        Dictionary<(string Kind, Guid Id), EvidenceNodeClassification> resolvedEvidence)
    {
        var isDefaultClassifier = ReferenceEquals(_blinding, BlindedTreatmentClassifier.Instance);
        Dictionary<(Guid? StudyId, Guid ConceptId, string? SpecId, string? ItemId), bool>? batchMemo =
            isDefaultClassifier ? new() : null;

        foreach (var row in rows)
        {
            var scope = row.ToScopePath() ?? ScopePath.TenantWide;
            resolvedPaths[(NodeKinds.Observation, row.Id)] = scope;

            var value = DeserializeValueForClassifier(row.ValueJson);
            if (batchMemo is not null && value is null)
            {
                var key = (row.StudyId, row.ConceptId, row.CollectionSpecId, row.ItemId);
                if (!batchMemo.TryGetValue(key, out var cachedBlinded))
                {
                    cachedBlinded = ClassifyObservationRow(row, value, designsByStudy);
                    batchMemo[key] = cachedBlinded;
                }

                resolvedEvidence[(NodeKinds.Observation, row.Id)] = new EvidenceNodeClassification(scope, cachedBlinded);
                continue;
            }

            var isBlinded = ClassifyObservationRow(row, value, designsByStudy);
            resolvedEvidence[(NodeKinds.Observation, row.Id)] = new EvidenceNodeClassification(scope, isBlinded);
        }
    }

    private bool ClassifyObservationRow(
        ObservationClassificationRow row,
        ObservationValue? value,
        Dictionary<Guid, List<ProtocolDesign>> designsByStudy)
    {
        IReadOnlyList<ProtocolDesign> designs = row.StudyId is { } studyGuid
            && designsByStudy.TryGetValue(studyGuid, out var studyDesigns)
            ? studyDesigns
            : [];
        BlindedConceptMetadata? concept = row.ConceptPreferredLabel is not null
            ? new BlindedConceptMetadata(row.ConceptExternalCode, row.ConceptPreferredLabel, row.ConceptDefinition)
            : null;
        return _blinding.IsBlindedObservation(
            ConceptId.FromGuid(row.ConceptId),
            value,
            concept,
            row.CollectionSpecId,
            row.ItemId,
            designs);
    }

    private ObservationValue? DeserializeValueForClassifier(string? valueJson)
    {
        if (string.IsNullOrEmpty(valueJson))
        {
            return null;
        }

        if (ReferenceEquals(_blinding, BlindedTreatmentClassifier.Instance)
            && !valueJson.Contains("missing", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ObservationValue>(valueJson, WebJsonOptions);
    }

    private async Task LoadValidationFindingClassificationsAsync(
        KernelDbContext db,
        TenantId tenant,
        List<ValidationFindingClassificationRow> rows,
        Dictionary<Guid, List<ProtocolDesign>> designsByStudy,
        Dictionary<(string Kind, Guid Id), ScopePath> resolvedPaths,
        Dictionary<(string Kind, Guid Id), EvidenceNodeClassification> resolvedEvidence,
        CancellationToken ct)
    {
        var conceptIdsByStudy = new Dictionary<Guid, HashSet<ConceptId>>();
        var distinctConceptIds = new HashSet<ConceptId>();
        foreach (var row in rows)
        {
            if (row.StudyId is not { } studyGuid || conceptIdsByStudy.ContainsKey(studyGuid))
            {
                continue;
            }

            var studyConceptIds = new HashSet<ConceptId>();
            if (designsByStudy.TryGetValue(studyGuid, out var studyDesigns))
            {
                foreach (var design in studyDesigns)
                {
                    foreach (var conceptRef in design.ExternalReferences().Concepts)
                    {
                        if (!conceptRef.ConceptId.IsEmpty)
                        {
                            studyConceptIds.Add(conceptRef.ConceptId);
                            distinctConceptIds.Add(conceptRef.ConceptId);
                        }
                    }
                }
            }

            conceptIdsByStudy[studyGuid] = studyConceptIds;
        }

        var conceptsById = await LoadConceptMetadataAsync(db, tenant, distinctConceptIds, ct).ConfigureAwait(false);
        var pendingSiblingFindings = new List<PendingSiblingFinding>();
        var pendingEnrollmentIds = new HashSet<Guid>();

        foreach (var row in rows)
        {
            var scope = row.ToScopePath() ?? ScopePath.TenantWide;
            resolvedPaths[(NodeKinds.ValidationFinding, row.Id)] = scope;

            IReadOnlyList<ProtocolDesign> designs = row.StudyId is { } studyGuid
                && designsByStudy.TryGetValue(studyGuid, out var studyDesigns)
                ? studyDesigns
                : [];
            var studyConcepts = row.StudyId is { } conceptStudyGuid
                && conceptIdsByStudy.TryGetValue(conceptStudyGuid, out var referencedIds)
                ? referencedIds
                : null;
            BlindedConceptMetadata? ResolveConcept(ConceptId id) =>
                studyConcepts is not null && studyConcepts.Contains(id) && conceptsById.TryGetValue(id, out var meta)
                    ? meta
                    : null;

            var targetKind = Enum.Parse<FindingTargetKind>(row.TargetKind, ignoreCase: true);
            var target = new FindingTarget(targetKind, row.Dataset, row.TargetKey, row.Variable);
            var rawObserved = JsonSerializer.Deserialize<string?[][]>(row.ObservedJson) ?? [];
            var observedValues = new List<ObservedVariable>(rawObserved.Length);
            foreach (var pair in rawObserved)
            {
                observedValues.Add(new ObservedVariable(pair[0]!, pair[1]));
            }

            var isBlinded = _blinding.IsBlindedFinding(target, observedValues, designs, ResolveConcept);
            if (!isBlinded && row.TargetObservationId is { } targetObsGuid && row.TargetObsConceptId is { } targetObsConceptGuid && row.TargetObsValueJson is not null)
            {
                if (resolvedEvidence.TryGetValue((NodeKinds.Observation, targetObsGuid), out var classifiedTargetObs))
                {
                    isBlinded = classifiedTargetObs.IsBlinded;
                }
                else
                {
                    var targetObsValue = DeserializeValueForClassifier(row.TargetObsValueJson);
                    BlindedConceptMetadata? targetObsConcept = row.TargetObsConceptPreferredLabel is not null
                        ? new BlindedConceptMetadata(row.TargetObsConceptExternalCode, row.TargetObsConceptPreferredLabel, row.TargetObsConceptDefinition)
                        : null;
                    isBlinded = _blinding.IsBlindedObservation(
                        ConceptId.FromGuid(targetObsConceptGuid),
                        targetObsValue,
                        targetObsConcept,
                        row.TargetObsSpecId,
                        row.TargetObsItemId,
                        designs);
                }
            }
            else if (!isBlinded
                && row.TargetFormId is { } targetFormId
                && row.TargetFormSpecId is not null
                && row.TargetFormEnrollmentId is { } targetEnrollmentId)
            {
                pendingSiblingFindings.Add(new PendingSiblingFinding(
                    row.Id,
                    scope,
                    row.TargetFormSpecId,
                    row.Variable,
                    observedValues,
                    targetFormId,
                    targetEnrollmentId));
                pendingEnrollmentIds.Add(targetEnrollmentId);
                continue;
            }

            resolvedEvidence[(NodeKinds.ValidationFinding, row.Id)] = new EvidenceNodeClassification(scope, isBlinded);
        }

        if (pendingSiblingFindings.Count == 0)
        {
            return;
        }

        var siblingsByEnrollment = await LoadClassifiedSiblingMembersByEnrollmentAsync(
                db,
                tenant,
                [.. pendingEnrollmentIds],
                designsByStudy,
                resolvedEvidence,
                ct)
            .ConfigureAwait(false);

        foreach (var pending in pendingSiblingFindings)
        {
            IReadOnlyList<ClassifiedSiblingMember> siblingMembers =
                siblingsByEnrollment.TryGetValue(pending.EnrollmentId, out var cachedSiblings)
                    ? cachedSiblings
                    : [];
            var isBlinded = ReferencesBlindedSiblingMember(
                pending.TargetFormSpecId,
                pending.Variable,
                pending.ObservedValues,
                pending.TargetFormId,
                siblingMembers);
            resolvedEvidence[(NodeKinds.ValidationFinding, pending.FindingId)] =
                new EvidenceNodeClassification(pending.Scope, isBlinded);
        }
    }

    private async Task<Dictionary<Guid, List<ClassifiedSiblingMember>>> LoadClassifiedSiblingMembersByEnrollmentAsync(
        KernelDbContext db,
        TenantId tenant,
        Guid[] enrollmentIds,
        Dictionary<Guid, List<ProtocolDesign>> designsByStudy,
        Dictionary<(string Kind, Guid Id), EvidenceNodeClassification> resolvedEvidence,
        CancellationToken ct)
    {
        object[] parameters =
        [
            new NpgsqlParameter<Guid>("tenant", tenant.Value),
            new NpgsqlParameter<Guid[]>("enrollmentIds", enrollmentIds),
        ];
        var rows = await db.Database
            .SqlQueryRaw<SiblingMemberRow>(EnrollmentSiblingMembersSql, parameters)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byEnrollment = new Dictionary<Guid, List<ClassifiedSiblingMember>>(enrollmentIds.Length);
        foreach (var m in rows)
        {
            if (!byEnrollment.TryGetValue(m.EnrollmentId, out var members))
            {
                members = [];
                byEnrollment[m.EnrollmentId] = members;
            }

            bool isBlinded;
            if (resolvedEvidence.TryGetValue((NodeKinds.Observation, m.ObservationId), out var classifiedObs))
            {
                isBlinded = classifiedObs.IsBlinded;
            }
            else
            {
                IReadOnlyList<ProtocolDesign> designs = designsByStudy.TryGetValue(m.StudyId, out var studyDesigns)
                    ? studyDesigns
                    : [];
                var value = DeserializeValueForClassifier(m.ValueJson);
                BlindedConceptMetadata? concept = m.ConceptPreferredLabel is not null
                    ? new BlindedConceptMetadata(m.ConceptExternalCode, m.ConceptPreferredLabel, m.ConceptDefinition)
                    : null;
                isBlinded = _blinding.IsBlindedObservation(
                    ConceptId.FromGuid(m.ConceptId),
                    value,
                    concept,
                    m.CollectionSpecId,
                    m.ItemId,
                    designs);
            }

            members.Add(new ClassifiedSiblingMember(m.FormId, m.CollectionSpecId, m.ItemId, isBlinded));
        }

        return byEnrollment;
    }

    private static bool ReferencesBlindedSiblingMember(
        string defaultSpecId,
        string? targetVariable,
        IReadOnlyList<ObservedVariable> observedValues,
        Guid targetFormId,
        IReadOnlyList<ClassifiedSiblingMember> siblingMembers) =>
        MatchesBlindedSiblingMember(defaultSpecId, targetVariable, targetFormId, siblingMembers)
        || observedValues.Any(observed =>
            MatchesBlindedSiblingMember(defaultSpecId, observed.Variable, targetFormId, siblingMembers));

    private static bool MatchesBlindedSiblingMember(
        string defaultSpecId,
        string? variable,
        Guid targetFormId,
        IReadOnlyList<ClassifiedSiblingMember> siblingMembers)
    {
        if (string.IsNullOrWhiteSpace(variable) || siblingMembers.Count == 0)
        {
            return false;
        }

        var dot = variable.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot < variable.Length - 1)
        {
            var specId = variable[..dot];
            var itemId = variable[(dot + 1)..];
            return siblingMembers.Any(m =>
                m.IsBlinded
                && string.Equals(m.CollectionSpecId, specId, StringComparison.Ordinal)
                && string.Equals(m.ItemId, itemId, StringComparison.Ordinal));
        }

        if (siblingMembers.Any(m =>
                m.IsBlinded
                && m.FormId == targetFormId
                && string.Equals(m.ItemId, variable, StringComparison.Ordinal)))
        {
            return true;
        }

        return siblingMembers.Any(m =>
            m.IsBlinded
            && !string.Equals(m.CollectionSpecId, defaultSpecId, StringComparison.Ordinal)
            && string.Equals(m.ItemId, variable, StringComparison.Ordinal));
    }

    public async Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var paths = await PathsOfAsync(tenant, [resource], cancellationToken).ConfigureAwait(false);
        return paths.GetValueOrDefault(resource, ScopePath.TenantWide);
    }

    public async Task<IReadOnlyDictionary<NodeRef, ScopePath>> PathsOfAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> resources, CancellationToken cancellationToken) =>
        (await ResolveVisibilityPathsAsync(tenant, resources, cancellationToken).ConfigureAwait(false)).Paths;

    public async Task<(IReadOnlyDictionary<NodeRef, ScopePath> Paths, IReadOnlySet<NodeRef>? PotentiallyBlindedEvidence)> ResolveVisibilityPathsAsync(
        TenantId tenant, IReadOnlyCollection<NodeRef> resources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var distinct = resources.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return (new Dictionary<NodeRef, ScopePath>(), FrozenSet<NodeRef>.Empty);
        }

        var byKind = distinct
            .Select(r => r.Unversioned())
            .Distinct()
            .Where(r => PathBearingKinds.Contains(r.Kind))
            .GroupBy(r => r.Kind, StringComparer.Ordinal)
            .ToList();

        var resolved = new Dictionary<(string Kind, Guid Id), ScopePath>();
        var potentiallyBlinded = new HashSet<NodeRef>();
        if (byKind.Count > 0)
        {
            await using var db = contexts.Create(tenant);
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var group in byKind)
                {
                    await LoadPathsForKindAsync(
                            db,
                            tenant,
                            group.Key,
                            [.. group.Select(r => r.Id)],
                            resolved,
                            potentiallyBlinded,
                            cancellationToken)
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

        var prefilteredBlinded = ReferenceEquals(_blinding, BlindedTreatmentClassifier.Instance)
            ? (IReadOnlySet<NodeRef>?)potentiallyBlinded
            : null;
        return (result, prefilteredBlinded);
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
        HashSet<NodeRef> potentiallyBlinded,
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

            case NodeKinds.FormVersionSnapshot:
                {
                    var ids = rawIds.Select(FormVersionSnapshotId.FromGuid).ToList();
                    var rows = await (
                            from snapshot in db.FormVersionSnapshots.AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { snapshot.TenantId, Id = snapshot.StudyId } equals new { study.TenantId, study.Id }
                            where snapshot.TenantId == tenant && ids.Contains(snapshot.Id)
                            select new { snapshot.Id, study.SponsorId, snapshot.StudyId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.StudyStandardsProfile:
                {
                    var ids = rawIds.Select(StudyStandardsProfileId.FromGuid).ToList();
                    var rows = await (
                            from profile in db.Set<Cadence.Kernel.Domain.Standards.StudyStandardsProfile>().AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { profile.TenantId, Id = profile.StudyId } equals new { study.TenantId, study.Id }
                            where profile.TenantId == tenant && ids.Contains(profile.Id)
                            select new { profile.Id, study.SponsorId, profile.StudyId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.ValidationRun:
                {
                    var ids = rawIds.Select(ValidationRunId.FromGuid).ToList();
                    var rows = await (
                            from run in db.Set<ValidationRun>().AsNoTracking()
                            join study in db.Studies.AsNoTracking() on new { run.TenantId, Id = run.StudyId } equals new { study.TenantId, study.Id }
                            where run.TenantId == tenant && ids.Contains(run.Id)
                            select new { run.Id, study.SponsorId, run.StudyId })
                        .ToListAsync(ct).ConfigureAwait(false);
                    foreach (var row in rows)
                    {
                        resolved[(kind, row.Id.Value)] = new ScopePath(row.SponsorId, row.StudyId);
                    }

                    break;
                }

            case NodeKinds.ProtocolElement:
                // An element belongs to the study of the protocol versions that govern it (ADR-2474).
                await LoadRawPathsAsync(db, tenant, kind, rawIds, ProtocolElementPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.FormQuery:
                // A discrepancy query belongs to the study site and study of its governing enrollment (ADR-2625).
                await LoadRawPathsAsync(db, tenant, kind, rawIds, FormQueryPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
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
                await LoadRawPathsAsync(db, tenant, kind, rawIds, GrantPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.QualificationRequirement:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, QualificationRequirementPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Effectivity:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, EffectivityPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
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
                await LoadRawPathsAsync(db, tenant, kind, rawIds, CollectionInstancePathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Observation:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, ObservationPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Assertion:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AssertionPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.Attestation:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, AttestationPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
                break;

            case NodeKinds.ValidationFinding:
                await LoadRawPathsAsync(db, tenant, kind, rawIds, ValidationFindingPathsSql, resolved, potentiallyBlinded, ct).ConfigureAwait(false);
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
        HashSet<NodeRef> potentiallyBlinded,
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

            if (row.MayBeBlinded)
            {
                potentiallyBlinded.Add(new NodeRef(kind, row.Id));
            }
        }
    }

    private sealed record ScopePathRow(
        Guid Id,
        Guid? OrganizationId,
        Guid? StudyId,
        string? Country,
        Guid? StudySiteId,
        bool MayBeBlinded)
    {
        public ScopePath? ToScopePath() => BuildScopePath(OrganizationId, StudyId, Country, StudySiteId);
    }

    private sealed record ObservationClassificationRow(
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
        public ScopePath? ToScopePath() => BuildScopePath(OrganizationId, StudyId, Country, StudySiteId);
    }

    private sealed record ValidationFindingClassificationRow(
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
        public ScopePath? ToScopePath() => BuildScopePath(OrganizationId, StudyId, Country, StudySiteId);
    }

    private sealed record SiblingMemberRow(
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

    private sealed record PendingSiblingFinding(
        Guid FindingId,
        ScopePath Scope,
        string TargetFormSpecId,
        string? Variable,
        IReadOnlyList<ObservedVariable> ObservedValues,
        Guid TargetFormId,
        Guid EnrollmentId);

    private sealed record ClassifiedSiblingMember(
        Guid FormId,
        string CollectionSpecId,
        string ItemId,
        bool IsBlinded);

    private static ScopePath? BuildScopePath(Guid? organizationId, Guid? studyId, string? country, Guid? studySiteId)
    {
        if (organizationId is not { } organization)
        {
            return null;
        }

        var study = studyId is { } studyGuid ? StudyId.FromGuid(studyGuid) : (StudyId?)null;
        var studySite = studySiteId is { } siteGuid ? StudySiteId.FromGuid(siteGuid) : (StudySiteId?)null;
        return new ScopePath(OrganizationId.FromGuid(organization), study, country, studySite);
    }
}
