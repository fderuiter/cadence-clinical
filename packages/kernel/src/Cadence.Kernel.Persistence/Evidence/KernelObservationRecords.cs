using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Reads an observation, its revisions, the locks that cover it, the assertions about it and its kernel audit entries
/// through a context bound to the caller's tenant (ADR-2250). Read-only: no tracking.
/// </summary>
internal sealed class KernelObservationRecords(
    KernelTenantDbContextFactory contexts,
    IAuthorizationProbe? authorizationProbe = null,
    IBlindedTreatmentClassifier? blindingClassifier = null) : IObservationRecords
{
    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    private static readonly string UnverifiedSdvTargetSql = $"""
        WITH unverified AS MATERIALIZED (
            SELECT
                o.id,
                o.tenant_id,
                o.study_id,
                o.enrollment_id,
                o.concept_id,
                o.value,
                o.observed_at,
                o.revision
            FROM kernel.observations AS o
            WHERE o.tenant_id = @tenant
              AND o.id = ANY(@ids)
              AND NOT (o.value ->> 'type' = 'missing' AND o.value ->> 'reason' = 'NotAsked')
              AND NOT EXISTS (
                  SELECT 1
                  FROM kernel.assertions AS va
                  WHERE va.tenant_id = o.tenant_id
                    AND va.target_kind = 'observation'
                    AND va.target_id = o.id
                    AND va.kind = 'Verified'
                    AND va.target_revision = o.revision
                    AND NOT EXISTS (
                        SELECT 1
                        FROM kernel.assertions AS ra
                        WHERE ra.tenant_id = va.tenant_id
                          AND ra.retracts_id = va.id
                    )
              )
        )
        SELECT DISTINCT ON (o.id)
            o.id AS "Id",
            o.study_id AS "StudyId",
            o.enrollment_id AS "EnrollmentId",
            cim.collection_instance_id AS "CollectionInstanceId",
            cim.item_id AS "ItemId",
            o.revision AS "CurrentRevision",
            COALESCE(lr.recorded_by, '') AS "LatestRecordedBy",
            COALESCE(lr.recorded_at, o.observed_at) AS "LatestRecordedAt",
            COALESCE(lr.reason_for_change, '') AS "LatestReasonForChange",
            {BlindedObservationSql.Predicate(includeMaskedRevisions: true)} AS "IsBlinded"
        FROM unverified AS o
        LEFT JOIN kernel.observation_revisions AS lr
            ON lr.tenant_id = o.tenant_id AND lr.observation_id = o.id AND lr.number = o.revision
        LEFT JOIN kernel.collection_instance_members AS cim
            ON cim.tenant_id = o.tenant_id AND cim.observation_id = o.id
        LEFT JOIN kernel.collection_instances AS ci
            ON ci.tenant_id = cim.tenant_id AND ci.id = cim.collection_instance_id
        LEFT JOIN kernel.concepts AS c
            ON c.tenant_id = o.tenant_id AND c.id = o.concept_id
        ORDER BY o.id, 10 DESC, cim.collection_instance_id NULLS LAST
        """;

    private readonly IBlindedTreatmentClassifier _blinding = blindingClassifier ?? BlindedTreatmentClassifier.Instance;

    public async Task<ObservationRecord?> FindForReaderAsync(
        TenantId tenant,
        ObservationId observation,
        bool unblinded,
        CancellationToken cancellationToken)
    {
        var record = await ReadCoreAsync(
            tenant,
            observation,
            includeFullBlindedClassification: !unblinded,
            cancellationToken).ConfigureAwait(false);
        if (record is null || unblinded)
        {
            return record;
        }

        return record.IsBlindedTreatment ? record.MaskBlinded() : record;
    }

    public async Task<ObservationRecord?> FindForReaderAsync(
        TenantId tenant,
        ObservationId observation,
        Func<EnrollmentId, CancellationToken, ValueTask<bool>> canUnblindAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canUnblindAsync);
        var record = await ReadCoreAsync(
            tenant,
            observation,
            includeFullBlindedClassification: true,
            cancellationToken).ConfigureAwait(false);
        if (record is null || !record.IsBlindedTreatment)
        {
            return record;
        }

        var unblinded = await canUnblindAsync(record.EnrollmentId, cancellationToken).ConfigureAwait(false);
        return unblinded ? record : record.MaskBlinded();
    }

    public async Task<ObservationRecord?> FindForReaderAsync(
        TenantId tenant,
        ObservationId observation,
        CancellationToken cancellationToken)
    {
        var record = await ReadCoreAsync(
            tenant,
            observation,
            includeFullBlindedClassification: true,
            cancellationToken).ConfigureAwait(false);
        if (record is null || !record.IsBlindedTreatment)
        {
            return record;
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

    public Task<ObservationRecord?> FindAsync(TenantId tenant, ObservationId observation, CancellationToken cancellationToken) =>
        ReadCoreAsync(tenant, observation, includeFullBlindedClassification: false, cancellationToken);

    public async Task<IReadOnlyDictionary<ObservationId, ObservationSdvAlertTarget>> FindUnverifiedSdvTargetsAsync(
        TenantId tenant,
        IReadOnlyCollection<ObservationId> observations,
        Func<EnrollmentId, CancellationToken, ValueTask<bool>> canUnblindAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(canUnblindAsync);

        var distinctIds = observations
            .Where(id => !id.IsEmpty)
            .Distinct()
            .ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<ObservationId, ObservationSdvAlertTarget>();
        }

        List<UnverifiedSdvTargetRow> rows;
        await using (var db = contexts.Create(tenant))
        {
            if (db.Database.IsRelational() && ReferenceEquals(_blinding, BlindedTreatmentClassifier.Instance))
            {
                await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    object[] parameters =
                    [
                        new NpgsqlParameter<Guid>("tenant", tenant.Value),
                        new NpgsqlParameter<Guid[]>("ids", [.. distinctIds.Select(id => id.Value)]),
                    ];
                    rows = await db.Database
                        .SqlQueryRaw<UnverifiedSdvTargetRow>(UnverifiedSdvTargetSql, parameters)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    await db.Database.CloseConnectionAsync().ConfigureAwait(false);
                }
            }
            else
            {
                rows = await LoadUnverifiedSdvTargetsFallbackAsync(db, distinctIds, cancellationToken).ConfigureAwait(false);
            }
        }

        if (rows.Count == 0)
        {
            return new Dictionary<ObservationId, ObservationSdvAlertTarget>();
        }

        var unblindByEnrollment = new Dictionary<EnrollmentId, bool>();
        var result = new Dictionary<ObservationId, ObservationSdvAlertTarget>(rows.Count);
        foreach (var row in rows)
        {
            var enrollmentId = EnrollmentId.FromGuid(row.EnrollmentId);
            if (row.IsBlinded)
            {
                if (!unblindByEnrollment.TryGetValue(enrollmentId, out var unblinded))
                {
                    unblinded = await canUnblindAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
                    unblindByEnrollment[enrollmentId] = unblinded;
                }

                if (!unblinded)
                {
                    continue;
                }
            }

            var observationId = ObservationId.FromGuid(row.Id);
            result[observationId] = new ObservationSdvAlertTarget(
                observationId,
                StudyId.FromGuid(row.StudyId),
                enrollmentId,
                row.CollectionInstanceId is { } formGuid ? CollectionInstanceId.FromGuid(formGuid) : null,
                row.ItemId,
                row.CurrentRevision,
                row.LatestRecordedBy,
                row.LatestRecordedAt,
                row.LatestReasonForChange,
                row.IsBlinded);
        }

        return result;
    }

    private async Task<List<UnverifiedSdvTargetRow>> LoadUnverifiedSdvTargetsFallbackAsync(
        KernelDbContext db,
        IReadOnlyList<ObservationId> observationIds,
        CancellationToken cancellationToken)
    {
        var foundObservations = await db.Observations.AsNoTracking()
            .Where(o => observationIds.Contains(o.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (foundObservations.Count == 0)
        {
            return [];
        }

        var rawObsGuids = foundObservations.Select(o => o.Id.Value).ToList();
        var rawAssertions = await db.Assertions.AsNoTracking()
            .Where(a => a.TargetKind == NodeKinds.Observation && rawObsGuids.Contains(a.TargetId) && a.Kind == AssertionKind.Verified)
            .Select(a => new { a.Id, a.TargetId, a.TargetRevision })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var verifiedIds = rawAssertions.Select(a => a.Id).ToList();
        var retractedIds = verifiedIds.Count == 0
            ? new HashSet<AssertionId>()
            : (await db.Assertions.AsNoTracking()
                .Where(a => a.RetractsId != null && verifiedIds.Contains(a.RetractsId.Value))
                .Select(a => a.RetractsId!.Value)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)).ToHashSet();

        var unverified = foundObservations
            .Where(o => o.Value is not MissingValue { Reason: NullFlavor.NotAsked }
                && !rawAssertions.Any(a =>
                    a.TargetId == o.Id.Value
                    && a.TargetRevision == o.Revision
                    && !retractedIds.Contains(a.Id)))
            .ToList();
        if (unverified.Count == 0)
        {
            return [];
        }

        var unverifiedIds = unverified.Select(o => o.Id).ToList();
        var instances = await db.CollectionInstances.AsNoTracking()
            .Where(ci => ci.Members.Any(m => unverifiedIds.Contains(m.ObservationId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var studyIds = unverified.Select(o => o.StudyId).Distinct().ToList();
        var studyVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(pv => studyIds.Contains(pv.StudyId))
            .Select(pv => new { pv.StudyId, pv.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var designsByStudy = studyVersions
            .GroupBy(pv => pv.StudyId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ProtocolDesign>)g
                    .Select(v => ProtocolDesignDocument.Read(v.Content).Design)
                    .Where(d => d is not null)
                    .Select(d => d!)
                    .ToList());
        var conceptIds = unverified.Select(o => o.ConceptId).Distinct().ToList();
        var concepts = await db.Concepts.AsNoTracking()
            .Where(c => conceptIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<UnverifiedSdvTargetRow>(unverified.Count);
        foreach (var obs in unverified)
        {
            var instance = instances.FirstOrDefault(ci => ci.Members.Any(m => m.ObservationId == obs.Id));
            var itemId = instance?.Members.FirstOrDefault(m => m.ObservationId == obs.Id)?.ItemId;
            var designs = designsByStudy.GetValueOrDefault(obs.StudyId, []);
            var concept = concepts.GetValueOrDefault(obs.ConceptId);
            BlindedConceptMetadata? metadata = concept is not null
                ? new BlindedConceptMetadata(concept.ExternalCode, concept.PreferredLabel, concept.Definition)
                : null;
            var isBlinded = _blinding.IsBlindedObservation(obs.ConceptId, obs.Value, metadata, instance?.CollectionSpecId, itemId, designs)
                || obs.Revisions.Any(r => _blinding.IsBlindedObservation(obs.ConceptId, r.Value, metadata, instance?.CollectionSpecId, itemId, designs));
            var latestRev = obs.Revisions.OrderBy(r => r.Number).LastOrDefault();
            rows.Add(new UnverifiedSdvTargetRow(
                obs.Id.Value,
                obs.StudyId.Value,
                obs.EnrollmentId.Value,
                instance?.Id.Value,
                itemId,
                obs.Revision,
                latestRev?.RecordedBy ?? string.Empty,
                latestRev?.RecordedAt ?? obs.ObservedAt,
                latestRev?.ReasonForChange ?? string.Empty,
                isBlinded));
        }

        return rows;
    }

    private sealed record UnverifiedSdvTargetRow(
        Guid Id,
        Guid StudyId,
        Guid EnrollmentId,
        Guid? CollectionInstanceId,
        string? ItemId,
        int CurrentRevision,
        string LatestRecordedBy,
        DateTimeOffset LatestRecordedAt,
        string LatestReasonForChange,
        bool IsBlinded);

    private async Task<ObservationRecord?> ReadCoreAsync(
        TenantId tenant,
        ObservationId observation,
        bool includeFullBlindedClassification,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var found = await db.Observations.AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == observation, cancellationToken)
                .ConfigureAwait(false);
            if (found is null)
            {
                return null;
            }

            var studySite = await db.Enrollments.AsNoTracking()
                .Where(e => e.Id == found.EnrollmentId)
                .Select(e => e.StudySiteId)
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);
            var foundInstance = await db.CollectionInstances.AsNoTracking()
                .SingleOrDefaultAsync(c => c.Members.Any(m => m.ObservationId == found.Id), cancellationToken)
                .ConfigureAwait(false);
            CollectionInstanceId? instance = foundInstance?.Id;
            string? itemId = null;
            CollectionItem? specItem = null;
            List<ProtocolDesign> designs = [];

            if (includeFullBlindedClassification)
            {
                var studyVersions = await db.ProtocolVersions.AsNoTracking()
                    .Where(v => v.StudyId == found.StudyId)
                    .Select(v => new { v.Id, v.Content })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                designs = studyVersions
                    .Select(v => ProtocolDesignDocument.Read(v.Content).Design)
                    .Where(d => d is not null)
                    .Select(d => d!)
                    .ToList();

                if (foundInstance is not null)
                {
                    itemId = foundInstance.Members.FirstOrDefault(m => m.ObservationId == found.Id)?.ItemId;
                    if (itemId is not null)
                    {
                        var content = studyVersions.SingleOrDefault(v => v.Id == foundInstance.ProtocolVersionId)?.Content;
                        if (content is not null)
                        {
                            specItem = ProtocolDesignDocument.Read(content).Design?.CollectionSpecs
                                .FirstOrDefault(s => string.Equals(s.Id, foundInstance.CollectionSpecId, StringComparison.Ordinal))
                                ?.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
                        }
                    }
                }
            }
            else if (foundInstance is not null)
            {
                itemId = foundInstance.Members.FirstOrDefault(m => m.ObservationId == found.Id)?.ItemId;
                if (itemId is not null)
                {
                    var content = await db.ProtocolVersions.AsNoTracking()
                        .Where(v => v.Id == foundInstance.ProtocolVersionId)
                        .Select(v => v.Content)
                        .SingleOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (content is not null)
                    {
                        var parsedDesign = ProtocolDesignDocument.Read(content).Design;
                        if (parsedDesign is not null)
                        {
                            designs = [parsedDesign];
                            specItem = parsedDesign.CollectionSpecs
                                .FirstOrDefault(s => string.Equals(s.Id, foundInstance.CollectionSpecId, StringComparison.Ordinal))
                                ?.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
                        }
                    }
                }
            }

            Concept? concept = null;
            if (includeFullBlindedClassification)
            {
                concept = await db.Concepts.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == found.ConceptId, cancellationToken)
                    .ConfigureAwait(false);
            }

            var isBlindedTreatment = BlindedObservationClassifier.IsBlindedTreatment(
                found.ConceptId,
                found.Value,
                concept,
                foundInstance?.CollectionSpecId,
                itemId,
                designs)
                || found.Revisions.Any(r => BlindedObservationClassifier.IsBlindedTreatment(
                    found.ConceptId,
                    r.Value,
                    concept,
                    foundInstance?.CollectionSpecId,
                    itemId,
                    designs));

            var scopes = LockScopes.Covering(found.StudyId, studySite, found.EnrollmentId, found.Id, instance);
            var assertions = await ScopedAssertions.ReadAsync(db, scopes, cancellationToken).ConfigureAwait(false);
            var locks = assertions.Locks();
            var about = assertions.About(a => a.TargetId == found.Id.Value);

            // The transaction stamps AggregateId with the key's text, the aggregate's UUID.
            var key = observation.ToString();
            var assertionKeys = assertions
                .CoveringAuditIds(a => a.TargetKind == NodeKinds.Observation && a.TargetId == found.Id.Value)
                .Select(id => id.ToString())
                .ToList();
            var auditQuery = assertionKeys.Count == 0
                ? db.AuditEntries.AsNoTracking()
                    .Where(a => a.TenantId == tenant && a.AggregateType == nameof(Observation) && a.AggregateId == key)
                : db.AuditEntries.AsNoTracking()
                    .Where(a => a.TenantId == tenant
                        && ((a.AggregateType == nameof(Observation) && a.AggregateId == key)
                            || (a.AggregateType == nameof(Assertion) && a.AggregateId != null && assertionKeys.Contains(a.AggregateId))));
            var audit = await auditQuery
                .OrderBy(a => a.Sequence)
                .Select(a => new ObservationAuditView(a.Sequence, a.RecordedAt, a.ActorId, a.Action, a.AggregateType!, a.AggregateVersion, a.ReasonForChange))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            string? expectedCodelist = string.IsNullOrWhiteSpace(specItem?.Terminology?.Codelist)
                ? null
                : specItem.Terminology.Codelist;
            IReadOnlyList<CodelistOptionView>? expectedCodelistOptions = null;
            if (expectedCodelist is not null && specItem?.Terminology is { } terminology)
            {
                var rawEntries = await db.CodeSystemEntries.AsNoTracking()
                    .Where(e => e.CodeSystemVersionId == terminology.CodeSystemVersionId && e.GroupCode == expectedCodelist)
                    .OrderBy(e => e.Code)
                    .ThenBy(e => e.Ordinal)
                    .Select(e => new { e.Code, e.Display, e.SubmissionValue })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                expectedCodelistOptions = rawEntries
                    .DistinctBy(e => e.Code, StringComparer.Ordinal)
                    .Select(e => new CodelistOptionView(e.Code, e.Display, e.SubmissionValue))
                    .ToList();
            }

            var revisions = found.Revisions.OrderBy(r => r.Number).ToList();
            var views = revisions
                .Select((r, i) => new ObservationRevisionView(
                    r.Number, r.Value, r.ReasonForChange, r.RecordedBy, r.RecordedAt, i + 1 < revisions.Count ? revisions[i + 1].RecordedAt : (DateTimeOffset?)null))
                .ToList();
            return new ObservationRecord(
                found.Id,
                found.EnrollmentId,
                found.ParticipantId,
                found.StudyId,
                found.Concept,
                found.Value,
                found.ObservedAt,
                found.VersionIndex,
                locks,
                views,
                about,
                audit,
                instance,
                itemId,
                specItem?.ExpectedKind,
                specItem?.Unit,
                specItem?.Terminology?.CodeSystemVersionId,
                expectedCodelist,
                expectedCodelistOptions,
                isBlindedTreatment);
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
