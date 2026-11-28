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
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

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
        return await CollectionRecordHydrator
            .FindSingleAsync(db, instance, governingDesign, _blinding, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await CollectionAttestationAndSummaryReader
            .ListForEnrollmentAsync(db, enrollment, cancellationToken)
            .ConfigureAwait(false);
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
        return await CollectionAttestationAndSummaryReader
            .FindAttestationAsync(db, instance, attestation, cancellationToken)
            .ConfigureAwait(false);
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
        return await CollectionAttestationAndSummaryReader
            .FindAttestationForReaderAsync(db, instance, attestation, unblinded, governingDesign, _blinding, cancellationToken)
            .ConfigureAwait(false);
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
        return await CollectionQueryTargetReader
            .FindQueryTargetsAsync(db, tenant, distinctIds, unblindedEnrollments, _blinding, cancellationToken)
            .ConfigureAwait(false);
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
        var records = await CollectionRecordHydrator.LoadRecordsAsync(
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
        return await CollectionMemberBlindingClassifier
            .FindObservationProjectionsAsync(db, tenant, distinctIds, _blinding, cancellationToken)
            .ConfigureAwait(false);
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

        await using var db = contexts.Create(tenant);
        return await CollectionQueryTargetReader
            .FindQueryTargetAsync(db, instance, observation, itemId, checkLocks, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await CollectionRecordHydrator.LoadRecordsAsync(
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
        return await CollectionRecordHydrator.LoadRecordsAsync(
            db,
            db.CollectionInstances.AsNoTracking().Where(c => c.StudyId == study),
            cancellationToken,
            _blinding).ConfigureAwait(false);
    }

    internal static Task<IReadOnlyList<CollectionInstanceRecord>> LoadRecordsAsync(
        KernelDbContext db,
        IQueryable<CollectionInstance> query,
        CancellationToken cancellationToken,
        IBlindedTreatmentClassifier? blindingClassifier = null) =>
        CollectionRecordHydrator.LoadRecordsAsync(db, query, cancellationToken, blindingClassifier);

    internal static List<AssertionView> FilterFormAssertions(
        IReadOnlyList<AssertionView> allAssertions,
        IReadOnlySet<Guid> memberObservationGuids) =>
        CollectionReadinessAndTimingHelper.FilterFormAssertions(allAssertions, memberObservationGuids);

    internal static (IReadOnlySet<AssertionId> ActiveIds, IReadOnlySet<AssertionId> AnsweredIds) ExtractActiveQueryAssertionIds(
        IReadOnlyList<AssertionView> assertions) =>
        CollectionReadinessAndTimingHelper.ExtractActiveQueryAssertionIds(assertions);

    internal static (int OpenCount, int AnsweredCount) CountActiveQueries(
        IReadOnlyList<AssertionView> assertions,
        IReadOnlySet<Guid>? excludeQueryIds = null) =>
        CollectionReadinessAndTimingHelper.CountActiveQueries(assertions, excludeQueryIds);

    internal static int CountFormErrorFindings(
        Guid instanceGuid,
        IReadOnlySet<Guid> memberObservationGuids,
        IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)> activeErrorTargets) =>
        CollectionReadinessAndTimingHelper.CountFormErrorFindings(instanceGuid, memberObservationGuids, activeErrorTargets);

    internal static (IReadOnlySet<string> BlindedItems, IReadOnlySet<ObservationId> BlindedObservations) ClassifyBlindedMembers(
        IBlindedTreatmentClassifier blinding,
        CollectionInstance instance,
        string? collectionTitle,
        IReadOnlyList<CollectionItem> items,
        IReadOnlyDictionary<ObservationId, Observation> observations,
        IReadOnlyDictionary<ConceptId, Concept> concepts,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyList<FormAttestationView> attestations) =>
        CollectionMemberBlindingClassifier.ClassifyBlindedMembers(
            blinding,
            instance,
            collectionTitle,
            items,
            observations,
            concepts,
            designs,
            attestations);
}
