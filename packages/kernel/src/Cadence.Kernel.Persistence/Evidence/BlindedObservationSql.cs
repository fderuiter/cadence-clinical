namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Shared PostgreSQL predicate evaluating <see cref="Cadence.Kernel.Evidence.BlindedTreatmentClassifier"/> markers
/// over <c>kernel.observations</c> (<c>o</c>), <c>kernel.collection_instance_members</c> (<c>cim</c>),
/// <c>kernel.collection_instances</c> (<c>ci</c>), <c>kernel.concepts</c> (<c>c</c>), and
/// <c>kernel.protocol_versions</c> (<c>K-AUTH-4</c>).
/// </summary>
internal static class BlindedObservationSql
{
    internal const string BlindedMarkerRegex =
        "(treatment[_ ]assignment|treatment[_ ]group|treatment[_ ]allocation|treatment[_ ]arm|rand_arm|randomi[sz]ation|randomized_to|assigned[_ ]intervention|investigational[_ ]product|kit[_ ]number|dispensation|unblind|blinded[_ ]treatment|study[_ ]drug)";

    internal static string Predicate(bool includeMaskedRevisions)
    {
        var maskedRevisionClause = includeMaskedRevisions
            ? """
                OR EXISTS (
                    SELECT 1
                    FROM kernel.observation_revisions AS mr
                    WHERE mr.tenant_id = o.tenant_id
                      AND mr.observation_id = o.id
                      AND mr.value ->> 'type' = 'missing'
                      AND mr.value ->> 'reason' = 'Masked'
                )
"""
            : string.Empty;

        return $"""
            (
                (o.value ->> 'type' = 'missing' AND o.value ->> 'reason' = 'Masked')
                {maskedRevisionClause}
                OR COALESCE(ci.collection_spec_id, '') ~* '{BlindedMarkerRegex}'
                OR COALESCE(cim.item_id, '') ~* '{BlindedMarkerRegex}'
                OR (
                    c.id IS NOT NULL
                    AND (COALESCE(c.external_code, '') || ' ' || c.preferred_label || ' ' || c.definition) ~* '{BlindedMarkerRegex}'
                )
                OR EXISTS (
                    SELECT 1
                    FROM kernel.protocol_versions AS pv
                    WHERE pv.tenant_id = o.tenant_id
                      AND pv.study_id = o.study_id
                      AND (pv.content ~* '{BlindedMarkerRegex}' OR pv.content LIKE '%"interventions"%')
                      AND pg_input_is_valid(pv.content, 'jsonb')
                      AND jsonb_typeof(pv.content::jsonb) = 'object'
                      AND (
                          EXISTS (
                              SELECT 1
                              FROM jsonb_array_elements(
                                  CASE
                                      WHEN jsonb_typeof(pv.content::jsonb -> 'interventions') = 'array'
                                          THEN pv.content::jsonb -> 'interventions'
                                      ELSE '[]'::jsonb
                                  END
                              ) AS iv
                              WHERE LOWER(iv -> 'product' ->> 'conceptId') = o.concept_id::text
                          )
                          OR EXISTS (
                              SELECT 1
                              FROM jsonb_array_elements(
                                  CASE
                                      WHEN jsonb_typeof(pv.content::jsonb -> 'collectionSpecs') = 'array'
                                          THEN pv.content::jsonb -> 'collectionSpecs'
                                      ELSE '[]'::jsonb
                                  END
                              ) AS spec
                              WHERE (ci.collection_spec_id IS NULL OR (spec ->> 'id') = ci.collection_spec_id)
                                AND (
                                    (
                                        (COALESCE(spec ->> 'id', '') || ' ' || COALESCE(spec ->> 'name', '') || ' ' || COALESCE(spec -> 'purpose' ->> 'operationalNeed', '')) ~* '{BlindedMarkerRegex}'
                                        AND (
                                            ci.collection_spec_id IS NOT NULL
                                            OR EXISTS (
                                                SELECT 1
                                                FROM jsonb_array_elements(
                                                    CASE
                                                        WHEN jsonb_typeof(spec -> 'items') = 'array'
                                                            THEN spec -> 'items'
                                                        ELSE '[]'::jsonb
                                                    END
                                                ) AS spec_it
                                                WHERE LOWER(spec_it -> 'concept' ->> 'conceptId') = o.concept_id::text
                                            )
                                        )
                                    )
                                    OR EXISTS (
                                        SELECT 1
                                        FROM jsonb_array_elements(
                                            CASE
                                                WHEN jsonb_typeof(spec -> 'items') = 'array'
                                                    THEN spec -> 'items'
                                                ELSE '[]'::jsonb
                                            END
                                        ) AS it
                                        WHERE (
                                            CASE
                                                WHEN cim.item_id IS NOT NULL THEN (it ->> 'id') = cim.item_id
                                                ELSE LOWER(it -> 'concept' ->> 'conceptId') = o.concept_id::text
                                            END
                                        )
                                        AND (COALESCE(it ->> 'id', '') || ' ' || COALESCE(it -> 'purpose' ->> 'operationalNeed', '') || ' ' || COALESCE(it ->> 'prompt', '')) ~* '{BlindedMarkerRegex}'
                                    )
                                )
                          )
                      )
                )
            )
            """;
    }
}
