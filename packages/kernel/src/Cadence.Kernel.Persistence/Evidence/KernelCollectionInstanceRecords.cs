using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Reads a form instance, its member observations, the assertions and locks that concern them and the signatures over
/// the form through a context bound to the caller's tenant (ADR-2250). Read-only: no tracking.
/// </summary>
internal sealed class KernelCollectionInstanceRecords(
    KernelTenantDbContextFactory contexts,
    IAuthorizationProbe? authorizationProbe = null,
    IBlindedTreatmentClassifier? blindingClassifier = null) : ICollectionInstanceRecords
{
    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    private const string BlindedMarkerRegexSql =
        "(treatment[_ ]assignment|treatment[_ ]group|treatment[_ ]allocation|treatment[_ ]arm|rand_arm|randomi[sz]ation|randomized_to|assigned[_ ]intervention|investigational[_ ]product|kit[_ ]number|dispensation|unblind|blinded[_ ]treatment|study[_ ]drug)";

    private static readonly string ObservationProjectionSql = $"""
        SELECT DISTINCT ON (o.id)
            o.id AS "Id",
            o.revision AS "Revision",
            (
                (o.value ->> 'type' = 'missing' AND o.value ->> 'reason' = 'Masked')
                OR COALESCE(ci.collection_spec_id, '') ~* '{BlindedMarkerRegexSql}'
                OR COALESCE(cim.item_id, '') ~* '{BlindedMarkerRegexSql}'
                OR (
                    c.id IS NOT NULL
                    AND (COALESCE(c.external_code, '') || ' ' || c.preferred_label || ' ' || c.definition) ~* '{BlindedMarkerRegexSql}'
                )
                OR EXISTS (
                    SELECT 1
                    FROM kernel.protocol_versions AS pv
                    WHERE pv.tenant_id = o.tenant_id
                      AND pv.study_id = o.study_id
                      AND (pv.content ~* '{BlindedMarkerRegexSql}' OR pv.content LIKE '%"interventions"%')
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
                                        (COALESCE(spec ->> 'id', '') || ' ' || COALESCE(spec ->> 'name', '') || ' ' || COALESCE(spec -> 'purpose' ->> 'operationalNeed', '')) ~* '{BlindedMarkerRegexSql}'
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
                                        AND (COALESCE(it ->> 'id', '') || ' ' || COALESCE(it -> 'purpose' ->> 'operationalNeed', '') || ' ' || COALESCE(it ->> 'prompt', '')) ~* '{BlindedMarkerRegexSql}'
                                    )
                                )
                          )
                      )
                )
            ) AS "IsBlinded"
        FROM kernel.observations AS o
        LEFT JOIN kernel.collection_instance_members AS cim
            ON cim.tenant_id = o.tenant_id AND cim.observation_id = o.id
        LEFT JOIN kernel.collection_instances AS ci
            ON ci.tenant_id = cim.tenant_id AND ci.id = cim.collection_instance_id
        LEFT JOIN kernel.concepts AS c
            ON c.tenant_id = o.tenant_id AND c.id = o.concept_id
        WHERE o.tenant_id = @tenant AND o.id = ANY(@ids)
        ORDER BY o.id, 3 DESC
        """;

    private readonly IBlindedTreatmentClassifier _blinding = blindingClassifier ?? BlindedTreatmentClassifier.Instance;

    public async Task<CollectionInstanceRecord?> FindForReaderAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        CancellationToken cancellationToken)
    {
        var record = await FindAsync(tenant, instance, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        if (authorizationProbe is null)
        {
            return record.MaskBlinded();
        }

        var decision = await authorizationProbe.CanIAsync(
            tenant,
            EnrollmentUnblindCapability,
            NodeRef.For(record.EnrollmentId),
            cancellationToken).ConfigureAwait(false);
        return decision.Permitted ? record : record.MaskBlinded();
    }

    public Task<CollectionInstanceRecord?> FindAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        CancellationToken cancellationToken) =>
        FindAsync(tenant, instance, governingDesign: null, cancellationToken);

    public async Task<CollectionInstanceRecord?> FindAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        ProtocolDesign? governingDesign,
        CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var found = await db.CollectionInstances.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == instance, cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var memberIds = found.Members.Select(m => m.ObservationId).ToList();
        var observations = (await db.Observations.AsNoTracking()
                .Where(o => memberIds.Contains(o.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(o => o.Id);
        var values = found.Members
            .OrderBy(m => m.ItemId, StringComparer.Ordinal)
            .Select(m => (m.ItemId, Observation: observations[m.ObservationId]))
            .Select(x => (x.ItemId, x.Observation, Current: x.Observation.Revisions.Single(r => r.Number == x.Observation.Revision)))
            .Select(x => new CollectedValueView(
                x.ItemId, x.Observation.Id, x.Observation.Concept,
                x.Observation.Value, x.Observation.ObservedAt, x.Observation.Revision, x.Current.RecordedBy, x.Current.RecordedAt))
            .ToList();

        // The locks at every scope covering the form or a value, and the assertions about its values.
        var scopes = LockScopes.Covering(found.StudyId, found.StudySiteId, found.EnrollmentId, found.Id, memberIds);
        var scoped = await ScopedAssertions.ReadAsync(db, scopes, cancellationToken).ConfigureAwait(false);
        var memberGuids = memberIds.Select(m => m.Value).ToHashSet();
        var assertions = scoped.About(a => a.TargetKind == NodeKinds.Observation && memberGuids.Contains(a.TargetId));

        // The form's layout and study protocol designs (for blinding classification, K-AUTH-4).
        List<ProtocolDesign> designs;
        ProtocolDesign? resolvedGoverningDesign;
        if (governingDesign is not null)
        {
            var otherVersions = await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.StudyId == found.StudyId && v.Id != found.ProtocolVersionId)
                .Select(v => v.Content)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            designs = [governingDesign];
            foreach (var content in otherVersions)
            {
                if (ProtocolDesignDocument.Read(content).Design is { } otherDesign)
                {
                    designs.Add(otherDesign);
                }
            }

            resolvedGoverningDesign = governingDesign;
        }
        else
        {
            var studyVersions = await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.StudyId == found.StudyId)
                .Select(v => new { v.Id, v.Content })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var designsById = studyVersions.ToDictionary(
                v => v.Id,
                v => ProtocolDesignDocument.Read(v.Content).Design);
            designs = designsById.Values
                .Where(d => d is not null)
                .Select(d => d!)
                .ToList();
            resolvedGoverningDesign = designsById.GetValueOrDefault(found.ProtocolVersionId);
        }

        var spec = resolvedGoverningDesign?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, found.CollectionSpecId, StringComparison.Ordinal));
        var items = spec?.Items ?? [];
        var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;
        var codelistGroups = await LoadCodelistGroupsAsync(db, items, cancellationToken).ConfigureAwait(false);
        var itemCodelists = MapItemCodelists(items, codelistGroups);

        var conceptIds = items.Select(i => i.Concept.ConceptId)
            .Concat(observations.Values.Select(o => o.ConceptId))
            .Distinct()
            .ToList();
        var concepts = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        // A signature names the revisions current when it was recorded; any later revision or value supersedes it (#5463).
        string? contentHash = values.Count == 0
            ? null
            : CollectionInstance.ContentHashOf(values.Select(v => new CollectionContentEntry(v.ObservationId, v.Revision, v.Value)));
        var instanceGuid = found.Id.Value;
        var signatures = await db.Attestations.AsNoTracking()
            .Where(a => a.TargetKind == NodeKinds.CollectionInstance && a.TargetId == instanceGuid)
            .OrderBy(a => a.RecordedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var attestations = signatures
            .Select(a => new FormAttestationView(
                a.Id, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason,
                !string.Equals(a.TargetContentHash, contentHash, StringComparison.Ordinal),
                SignedValues(found, observations, a.RecordedAt, a.TargetContentHash)))
            .ToList();
        var (blindedItemIds, blindedObservationIds) = ClassifyBlindedMembers(_blinding, found, collectionTitle, items, observations, concepts, designs, attestations);

        var activeErrorTargets = await KernelValidationStore.LoadActiveErrorFindingTargetsAsync(db, found.StudyId, cancellationToken)
            .ConfigureAwait(false);
        var (complete, missingRequired, openQueries, answeredQueries, errorFindings, blockers) =
            EvaluateFormReadiness(items, values, assertions, found.Id.Value, memberGuids, activeErrorTargets);
        var enrollmentVersion = await db.Enrollments.AsNoTracking()
            .Where(e => e.Id == found.EnrollmentId)
            .Select(e => (long?)e.VersionIndex)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? 1L;

        return new CollectionInstanceRecord(
            found.Id,
            found.EnrollmentId,
            found.ParticipantId,
            found.StudyId,
            found.StudySiteId,
            found.ProtocolVersionId,
            found.CollectionSpecId,
            found.PlannedActivityId,
            found.CollectedAt,
            found.OpenedBy,
            found.OpenedAt,
            found.VersionIndex,
            items,
            values,
            scoped.Locks(),
            assertions,
            attestations,
            contentHash,
            collectionTitle,
            enrollmentVersion,
            complete,
            missingRequired,
            openQueries,
            answeredQueries,
            errorFindings,
            blockers,
            blindedItemIds,
            blindedObservationIds,
            itemCodelists);
    }

    public async Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var instances = await db.CollectionInstances.AsNoTracking()
            .Where(c => c.EnrollmentId == enrollment)
            .OrderBy(c => c.OpenedAt).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instances.Count == 0)
        {
            return [];
        }

        var versionIds = instances.Select(c => c.ProtocolVersionId).Distinct().ToList();
        var rawVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => versionIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var specsByVersion = rawVersions.ToDictionary(
            v => v.Id,
            v => ProtocolDesignDocument.Read(v.Content).Design?.CollectionSpecs ?? []);

        var allMemberIds = instances.SelectMany(c => c.Members.Select(m => m.ObservationId)).Distinct().ToList();
        var observations = allMemberIds.Count == 0
            ? new Dictionary<ObservationId, ObservationSummaryRow>()
            : await db.Observations.AsNoTracking()
                .Where(o => allMemberIds.Contains(o.Id))
                .Select(o => new ObservationSummaryRow(o.Id, o.Value, o.Revision))
                .ToDictionaryAsync(o => o.Id, cancellationToken)
                .ConfigureAwait(false);

        var first = instances[0];
        List<NodeRef> allScopes =
        [
            NodeRef.For(first.StudyId),
            NodeRef.For(first.StudySiteId),
            NodeRef.For(enrollment),
            .. instances.Select(i => NodeRef.For(i.Id)),
            .. allMemberIds.Select(o => NodeRef.For(o)),
        ];
        var scoped = await ScopedAssertions.ReadAsync(db, allScopes, cancellationToken).ConfigureAwait(false);
        var allLocks = scoped.Locks();
        var allMemberGuids = allMemberIds.Select(o => o.Value).ToHashSet();
        var allMemberAssertions = scoped
            .About(a => a.TargetKind == NodeKinds.Observation && allMemberGuids.Contains(a.TargetId));
        var verifiedRevisions = allMemberAssertions
            .Where(a => a.Kind == AssertionKind.Verified && a.RetractedAt is null && a.Revision is not null)
            .Select(a => (a.Target.Id, Revision: a.Revision!.Value))
            .ToHashSet();

        var instanceGuids = instances.Select(i => i.Id.Value).ToList();
        var rawSignatures = await db.Attestations.AsNoTracking()
            .Where(a => a.TargetKind == NodeKinds.CollectionInstance && instanceGuids.Contains(a.TargetId))
            .Select(a => new { a.TargetId, a.TargetContentHash })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var signaturesByInstance = rawSignatures.ToLookup(a => a.TargetId, a => a.TargetContentHash);
        var activeErrorTargets = await KernelValidationStore.LoadActiveErrorFindingTargetsAsync(db, first.StudyId, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. instances.Select(instance => ToSummary(
                instance,
                specsByVersion.GetValueOrDefault(instance.ProtocolVersionId) ?? [],
                observations,
                allLocks,
                allMemberAssertions,
                verifiedRevisions,
                signaturesByInstance[instance.Id.Value],
                activeErrorTargets)),
        ];
    }

    public async Task<CollectionInstanceHeader?> FindHeaderAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        CancellationToken cancellationToken = default)
    {
        if (instance.IsEmpty)
        {
            return null;
        }

        await using var db = contexts.Create(tenant);
        return await SelectHeaders(db.CollectionInstances.AsNoTracking().Where(c => c.Id == instance))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>> FindHeadersAsync(
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instances,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var distinctIds = instances.Where(id => !id.IsEmpty).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<CollectionInstanceId, CollectionInstanceHeader>();
        }

        await using var db = contexts.Create(tenant);
        var headers = await SelectHeaders(db.CollectionInstances.AsNoTracking().Where(c => distinctIds.Contains(c.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return headers.ToDictionary(h => h.CollectionInstanceId);
    }

    private static IQueryable<CollectionInstanceHeader> SelectHeaders(IQueryable<CollectionInstance> query) =>
        query.Select(c => new CollectionInstanceHeader(
            c.Id,
            c.EnrollmentId,
            c.ParticipantId,
            c.StudyId,
            c.StudySiteId,
            c.ProtocolVersionId,
            c.CollectionSpecId,
            c.PlannedActivityId,
            c.CollectedAt,
            c.OpenedAt,
            c.VersionIndex));

    public async Task<FormAttestationView?> FindAttestationAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        AttestationId attestation,
        CancellationToken cancellationToken = default)
    {
        if (instance.IsEmpty || attestation.IsEmpty)
        {
            return null;
        }

        await using var db = contexts.Create(tenant);
        var loaded = await LoadAttestationCoreAsync(db, instance, attestation, cancellationToken).ConfigureAwait(false);
        return loaded?.View;
    }

    public async Task<FormAttestationView?> FindAttestationForReaderAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        AttestationId attestation,
        bool unblinded,
        ProtocolDesign? governingDesign = null,
        CancellationToken cancellationToken = default)
    {
        if (instance.IsEmpty || attestation.IsEmpty)
        {
            return null;
        }

        await using var db = contexts.Create(tenant);
        var loaded = await LoadAttestationCoreAsync(db, instance, attestation, cancellationToken).ConfigureAwait(false);
        if (loaded is not { } core)
        {
            return null;
        }

        if (unblinded)
        {
            return core.View;
        }

        List<ProtocolDesign> designs;
        ProtocolDesign? resolvedGoverningDesign;
        if (governingDesign is not null)
        {
            designs = [governingDesign];
            resolvedGoverningDesign = governingDesign;
        }
        else
        {
            var studyVersions = await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.StudyId == core.Found.StudyId)
                .Select(v => new { v.Id, v.Content })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var designsById = studyVersions.ToDictionary(
                v => v.Id,
                v => ProtocolDesignDocument.Read(v.Content).Design);
            designs = designsById.Values
                .Where(d => d is not null)
                .Select(d => d!)
                .ToList();
            resolvedGoverningDesign = designsById.GetValueOrDefault(core.Found.ProtocolVersionId);
        }

        var spec = resolvedGoverningDesign?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, core.Found.CollectionSpecId, StringComparison.Ordinal));
        var items = spec?.Items ?? [];
        var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;
        var conceptIds = items.Select(i => i.Concept.ConceptId)
            .Concat(core.Observations.Values.Select(o => o.ConceptId))
            .Distinct()
            .ToList();
        var concepts = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        var (blindedItemIds, blindedObservationIds) = ClassifyBlindedMembers(
            _blinding,
            core.Found,
            collectionTitle,
            items,
            core.Observations,
            concepts,
            designs,
            [core.View]);
        if (blindedItemIds.Count == 0 && blindedObservationIds.Count == 0)
        {
            return core.View;
        }

        return core.View with
        {
            ContentHash = "masked",
            Reason = "masked",
            SignedValues = core.View.SignedValues?
                .Select(sv =>
                {
                    if (!blindedItemIds.Contains(sv.ItemId) && !blindedObservationIds.Contains(sv.ObservationId))
                    {
                        return sv;
                    }

                    var maskedValue = sv.Value is MissingValue { Reason: NullFlavor.NotAsked }
                        ? sv.Value
                        : new MissingValue(NullFlavor.Masked);
                    return sv with
                    {
                        ObservationId = ObservationId.Empty,
                        Value = maskedValue,
                    };
                })
                .ToList(),
        };
    }

    private static async Task<(CollectionInstance Found, Dictionary<ObservationId, Observation> Observations, FormAttestationView View)?> LoadAttestationCoreAsync(
        KernelDbContext db,
        CollectionInstanceId instance,
        AttestationId attestation,
        CancellationToken cancellationToken)
    {
        var found = await db.CollectionInstances.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == instance, cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var instanceGuid = found.Id.Value;
        var signature = await db.Attestations.AsNoTracking()
            .Where(a => a.Id == attestation && a.TargetKind == NodeKinds.CollectionInstance && a.TargetId == instanceGuid)
            .Select(a => new { a.Id, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (signature is null)
        {
            return null;
        }

        var memberIds = found.Members.Select(m => m.ObservationId).ToList();
        var observations = memberIds.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : (await db.Observations.AsNoTracking()
                    .Where(o => memberIds.Contains(o.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(o => o.Id);
        var currentEntries = found.Members
            .Where(m => observations.ContainsKey(m.ObservationId))
            .OrderBy(m => m.ItemId, StringComparer.Ordinal)
            .Select(m => observations[m.ObservationId])
            .Select(o => new CollectionContentEntry(o.Id, o.Revision, o.Value))
            .ToList();
        var contentHash = currentEntries.Count == 0 ? null : CollectionInstance.ContentHashOf(currentEntries);
        var view = new FormAttestationView(
            signature.Id,
            signature.Meaning,
            signature.ActorId,
            signature.RecordedAt,
            signature.TargetContentHash,
            signature.Reason,
            !string.Equals(signature.TargetContentHash, contentHash, StringComparison.Ordinal),
            SignedValues(found, observations, signature.RecordedAt, signature.TargetContentHash));
        return (found, observations, view);
    }

    public async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget>> FindQueryTargetsAsync(
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instances,
        IReadOnlySet<EnrollmentId> unblindedEnrollments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(unblindedEnrollments);
        var distinctIds = instances.Where(id => !id.IsEmpty).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>();
        }

        await using var db = contexts.Create(tenant);
        var forms = await db.CollectionInstances.AsNoTracking()
            .Where(c => distinctIds.Contains(c.Id))
            .Select(c => new QueryTargetFormRow(
                c.Id,
                c.EnrollmentId,
                c.StudyId,
                c.StudySiteId,
                c.ProtocolVersionId,
                c.CollectionSpecId,
                c.PlannedActivityId,
                c.Members.Select(m => new MemberObservationRow(m.ItemId, m.ObservationId)).ToList()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (forms.Count == 0)
        {
            return new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>();
        }

        var allMemberIds = forms.SelectMany(f => f.Members.Select(m => m.ObservationId)).Distinct().ToList();
        var blindedForms = forms.Where(f => !unblindedEnrollments.Contains(f.EnrollmentId)).ToList();

        if (blindedForms.Count == 0)
        {
            var revisionsByObs = allMemberIds.Count == 0
                ? new Dictionary<ObservationId, int>()
                : await db.Observations.AsNoTracking()
                    .Where(o => allMemberIds.Contains(o.Id))
                    .Select(o => new { o.Id, o.Revision })
                    .ToDictionaryAsync(o => o.Id, o => o.Revision, cancellationToken)
                    .ConfigureAwait(false);

            var unblindedTargets = new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>(forms.Count);
            foreach (var form in forms)
            {
                var observationRevisions = new Dictionary<ObservationId, int>(form.Members.Count);
                var itemRevisions = new Dictionary<string, int>(form.Members.Count, StringComparer.Ordinal);
                foreach (var member in form.Members)
                {
                    if (!revisionsByObs.TryGetValue(member.ObservationId, out var revision))
                    {
                        continue;
                    }

                    itemRevisions[member.ItemId] = revision;
                    observationRevisions[member.ObservationId] = revision;
                }

                unblindedTargets[form.Id] = new CollectionInstanceQueryTarget(
                    form.Id,
                    form.EnrollmentId,
                    form.StudyId,
                    form.StudySiteId,
                    form.ProtocolVersionId,
                    form.CollectionSpecId,
                    form.PlannedActivityId,
                    observationRevisions,
                    itemRevisions,
                    FrozenSet<string>.Empty,
                    FrozenSet<ObservationId>.Empty);
            }

            return unblindedTargets;
        }

        if (db.Database.IsRelational() && ReferenceEquals(_blinding, BlindedTreatmentClassifier.Instance))
        {
            var projections = allMemberIds.Count == 0
                ? new Dictionary<ObservationId, CollectionObservationProjection>()
                : (await db.Database
                        .SqlQueryRaw<ObservationProjectionRow>(
                            ObservationProjectionSql,
                            new NpgsqlParameter<Guid>("tenant", tenant.Value),
                            new NpgsqlParameter<Guid[]>("ids", [.. allMemberIds.Select(id => id.Value)]))
                        .AsNoTracking()
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false))
                    .ToDictionary(
                        r => ObservationId.FromGuid(r.Id),
                        r => new CollectionObservationProjection(r.Revision, r.IsBlinded));

            var sqlTargets = new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>(forms.Count);
            foreach (var form in forms)
            {
                var isUnblinded = unblindedEnrollments.Contains(form.EnrollmentId);
                HashSet<string>? blindedItemsBuilder = null;
                if (!isUnblinded && _blinding.IsBlindedCollectionSpec(form.CollectionSpecId))
                {
                    blindedItemsBuilder = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var member in form.Members)
                    {
                        blindedItemsBuilder.Add(member.ItemId);
                    }
                }

                var observationRevisions = new Dictionary<ObservationId, int>(form.Members.Count);
                var itemRevisions = new Dictionary<string, int>(form.Members.Count, StringComparer.Ordinal);
                foreach (var member in form.Members)
                {
                    if (!projections.TryGetValue(member.ObservationId, out var proj))
                    {
                        continue;
                    }

                    itemRevisions[member.ItemId] = proj.Revision;
                    var memberBlinded = !isUnblinded
                        && (proj.IsBlinded
                            || (blindedItemsBuilder?.Contains(member.ItemId) ?? false)
                            || BlindedTreatmentClassifier.MatchesMarkers(member.ItemId));
                    if (memberBlinded)
                    {
                        blindedItemsBuilder ??= new HashSet<string>(StringComparer.Ordinal);
                        blindedItemsBuilder.Add(member.ItemId);
                    }
                    else
                    {
                        observationRevisions[member.ObservationId] = proj.Revision;
                    }
                }

                var blindedItems = blindedItemsBuilder is { Count: > 0 }
                    ? blindedItemsBuilder.ToFrozenSet(StringComparer.Ordinal)
                    : FrozenSet<string>.Empty;
                sqlTargets[form.Id] = new CollectionInstanceQueryTarget(
                    form.Id,
                    form.EnrollmentId,
                    form.StudyId,
                    form.StudySiteId,
                    form.ProtocolVersionId,
                    form.CollectionSpecId,
                    form.PlannedActivityId,
                    observationRevisions,
                    itemRevisions,
                    blindedItems,
                    FrozenSet<ObservationId>.Empty);
            }

            return sqlTargets;
        }

        var observations = allMemberIds.Count == 0
            ? new Dictionary<ObservationId, QueryTargetObservationRow>()
            : await db.Observations.AsNoTracking()
                .Where(o => allMemberIds.Contains(o.Id))
                .Select(o => new QueryTargetObservationRow(o.Id, o.ConceptId, o.Value, o.Revision))
                .ToDictionaryAsync(o => o.Id, cancellationToken)
                .ConfigureAwait(false);

        var blindedStudyIds = blindedForms.Select(f => f.StudyId).Distinct().ToList();
        var rawVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => blindedStudyIds.Contains(v.StudyId))
            .Select(v => new { v.Id, v.StudyId, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var designsByVersion = rawVersions.ToDictionary(
            v => v.Id,
            v => ProtocolDesignDocument.Read(v.Content).Design);
        var specsByVersion = designsByVersion.ToDictionary(
            kv => kv.Key,
            kv => kv.Value?.CollectionSpecs ?? []);
        var designsByStudy = rawVersions
            .Select(v => (v.StudyId, Design: designsByVersion[v.Id]))
            .Where(x => x.Design is not null)
            .GroupBy(x => x.StudyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProtocolDesign>)g.Select(x => x.Design!).ToList());

        var blindedMemberIds = blindedForms.SelectMany(f => f.Members.Select(m => m.ObservationId)).ToHashSet();
        var conceptIds = rawVersions
            .SelectMany(v => designsByVersion[v.Id]?.CollectionSpecs ?? [])
            .SelectMany(s => s.Items)
            .Select(i => i.Concept.ConceptId)
            .Concat(observations.Values.Where(o => blindedMemberIds.Contains(o.Id)).Select(o => o.ConceptId))
            .Distinct()
            .ToList();
        var concepts = conceptIds.Count == 0
            ? []
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .Select(c => new { c.Id, c.ExternalCode, c.PreferredLabel, c.Definition })
                .ToDictionaryAsync(
                    c => c.Id,
                    c => new BlindedConceptMetadata(c.ExternalCode, c.PreferredLabel, c.Definition),
                    cancellationToken)
                .ConfigureAwait(false);

        var targets = new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>(forms.Count);
        foreach (var form in forms)
        {
            var isUnblinded = unblindedEnrollments.Contains(form.EnrollmentId);
            IReadOnlySet<string> blindedItems = FrozenSet<string>.Empty;
            var blindedObservations = new HashSet<ObservationId>();
            if (!isUnblinded)
            {
                var specs = specsByVersion.GetValueOrDefault(form.ProtocolVersionId) ?? [];
                var spec = specs.FirstOrDefault(s => string.Equals(s.Id, form.CollectionSpecId, StringComparison.Ordinal));
                var items = spec?.Items ?? [];
                var title = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;
                var designs = designsByStudy.GetValueOrDefault(form.StudyId) ?? [];
                (blindedItems, blindedObservations) = ClassifyQueryTargetBlinding(
                    _blinding,
                    form,
                    title,
                    items,
                    observations,
                    concepts,
                    designs);
            }

            var observationRevisions = new Dictionary<ObservationId, int>(form.Members.Count);
            var itemRevisions = new Dictionary<string, int>(form.Members.Count, StringComparer.Ordinal);
            foreach (var member in form.Members)
            {
                if (!observations.TryGetValue(member.ObservationId, out var obs))
                {
                    continue;
                }

                itemRevisions[member.ItemId] = obs.Revision;
                if (isUnblinded || (!blindedItems.Contains(member.ItemId) && !blindedObservations.Contains(obs.Id)))
                {
                    observationRevisions[obs.Id] = obs.Revision;
                }
            }

            targets[form.Id] = new CollectionInstanceQueryTarget(
                form.Id,
                form.EnrollmentId,
                form.StudyId,
                form.StudySiteId,
                form.ProtocolVersionId,
                form.CollectionSpecId,
                form.PlannedActivityId,
                observationRevisions,
                itemRevisions,
                blindedItems,
                FrozenSet<ObservationId>.Empty);
        }

        return targets;
    }

    public async Task<CollectionInstanceScopeState?> FindScopeStateAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        CancellationToken cancellationToken = default)
    {
        if (instance.IsEmpty)
        {
            return null;
        }

        await using var db = contexts.Create(tenant);
        var found = await db.CollectionInstances.AsNoTracking()
            .Where(c => c.Id == instance)
            .Select(c => new
            {
                c.Id,
                c.EnrollmentId,
                c.StudyId,
                c.StudySiteId,
                c.ProtocolVersionId,
                c.CollectionSpecId,
                c.PlannedActivityId,
                c.VersionIndex,
                EnrollmentVersion = db.Enrollments
                    .Where(e => e.Id == c.EnrollmentId)
                    .Select(e => (long?)e.VersionIndex)
                    .FirstOrDefault() ?? 1L,
                Members = c.Members.Select(m => new MemberObservationRow(m.ItemId, m.ObservationId)).ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var memberIds = found.Members.Select(m => m.ObservationId).ToList();
        var observations = memberIds.Count == 0
            ? new Dictionary<ObservationId, ObservationSummaryRow>()
            : await db.Observations.AsNoTracking()
                .Where(o => memberIds.Contains(o.Id))
                .Select(o => new ObservationSummaryRow(o.Id, o.Value, o.Revision))
                .ToDictionaryAsync(o => o.Id, cancellationToken)
                .ConfigureAwait(false);

        var scopes = LockScopes.Covering(found.StudyId, found.StudySiteId, found.EnrollmentId, found.Id, memberIds);
        var locks = await ScopedAssertions.ReadLocksAsync(db, scopes, cancellationToken).ConfigureAwait(false);

        var values = found.Members
            .Where(m => observations.ContainsKey(m.ObservationId))
            .OrderBy(m => m.ItemId, StringComparer.Ordinal)
            .Select(m =>
            {
                var obs = observations[m.ObservationId];
                return new CollectionScopeValue(m.ItemId, obs.Id, obs.Value, obs.Revision);
            })
            .ToList();

        return new CollectionInstanceScopeState(
            found.Id,
            found.EnrollmentId,
            found.StudyId,
            found.StudySiteId,
            found.ProtocolVersionId,
            found.CollectionSpecId,
            found.PlannedActivityId,
            found.VersionIndex,
            found.EnrollmentVersion,
            values,
            locks);
    }

    public async Task<IReadOnlyList<CollectionRuleInput>> ListRuleInputsForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken = default)
    {
        if (enrollment.IsEmpty)
        {
            return [];
        }

        await using var db = contexts.Create(tenant);
        var forms = await db.CollectionInstances.AsNoTracking()
            .Where(c => c.EnrollmentId == enrollment)
            .OrderBy(c => c.OpenedAt).ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.EnrollmentId,
                c.StudyId,
                c.ProtocolVersionId,
                c.CollectionSpecId,
                c.OpenedAt,
                Members = c.Members.Select(m => new MemberObservationRow(m.ItemId, m.ObservationId)).ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (forms.Count == 0)
        {
            return [];
        }

        var versionIds = forms.Select(f => f.ProtocolVersionId).Distinct().ToList();
        var rawVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => versionIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var specsByVersion = rawVersions.ToDictionary(
            v => v.Id,
            v => ProtocolDesignDocument.Read(v.Content).Design?.CollectionSpecs ?? []);

        var allMemberIds = forms.SelectMany(f => f.Members.Select(m => m.ObservationId)).Distinct().ToList();
        var observations = allMemberIds.Count == 0
            ? new Dictionary<ObservationId, ObservationValue>()
            : await db.Observations.AsNoTracking()
                .Where(o => allMemberIds.Contains(o.Id))
                .Select(o => new { o.Id, o.Value })
                .ToDictionaryAsync(o => o.Id, o => o.Value, cancellationToken)
                .ConfigureAwait(false);

        var inputs = new List<CollectionRuleInput>(forms.Count);
        foreach (var form in forms)
        {
            var specs = specsByVersion.GetValueOrDefault(form.ProtocolVersionId) ?? [];
            var spec = specs.FirstOrDefault(s => string.Equals(s.Id, form.CollectionSpecId, StringComparison.Ordinal));
            var items = spec?.Items ?? [];
            var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;
            var values = form.Members
                .Where(m => observations.ContainsKey(m.ObservationId))
                .OrderBy(m => m.ItemId, StringComparer.Ordinal)
                .Select(m => new CollectionRuleValue(m.ItemId, m.ObservationId, observations[m.ObservationId]))
                .ToList();
            inputs.Add(new CollectionRuleInput(
                form.Id,
                form.EnrollmentId,
                form.StudyId,
                form.ProtocolVersionId,
                form.CollectionSpecId,
                form.OpenedAt,
                items,
                values,
                collectionTitle));
        }

        return inputs;
    }

    public async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>> FindManyAsync(
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instances,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var distinctIds = instances.Where(id => !id.IsEmpty).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<CollectionInstanceId, CollectionInstanceRecord>();
        }

        await using var db = contexts.Create(tenant);
        var records = await LoadRecordsAsync(
            db,
            db.CollectionInstances.AsNoTracking().Where(c => distinctIds.Contains(c.Id)),
            cancellationToken,
            _blinding).ConfigureAwait(false);
        return records.ToDictionary(r => r.CollectionInstanceId);
    }

    public async Task<IReadOnlyDictionary<ObservationId, int>> FindObservationRevisionsAsync(
        TenantId tenant,
        IReadOnlyCollection<ObservationId> observations,
        IReadOnlyCollection<CollectionInstanceId>? instances = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var distinctIds = observations.Where(id => !id.IsEmpty).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<ObservationId, int>();
        }

        await using var db = contexts.Create(tenant);
        return await db.Observations.AsNoTracking()
            .Where(o => distinctIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Revision })
            .ToDictionaryAsync(x => x.Id, x => x.Revision, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<ObservationId, CollectionObservationProjection>> FindObservationProjectionsAsync(
        TenantId tenant,
        IReadOnlyCollection<ObservationId> observations,
        IReadOnlyCollection<CollectionInstanceId>? instances = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var distinctIds = observations.Where(id => !id.IsEmpty).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<ObservationId, CollectionObservationProjection>();
        }

        await using var db = contexts.Create(tenant);
        if (db.Database.IsRelational() && ReferenceEquals(_blinding, BlindedTreatmentClassifier.Instance))
        {
            object[] parameters =
            [
                new NpgsqlParameter<Guid>("tenant", tenant.Value),
                new NpgsqlParameter<Guid[]>("ids", [.. distinctIds.Select(id => id.Value)]),
            ];
            var rows = await db.Database
                .SqlQueryRaw<ObservationProjectionRow>(ObservationProjectionSql, parameters)
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return rows.ToDictionary(
                r => ObservationId.FromGuid(r.Id),
                r => new CollectionObservationProjection(r.Revision, r.IsBlinded));
        }

        return await LoadObservationProjectionsFallbackAsync(db, distinctIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CollectionQueryTarget?> FindQueryTargetAsync(
        TenantId tenant,
        CollectionInstanceId instance,
        ObservationId? observation = null,
        string? itemId = null,
        bool checkLocks = true,
        CancellationToken cancellationToken = default)
    {
        if (instance.IsEmpty)
        {
            return null;
        }

        var hasObservation = observation is { IsEmpty: false };
        var targetObservation = observation ?? default;
        var trimmedItemId = itemId?.Trim();
        var hasItem = !hasObservation && !string.IsNullOrWhiteSpace(trimmedItemId);

        await using var db = contexts.Create(tenant);
        var openedExplicitly = false;
        if (checkLocks && db.Database.IsRelational())
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            openedExplicitly = true;
        }

        try
        {
            QueryTargetRow? row;
            if (hasObservation || hasItem)
            {
                var matchedMembers =
                    from m in db.Set<CollectionInstanceMember>().AsNoTracking()
                    join o in db.Observations.AsNoTracking()
                        on new { m.TenantId, Id = m.ObservationId } equals new { o.TenantId, o.Id }
                    where hasObservation ? m.ObservationId == targetObservation : m.ItemId == trimmedItemId
                    select new
                    {
                        m.TenantId,
                        m.CollectionInstanceId,
                        m.ItemId,
                        ObservationId = (ObservationId?)o.Id,
                        Revision = (int?)o.Revision,
                        o.Value,
                    };

                row = await (
                        from c in db.CollectionInstances.AsNoTracking()
                        join e in db.Enrollments.AsNoTracking()
                            on new { c.TenantId, Id = c.EnrollmentId } equals new { e.TenantId, e.Id }
                        join mm in matchedMembers
                            on new { c.TenantId, CollectionInstanceId = c.Id } equals new { mm.TenantId, mm.CollectionInstanceId }
                            into mmGroup
                        from mm in mmGroup.DefaultIfEmpty()
                        where c.Id == instance
                        select new QueryTargetRow(
                            c.Id,
                            c.StudyId,
                            c.StudySiteId,
                            c.EnrollmentId,
                            c.CollectionSpecId,
                            c.PlannedActivityId,
                            c.VersionIndex,
                            e.VersionIndex,
                            mm != null ? mm.ItemId : null,
                            mm != null ? mm.ObservationId : null,
                            mm != null ? mm.Revision : null,
                            mm != null ? mm.Value : null))
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                row = await (
                        from c in db.CollectionInstances.AsNoTracking()
                        join e in db.Enrollments.AsNoTracking()
                            on new { c.TenantId, Id = c.EnrollmentId } equals new { e.TenantId, e.Id }
                        where c.Id == instance
                        select new QueryTargetRow(
                            c.Id,
                            c.StudyId,
                            c.StudySiteId,
                            c.EnrollmentId,
                            c.CollectionSpecId,
                            c.PlannedActivityId,
                            c.VersionIndex,
                            e.VersionIndex,
                            null,
                            null,
                            null,
                            null))
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (row is null)
            {
                return null;
            }

            var isLocked = false;
            if (checkLocks)
            {
                var studyGuid = row.StudyId.Value;
                var siteGuid = row.StudySiteId.Value;
                var enrollmentGuid = row.EnrollmentId.Value;
                var formGuid = row.Id.Value;
                var lockObsGuid = hasObservation ? targetObservation.Value : row.ObservationId?.Value;
                Guid[] targetGuids = lockObsGuid is { } og
                    ? [studyGuid, siteGuid, enrollmentGuid, formGuid, og]
                    : [studyGuid, siteGuid, enrollmentGuid, formGuid];

                isLocked = await db.Assertions.AsNoTracking()
                    .AnyAsync(
                        a => a.Kind == AssertionKind.Locked
                            && targetGuids.Contains(a.TargetId)
                            && ((a.TargetKind == NodeKinds.Study && a.TargetId == studyGuid)
                                || (a.TargetKind == NodeKinds.StudySite && a.TargetId == siteGuid)
                                || (a.TargetKind == NodeKinds.Enrollment && a.TargetId == enrollmentGuid)
                                || (a.TargetKind == NodeKinds.CollectionInstance && a.TargetId == formGuid)
                                || (lockObsGuid != null && a.TargetKind == NodeKinds.Observation && a.TargetId == lockObsGuid.Value))
                            && !db.Assertions.Any(r => r.TenantId == a.TenantId && r.RetractsId == a.Id),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return new CollectionQueryTarget(
                row.Id,
                row.StudyId,
                row.StudySiteId,
                row.EnrollmentId,
                row.CollectionSpecId,
                row.PlannedActivityId,
                row.VersionIndex,
                row.EnrollmentVersion,
                isLocked,
                row.ItemId,
                row.ObservationId,
                row.Revision,
                row.Value is MissingValue { Reason: NullFlavor.NotAsked });
        }
        finally
        {
            if (openedExplicitly)
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await LoadRecordsAsync(
            db,
            db.CollectionInstances.AsNoTracking().Where(c => c.EnrollmentId == enrollment),
            cancellationToken,
            _blinding).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CollectionInstanceRecord>> ListForStudyAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await LoadRecordsAsync(
            db,
            db.CollectionInstances.AsNoTracking().Where(c => c.StudyId == study),
            cancellationToken,
            _blinding).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<CollectionInstanceRecord>> LoadRecordsAsync(
        KernelDbContext db,
        IQueryable<CollectionInstance> query,
        CancellationToken cancellationToken,
        IBlindedTreatmentClassifier? blindingClassifier = null)
    {
        var blinding = blindingClassifier ?? BlindedTreatmentClassifier.Instance;
        var instances = await query
            .OrderBy(c => c.OpenedAt).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instances.Count == 0)
        {
            return [];
        }

        var studyIds = instances.Select(c => c.StudyId).Distinct().ToList();
        var rawStudyVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => studyIds.Contains(v.StudyId))
            .Select(v => new { v.Id, v.StudyId, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var designsByVersion = rawStudyVersions.ToDictionary(
            v => v.Id,
            v => ProtocolDesignDocument.Read(v.Content).Design);
        var specsByVersion = designsByVersion.ToDictionary(
            kv => kv.Key,
            kv => kv.Value?.CollectionSpecs ?? []);
        var designsByStudy = rawStudyVersions
            .Select(v => (v.StudyId, Design: designsByVersion[v.Id]))
            .Where(x => x.Design is not null)
            .GroupBy(x => x.StudyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProtocolDesign>)g.Select(x => x.Design!).ToList());

        var allMemberIds = instances.SelectMany(c => c.Members.Select(m => m.ObservationId)).Distinct().ToList();
        var observations = allMemberIds.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : (await db.Observations.AsNoTracking()
                    .Where(o => allMemberIds.Contains(o.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(o => o.Id);

        var conceptIds = rawStudyVersions
            .SelectMany(v => designsByVersion[v.Id]?.CollectionSpecs ?? [])
            .SelectMany(s => s.Items)
            .Select(i => i.Concept.ConceptId)
            .Concat(observations.Values.Select(o => o.ConceptId))
            .Distinct()
            .ToList();
        var concepts = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        var scopes = instances.Select(i => NodeRef.For(i.StudyId))
            .Concat(instances.Select(i => NodeRef.For(i.StudySiteId)))
            .Concat(instances.Select(i => NodeRef.For(i.EnrollmentId)))
            .Concat(instances.Select(i => NodeRef.For(i.Id)))
            .Concat(allMemberIds.Select(o => NodeRef.For(o)))
            .Distinct()
            .ToList();
        var scoped = await ScopedAssertions.ReadAsync(db, scopes, cancellationToken).ConfigureAwait(false);
        var allLocks = scoped.Locks();
        var allMemberGuids = allMemberIds.Select(o => o.Value).ToHashSet();
        var allMemberAssertions = scoped.About(a => a.TargetKind == NodeKinds.Observation && allMemberGuids.Contains(a.TargetId));

        var instanceGuids = instances.Select(i => i.Id.Value).ToList();
        var rawSignatures = await db.Attestations.AsNoTracking()
            .Where(a => a.TargetKind == NodeKinds.CollectionInstance && instanceGuids.Contains(a.TargetId))
            .OrderBy(a => a.RecordedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.TargetId, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var signaturesByInstance = rawSignatures.ToLookup(a => a.TargetId);
        var errorTargetsByStudy = new Dictionary<StudyId, IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)>>();
        foreach (var studyId in instances.Select(i => i.StudyId).Distinct())
        {
            errorTargetsByStudy[studyId] = await KernelValidationStore.LoadActiveErrorFindingTargetsAsync(db, studyId, cancellationToken)
                .ConfigureAwait(false);
        }

        var allItems = instances
            .SelectMany(instance =>
                (specsByVersion.GetValueOrDefault(instance.ProtocolVersionId) ?? [])
                    .FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal))
                    ?.Items ?? [])
            .ToList();
        var codelistGroups = await LoadCodelistGroupsAsync(db, allItems, cancellationToken).ConfigureAwait(false);

        var enrollmentIds = instances.Select(i => i.EnrollmentId).Distinct().ToList();
        var enrollmentVersions = await db.Enrollments.AsNoTracking()
            .Where(e => enrollmentIds.Contains(e.Id))
            .Select(e => new { e.Id, e.VersionIndex })
            .ToDictionaryAsync(e => e.Id, e => e.VersionIndex, cancellationToken)
            .ConfigureAwait(false);

        var records = new List<CollectionInstanceRecord>(instances.Count);
        foreach (var instance in instances)
        {
            var specs = specsByVersion.GetValueOrDefault(instance.ProtocolVersionId) ?? [];
            var spec = specs.FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal));
            var items = spec?.Items ?? [];
            var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;
            var itemCodelists = MapItemCodelists(items, codelistGroups);

            var values = instance.Members
                .Where(m => observations.ContainsKey(m.ObservationId))
                .OrderBy(m => m.ItemId, StringComparer.Ordinal)
                .Select(m => (m.ItemId, Observation: observations[m.ObservationId]))
                .Select(x => (x.ItemId, x.Observation, Current: x.Observation.Revisions.Single(r => r.Number == x.Observation.Revision)))
                .Select(x => new CollectedValueView(
                    x.ItemId, x.Observation.Id, x.Observation.Concept,
                    x.Observation.Value, x.Observation.ObservedAt, x.Observation.Revision, x.Current.RecordedBy, x.Current.RecordedAt))
                .ToList();

            var memberGuids = instance.Members.Select(m => m.ObservationId.Value).ToHashSet();
            var coveringLocks = allLocks
                .Where(l =>
                    (l.Scope.Kind == NodeKinds.Study && l.Scope.Id == instance.StudyId.Value)
                    || (l.Scope.Kind == NodeKinds.StudySite && l.Scope.Id == instance.StudySiteId.Value)
                    || (l.Scope.Kind == NodeKinds.Enrollment && l.Scope.Id == instance.EnrollmentId.Value)
                    || (l.Scope.Kind == NodeKinds.CollectionInstance && l.Scope.Id == instance.Id.Value)
                    || (l.Scope.Kind == NodeKinds.Observation && memberGuids.Contains(l.Scope.Id)))
                .ToList();
            var assertions = FilterFormAssertions(allMemberAssertions, memberGuids);

            string? contentHash = values.Count == 0
                ? null
                : CollectionInstance.ContentHashOf(values.Select(v => new CollectionContentEntry(v.ObservationId, v.Revision, v.Value)));
            var attestations = signaturesByInstance[instance.Id.Value]
                .Select(a => new FormAttestationView(
                    a.Id, a.Meaning, a.ActorId, a.RecordedAt, a.TargetContentHash, a.Reason,
                    !string.Equals(a.TargetContentHash, contentHash, StringComparison.Ordinal),
                    SignedValues(instance, observations, a.RecordedAt, a.TargetContentHash)))
                .ToList();

            var studyDesigns = designsByStudy.GetValueOrDefault(instance.StudyId) ?? [];
            var (blindedItemIds, blindedObservationIds) = ClassifyBlindedMembers(blinding, instance, collectionTitle, items, observations, concepts, studyDesigns, attestations);
            var activeErrorTargets = errorTargetsByStudy[instance.StudyId];
            var (complete, missingRequired, openQueries, answeredQueries, errorFindings, blockers) =
                EvaluateFormReadiness(items, values, assertions, instance.Id.Value, memberGuids, activeErrorTargets);

            records.Add(new CollectionInstanceRecord(
                instance.Id,
                instance.EnrollmentId,
                instance.ParticipantId,
                instance.StudyId,
                instance.StudySiteId,
                instance.ProtocolVersionId,
                instance.CollectionSpecId,
                instance.PlannedActivityId,
                instance.CollectedAt,
                instance.OpenedBy,
                instance.OpenedAt,
                instance.VersionIndex,
                items,
                values,
                coveringLocks,
                assertions,
                attestations,
                contentHash,
                collectionTitle,
                enrollmentVersions.GetValueOrDefault(instance.EnrollmentId, 1L),
                complete,
                missingRequired,
                openQueries,
                answeredQueries,
                errorFindings,
                blockers,
                blindedItemIds,
                blindedObservationIds,
                itemCodelists));
        }

        return records;
    }

    private static async Task<Dictionary<(CodeSystemVersionId Version, string GroupCode), IReadOnlyList<CodelistOptionView>>> LoadCodelistGroupsAsync(
        KernelDbContext db,
        IEnumerable<CollectionItem> items,
        CancellationToken cancellationToken)
    {
        var keys = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Terminology?.Codelist))
            .Select(i => (i.Terminology!.CodeSystemVersionId, GroupCode: i.Terminology.Codelist!))
            .Distinct()
            .ToList();
        if (keys.Count == 0)
        {
            return [];
        }

        var versionIds = keys.Select(k => k.CodeSystemVersionId).Distinct().ToList();
        var groupCodes = keys.Select(k => k.GroupCode).Distinct(StringComparer.Ordinal).ToList();
        var rawEntries = await db.CodeSystemEntries.AsNoTracking()
            .Where(e => versionIds.Contains(e.CodeSystemVersionId) && e.GroupCode != null && groupCodes.Contains(e.GroupCode))
            .OrderBy(e => e.Code)
            .ThenBy(e => e.Ordinal)
            .Select(e => new { e.CodeSystemVersionId, e.GroupCode, e.Code, e.Display, e.SubmissionValue })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rawEntries
            .GroupBy(e => (e.CodeSystemVersionId, GroupCode: e.GroupCode!))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CodelistOptionView>)g
                    .DistinctBy(e => e.Code, StringComparer.Ordinal)
                    .Select(e => new CodelistOptionView(e.Code, e.Display, e.SubmissionValue))
                    .ToList());
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<CodelistOptionView>>? MapItemCodelists(
        IReadOnlyList<CollectionItem> items,
        IReadOnlyDictionary<(CodeSystemVersionId Version, string GroupCode), IReadOnlyList<CodelistOptionView>> groups)
    {
        Dictionary<string, IReadOnlyList<CodelistOptionView>>? mapped = null;
        foreach (var item in items)
        {
            if (item.Terminology is not { } terminology || string.IsNullOrWhiteSpace(terminology.Codelist))
            {
                continue;
            }

            mapped ??= new Dictionary<string, IReadOnlyList<CodelistOptionView>>(StringComparer.Ordinal);
            mapped[item.Id] = groups.GetValueOrDefault((terminology.CodeSystemVersionId, terminology.Codelist)) ?? [];
        }

        return mapped;
    }

    private static CollectionInstanceSummary ToSummary(
        CollectionInstance instance,
        IReadOnlyList<CollectionSpec> specs,
        IReadOnlyDictionary<ObservationId, ObservationSummaryRow> observations,
        IReadOnlyList<LockView> allLocks,
        IReadOnlyList<AssertionView> allMemberAssertions,
        IReadOnlySet<(Guid ObservationId, int Revision)> verifiedRevisions,
        IEnumerable<string> signatureHashes,
        IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)> activeErrorTargets)
    {
        var spec = specs.FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal));
        var specItems = spec?.Items ?? [];
        var collectionTitle = string.IsNullOrWhiteSpace(spec?.Name) ? null : spec.Name;

        var memberObservations = instance.Members
            .Where(m => observations.ContainsKey(m.ObservationId))
            .OrderBy(m => m.ItemId, StringComparer.Ordinal)
            .Select(m => (m.ItemId, Observation: observations[m.ObservationId]))
            .ToList();
        var recordedMembers = memberObservations
            .Where(m => m.Observation.Value is not MissingValue { Reason: NullFlavor.NotAsked })
            .ToList();
        var recordedItems = recordedMembers.Select(m => m.ItemId).ToHashSet(StringComparer.Ordinal);

        var itemCount = specItems.Select(i => i.Id)
            .Concat(instance.Members.Select(m => m.ItemId))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var missingRequired = specItems
            .Where(i => i.Required && !recordedItems.Contains(i.Id))
            .Select(i => i.Id)
            .ToList();
        var missingRequiredCount = missingRequired.Count;
        var recordedCount = recordedMembers.Count;

        var verifiedCount = recordedMembers.Count(m => verifiedRevisions.Contains((m.Observation.Id.Value, m.Observation.Revision)));
        var unverifiedCount = recordedCount - verifiedCount;

        var memberGuids = instance.Members.Select(m => m.ObservationId.Value).ToHashSet();
        var assertions = FilterFormAssertions(allMemberAssertions, memberGuids);
        var (activeQueryAssertionIds, answeredQueryAssertionIds) = ExtractActiveQueryAssertionIds(assertions);
        var openQueryCount = activeQueryAssertionIds.Count - answeredQueryAssertionIds.Count;
        var answeredQueryCount = answeredQueryAssertionIds.Count;
        var openErrorFindingCount = CountFormErrorFindings(instance.Id.Value, memberGuids, activeErrorTargets);
        var complete = (itemCount == 0 || recordedCount > 0)
            && CollectionReadiness.IsComplete(missingRequiredCount, openQueryCount, answeredQueryCount, openErrorFindingCount);
        var blockers = CollectionReadiness.BuildBlockers(
            missingRequired,
            missingRequiredCount,
            openQueryCount,
            answeredQueryCount,
            openErrorFindingCount);

        var coveringLocks = allLocks
            .Where(l =>
                (l.Scope.Kind == NodeKinds.Study && l.Scope.Id == instance.StudyId.Value)
                || (l.Scope.Kind == NodeKinds.StudySite && l.Scope.Id == instance.StudySiteId.Value)
                || (l.Scope.Kind == NodeKinds.Enrollment && l.Scope.Id == instance.EnrollmentId.Value)
                || (l.Scope.Kind == NodeKinds.CollectionInstance && l.Scope.Id == instance.Id.Value)
                || (l.Scope.Kind == NodeKinds.Observation && memberGuids.Contains(l.Scope.Id)))
            .ToList();
        var locked = coveringLocks.Any(l => l.Kind == LockKind.Locked);
        var frozen = coveringLocks.Any(l => l.Kind == LockKind.Frozen);

        string? contentHash = memberObservations.Count == 0
            ? null
            : CollectionInstance.ContentHashOf(
                memberObservations.Select(m => new CollectionContentEntry(m.Observation.Id, m.Observation.Revision, m.Observation.Value)));
        var hashes = signatureHashes.ToList();
        var signed = contentHash is not null && hashes.Any(h => string.Equals(h, contentHash, StringComparison.Ordinal));
        var signatureSuperseded = !signed && hashes.Count > 0;

        return new CollectionInstanceSummary(
            instance.Id,
            instance.EnrollmentId,
            instance.ParticipantId,
            instance.StudyId,
            instance.StudySiteId,
            instance.ProtocolVersionId,
            instance.CollectionSpecId,
            instance.PlannedActivityId,
            instance.CollectedAt,
            instance.OpenedBy,
            instance.OpenedAt,
            instance.VersionIndex,
            complete,
            itemCount,
            recordedCount,
            collectionTitle,
            missingRequiredCount,
            verifiedCount,
            unverifiedCount,
            locked,
            frozen,
            coveringLocks.Count,
            signed,
            signatureSuperseded,
            hashes.Count,
            openQueryCount,
            answeredQueryCount,
            openErrorFindingCount,
            blockers,
            activeQueryAssertionIds,
            answeredQueryAssertionIds);
    }

    internal static List<AssertionView> FilterFormAssertions(
        IReadOnlyList<AssertionView> allAssertions,
        IReadOnlySet<Guid> memberObservationGuids)
    {
        var formQueryGuids = allAssertions
            .Where(a => a.Kind == AssertionKind.Queried && a.Target.Kind == NodeKinds.Observation && memberObservationGuids.Contains(a.Target.Id))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();
        return [.. allAssertions
            .Where(a => (a.Target.Kind == NodeKinds.Observation && memberObservationGuids.Contains(a.Target.Id))
                || (a.Target.Kind == NodeKinds.Assertion && formQueryGuids.Contains(a.Target.Id)))];
    }

    internal static (IReadOnlySet<AssertionId> ActiveIds, IReadOnlySet<AssertionId> AnsweredIds) ExtractActiveQueryAssertionIds(
        IReadOnlyList<AssertionView> assertions)
    {
        var activeIds = assertions
            .Where(a => a.Kind == AssertionKind.Queried && a.RetractedAt is null)
            .Select(a => a.AssertionId)
            .ToFrozenSet();
        if (activeIds.Count == 0)
        {
            return (FrozenSet<AssertionId>.Empty, FrozenSet<AssertionId>.Empty);
        }

        var answeredIds = assertions
            .Where(a => a.Kind == AssertionKind.QueryAnswered
                && a.RetractedAt is null
                && a.Target.Kind == NodeKinds.Assertion
                && activeIds.Contains(AssertionId.FromGuid(a.Target.Id)))
            .Select(a => AssertionId.FromGuid(a.Target.Id))
            .ToFrozenSet();
        return (activeIds, answeredIds);
    }

    internal static (int OpenCount, int AnsweredCount) CountActiveQueries(
        IReadOnlyList<AssertionView> assertions,
        IReadOnlySet<Guid>? excludeQueryIds = null)
    {
        var activeQueryIds = assertions
            .Where(a => a.Kind == AssertionKind.Queried
                && a.RetractedAt is null
                && (excludeQueryIds is null || !excludeQueryIds.Contains(a.AssertionId.Value)))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();
        if (activeQueryIds.Count == 0)
        {
            return (0, 0);
        }

        var answeredQueryIds = assertions
            .Where(a => a.Kind == AssertionKind.QueryAnswered
                && a.RetractedAt is null
                && a.Target.Kind == NodeKinds.Assertion
                && activeQueryIds.Contains(a.Target.Id))
            .Select(a => a.Target.Id)
            .ToHashSet();
        var answeredCount = answeredQueryIds.Count;
        return (activeQueryIds.Count - answeredCount, answeredCount);
    }

    internal static int CountFormErrorFindings(
        Guid instanceGuid,
        IReadOnlySet<Guid> memberObservationGuids,
        IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)> activeErrorTargets) =>
        activeErrorTargets.Count(entry =>
            (entry.Target.Kind == FindingTargetKind.DatasetRecord
                && Guid.TryParse(entry.Target.Key, out var recordGuid)
                && recordGuid == instanceGuid)
            || (entry.Target.Kind == FindingTargetKind.Observation
                && entry.Target.Node is { Kind: NodeKinds.Observation } obsNode
                && memberObservationGuids.Contains(obsNode.Id)));

    private static (
        bool Complete,
        IReadOnlyList<string> MissingRequired,
        int OpenQueries,
        int AnsweredQueries,
        int ErrorFindings,
        IReadOnlyList<string> Blockers) EvaluateFormReadiness(
        IReadOnlyList<CollectionItem> items,
        IReadOnlyList<CollectedValueView> values,
        IReadOnlyList<AssertionView> assertions,
        Guid instanceGuid,
        IReadOnlySet<Guid> memberGuids,
        IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)> activeErrorTargets)
    {
        var recordedItems = values
            .Where(v => v.Value is not MissingValue { Reason: NullFlavor.NotAsked })
            .Select(v => v.ItemId)
            .ToHashSet(StringComparer.Ordinal);
        var missingRequired = items
            .Where(i => i.Required && !recordedItems.Contains(i.Id))
            .Select(i => i.Id)
            .ToList();
        var (openQueries, answeredQueries) = CountActiveQueries(assertions);
        var errorFindings = CountFormErrorFindings(instanceGuid, memberGuids, activeErrorTargets);
        var complete = (items.Count == 0 || recordedItems.Count > 0)
            && CollectionReadiness.IsComplete(missingRequired.Count, openQueries, answeredQueries, errorFindings);
        var blockers = CollectionReadiness.BuildBlockers(missingRequired, missingRequired.Count, openQueries, answeredQueries, errorFindings);
        return (complete, missingRequired, openQueries, answeredQueries, errorFindings, blockers);
    }

    /// <summary>
    /// The revisions a signature recorded at <paramref name="signedAt"/> signed: each member that had joined by then, at
    /// its latest revision recorded by then. Returned only when they hash to <paramref name="signedHash"/>, so what the
    /// form shows as signed is exactly what was signed.
    /// </summary>
    private static List<SignedValueView>? SignedValues(
        CollectionInstance instance, IReadOnlyDictionary<ObservationId, Observation> observations, DateTimeOffset signedAt, string signedHash)
    {
        var signed = instance.Members
            .Where(m => m.JoinedAt <= signedAt)
            .OrderBy(m => m.ItemId, StringComparer.Ordinal)
            .Select(m => (m.ItemId, Revision: observations[m.ObservationId].Revisions.Where(r => r.RecordedAt <= signedAt).MaxBy(r => r.Number)))
            .Where(x => x.Revision is not null)
            .Select(x => new SignedValueView(x.ItemId, x.Revision!.ObservationId, x.Revision.Number, x.Revision.Value))
            .ToList();
        var hash = CollectionInstance.ContentHashOf(signed.Select(v => new CollectionContentEntry(v.ObservationId, v.Revision, v.Value)));
        return string.Equals(hash, signedHash, StringComparison.Ordinal) ? signed : null;
    }

    private static (IReadOnlySet<string> BlindedItems, IReadOnlySet<ObservationId> BlindedObservations) ClassifyBlindedMembers(
        IBlindedTreatmentClassifier blinding,
        CollectionInstance instance,
        string? collectionTitle,
        IReadOnlyList<CollectionItem> items,
        IReadOnlyDictionary<ObservationId, Observation> observations,
        IReadOnlyDictionary<ConceptId, Concept> concepts,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyList<FormAttestationView> attestations)
    {
        BlindedConceptMetadata? ResolveConcept(ConceptId id) =>
            concepts.TryGetValue(id, out var concept) ? BlindedObservationClassifier.ToMetadata(concept) : null;

        var specById = items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var (specBlinded, blindedItems) = ClassifySpecBlindedItems(
            blinding,
            instance.CollectionSpecId,
            collectionTitle,
            items,
            ResolveConcept,
            designs);

        var blindedObservations = new HashSet<ObservationId>();
        foreach (var member in instance.Members)
        {
            if (!observations.TryGetValue(member.ObservationId, out var obs))
            {
                continue;
            }

            specById.TryGetValue(member.ItemId, out var specItem);
            var conceptMeta = ResolveConcept(obs.ConceptId);
            var currentOrHistoricalBlinded = blinding.IsBlindedCollectionItem(
                    instance.CollectionSpecId,
                    collectionTitle,
                    specItem,
                    obs.Value,
                    conceptMeta,
                    designs)
                || blinding.IsBlindedObservation(
                    obs.ConceptId,
                    obs.Value,
                    conceptMeta,
                    instance.CollectionSpecId,
                    member.ItemId,
                    designs)
                || obs.Revisions.Any(r => blinding.IsBlindedObservation(
                    obs.ConceptId,
                    r.Value,
                    conceptMeta,
                    instance.CollectionSpecId,
                    member.ItemId,
                    designs));
            if (specBlinded || blindedItems.Contains(member.ItemId) || currentOrHistoricalBlinded)
            {
                blindedItems.Add(member.ItemId);
                blindedObservations.Add(obs.Id);
            }
        }

        foreach (var signedValue in attestations
            .Where(signature => signature.SignedValues is not null)
            .SelectMany(signature => signature.SignedValues!))
        {
            specById.TryGetValue(signedValue.ItemId, out var specItem);
            var conceptMeta = specItem is not null ? ResolveConcept(specItem.Concept.ConceptId) : null;
            if (specBlinded
                || blindedItems.Contains(signedValue.ItemId)
                || blinding.IsBlindedCollectionItem(
                    instance.CollectionSpecId,
                    collectionTitle,
                    specItem,
                    signedValue.Value,
                    conceptMeta,
                    designs))
            {
                blindedItems.Add(signedValue.ItemId);
                blindedObservations.Add(signedValue.ObservationId);
            }
        }

        return (
            blindedItems.Count == 0 ? FrozenSet<string>.Empty : blindedItems.ToFrozenSet(StringComparer.Ordinal),
            blindedObservations.Count == 0 ? FrozenSet<ObservationId>.Empty : blindedObservations.ToFrozenSet());
    }

    private static (IReadOnlySet<string> BlindedItems, HashSet<ObservationId> BlindedObservations) ClassifyQueryTargetBlinding(
        IBlindedTreatmentClassifier blinding,
        QueryTargetFormRow form,
        string? collectionTitle,
        IReadOnlyList<CollectionItem> items,
        IReadOnlyDictionary<ObservationId, QueryTargetObservationRow> observations,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> concepts,
        IReadOnlyList<ProtocolDesign> designs)
    {
        BlindedConceptMetadata? ResolveConcept(ConceptId id) =>
            concepts.TryGetValue(id, out var foundMeta) ? foundMeta : null;

        var (specBlinded, blindedItems) = ClassifySpecBlindedItems(
            blinding,
            form.CollectionSpecId,
            collectionTitle,
            items,
            ResolveConcept,
            designs);

        var blindedObservations = new HashSet<ObservationId>();
        foreach (var member in form.Members)
        {
            if (!observations.TryGetValue(member.ObservationId, out var obs))
            {
                continue;
            }

            if (specBlinded
                || blindedItems.Contains(member.ItemId)
                || blinding.IsBlindedObservation(
                    obs.ConceptId,
                    obs.Value,
                    ResolveConcept(obs.ConceptId),
                    form.CollectionSpecId,
                    member.ItemId,
                    designs))
            {
                blindedItems.Add(member.ItemId);
                blindedObservations.Add(obs.Id);
            }
        }

        return (
            blindedItems.Count == 0 ? FrozenSet<string>.Empty : blindedItems.ToFrozenSet(StringComparer.Ordinal),
            blindedObservations);
    }

    private static (bool SpecBlinded, HashSet<string> BlindedItems) ClassifySpecBlindedItems(
        IBlindedTreatmentClassifier blinding,
        string collectionSpecId,
        string? collectionTitle,
        IReadOnlyList<CollectionItem> items,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        IReadOnlyList<ProtocolDesign> designs)
    {
        var specBlinded = blinding.IsBlindedCollectionSpec(collectionSpecId, collectionTitle, designs: designs);
        var blindedItems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (specBlinded
                || blinding.IsBlindedCollectionItem(
                    collectionSpecId,
                    collectionTitle,
                    item,
                    value: null,
                    resolveConcept(item.Concept.ConceptId),
                    designs))
            {
                blindedItems.Add(item.Id);
            }
        }

        return (specBlinded, blindedItems);
    }

    private async Task<IReadOnlyDictionary<ObservationId, CollectionObservationProjection>> LoadObservationProjectionsFallbackAsync(
        KernelDbContext db,
        IReadOnlyCollection<ObservationId> distinctIds,
        CancellationToken cancellationToken)
    {
        var obsRows = await (
                from o in db.Observations.AsNoTracking()
                join cim in db.Set<CollectionInstanceMember>().AsNoTracking()
                    on new { o.TenantId, ObservationId = o.Id } equals new { cim.TenantId, cim.ObservationId }
                    into cimGroup
                from cim in cimGroup.DefaultIfEmpty()
                join ci in db.CollectionInstances.AsNoTracking()
                    on new { TenantId = cim != null ? cim.TenantId : default, Id = cim != null ? cim.CollectionInstanceId : default }
                    equals new { ci.TenantId, ci.Id }
                    into ciGroup
                from ci in ciGroup.DefaultIfEmpty()
                join c in db.Concepts.AsNoTracking()
                    on new { o.TenantId, Id = o.ConceptId } equals new { c.TenantId, c.Id }
                    into cGroup
                from c in cGroup.DefaultIfEmpty()
                where distinctIds.Contains(o.Id)
                select new
                {
                    o.Id,
                    o.Revision,
                    o.StudyId,
                    o.ConceptId,
                    o.Value,
                    CollectionSpecId = ci != null ? ci.CollectionSpecId : null,
                    ItemId = cim != null ? cim.ItemId : null,
                    ConceptMeta = c != null ? (BlindedConceptMetadata?)new BlindedConceptMetadata(c.ExternalCode, c.PreferredLabel, c.Definition) : null,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (obsRows.Count == 0)
        {
            return new Dictionary<ObservationId, CollectionObservationProjection>();
        }

        var studyIds = obsRows.Select(r => r.StudyId).Distinct().ToList();
        var rawVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => studyIds.Contains(v.StudyId))
            .Select(v => new { v.StudyId, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var designsByStudy = rawVersions
            .Select(v => (v.StudyId, Design: ProtocolDesignDocument.Read(v.Content).Design))
            .Where(x => x.Design is not null)
            .GroupBy(x => x.StudyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProtocolDesign>)g.Select(x => x.Design!).ToList());

        var result = new Dictionary<ObservationId, CollectionObservationProjection>(obsRows.Count);
        foreach (var row in obsRows)
        {
            var studyDesigns = designsByStudy.GetValueOrDefault(row.StudyId) ?? [];
            var isBlinded = _blinding.IsBlindedObservation(
                row.ConceptId,
                row.Value,
                row.ConceptMeta,
                row.CollectionSpecId,
                row.ItemId,
                studyDesigns);
            result[row.Id] = new CollectionObservationProjection(row.Revision, isBlinded);
        }

        return result;
    }

    private sealed record MemberObservationRow(string ItemId, ObservationId ObservationId);

    private sealed record ObservationSummaryRow(ObservationId Id, ObservationValue Value, int Revision);

    private sealed record ObservationProjectionRow(Guid Id, int Revision, bool IsBlinded);

    private sealed record QueryTargetObservationRow(ObservationId Id, ConceptId ConceptId, ObservationValue Value, int Revision);

    private sealed record QueryTargetFormRow(
        CollectionInstanceId Id,
        EnrollmentId EnrollmentId,
        StudyId StudyId,
        StudySiteId StudySiteId,
        ProtocolVersionId ProtocolVersionId,
        string CollectionSpecId,
        string? PlannedActivityId,
        IReadOnlyList<MemberObservationRow> Members);

    private sealed record QueryTargetRow(
        CollectionInstanceId Id,
        StudyId StudyId,
        StudySiteId StudySiteId,
        EnrollmentId EnrollmentId,
        string CollectionSpecId,
        string? PlannedActivityId,
        long VersionIndex,
        long EnrollmentVersion,
        string? ItemId,
        ObservationId? ObservationId,
        int? Revision,
        ObservationValue? Value);
}
