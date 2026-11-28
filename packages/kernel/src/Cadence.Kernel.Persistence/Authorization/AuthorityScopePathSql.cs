using System;

using Cadence.Kernel.Authorization;

namespace Cadence.Kernel.Persistence.Authorization;

internal static class AuthorityScopePathSql
{
    public const string BlindedMarkerRegexSql =
        "(treatment[_ ]assignment|treatment[_ ]group|treatment[_ ]allocation|treatment[_ ]arm|rand_arm|randomi[sz]ation|randomized_to|assigned[_ ]intervention|investigational[_ ]product|kit[_ ]number|dispensation|unblind|blinded[_ ]treatment|study[_ ]drug)";

    public static string StudyMayHaveBlindedEvidenceSql(string studyIdExpr) => $"""
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

    public const string ProtocolElementPathsSql = """
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

    public static readonly string FormQueryPathsSql = $"""
        SELECT DISTINCT ON (le.to_id)
            le.to_id AS "Id",
            study.sponsor_id AS "OrganizationId",
            ss.study_id AS "StudyId",
            site.country AS "Country",
            ss.id AS "StudySiteId",
            {StudyMayHaveBlindedEvidenceSql("ss.study_id")} AS "MayBeBlinded"
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

    public const string GrantPathsSql = """
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

    public const string QualificationRequirementPathsSql = """
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

    public const string EffectivityPathsSql = """
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

    public const string CollectionInstancePathsSql = """
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

    public static readonly string ObservationPathsSql = $"""
        SELECT
            o.id AS "Id",
            s.sponsor_id AS "OrganizationId",
            o.study_id AS "StudyId",
            ss_site.country AS "Country",
            ss.id AS "StudySiteId",
            {StudyMayHaveBlindedEvidenceSql("o.study_id")} AS "MayBeBlinded"
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

    public static readonly string AssertionPathsSql = $"""
        SELECT
            a.id AS "Id",
            s.sponsor_id AS "OrganizationId",
            s.id AS "StudyId",
            ss_site.country AS "Country",
            ss.id AS "StudySiteId",
            {StudyMayHaveBlindedEvidenceSql("s.id")} AS "MayBeBlinded"
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
        """;

    public static ScopePath? BuildScopePath(Guid? organizationId, Guid? studyId, string? country, Guid? studySiteId)
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

internal sealed record ScopePathRow(
    Guid Id,
    Guid? OrganizationId,
    Guid? StudyId,
    string? Country,
    Guid? StudySiteId,
    bool MayBeBlinded)
{
    public ScopePath? ToScopePath() => AuthorityScopePathSql.BuildScopePath(OrganizationId, StudyId, Country, StudySiteId);
}
