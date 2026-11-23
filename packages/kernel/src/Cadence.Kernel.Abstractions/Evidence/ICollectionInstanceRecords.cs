using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Evidence;

/// <summary>
/// Reads a form instance in one call (spec §8.1, ADR-2622): its values with their revisions, the assertions about them,
/// the signatures over the form and the locks in force. Reads are tenant-scoped by Row-Level Security and never change
/// state. Kernel persistence implements it (<c>AddKernelPersistence</c>).
/// </summary>
public interface ICollectionInstanceRecords
{
    /// <summary>The form instance, or null when it does not exist in <paramref name="tenant"/>.</summary>
    Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken);

    /// <summary>
    /// Reads <paramref name="instances"/> in one batch, keyed by <see cref="CollectionInstanceId"/>; missing forms are omitted.
    /// </summary>
    async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>> FindManyAsync(
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instances,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var found = new Dictionary<CollectionInstanceId, CollectionInstanceRecord>();
        foreach (var instance in instances.Where(id => !id.IsEmpty).Distinct())
        {
            if (await FindAsync(tenant, instance, cancellationToken).ConfigureAwait(false) is { } record)
            {
                found[instance] = record;
            }
        }

        return found;
    }

    /// <summary>
    /// Reads the current revision numbers for <paramref name="observations"/> in one batch; missing observations are omitted.
    /// </summary>
    async Task<IReadOnlyDictionary<ObservationId, int>> FindObservationRevisionsAsync(
        TenantId tenant,
        IReadOnlyCollection<ObservationId> observations,
        IReadOnlyCollection<CollectionInstanceId>? instances = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0 || instances is null || instances.Count == 0)
        {
            return new Dictionary<ObservationId, int>();
        }

        var wanted = observations.Where(id => !id.IsEmpty).ToHashSet();
        var records = await FindManyAsync(tenant, instances, cancellationToken).ConfigureAwait(false);
        var revisions = new Dictionary<ObservationId, int>();
        foreach (var value in records.Values.SelectMany(r => r.Values).Where(v => wanted.Contains(v.ObservationId)))
        {
            revisions[value.ObservationId] = value.Revision;
        }

        return revisions;
    }

    /// <summary>Every form instance opened for <paramref name="enrollment"/> in <paramref name="tenant"/>, oldest first.</summary>
    Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken);

    /// <summary>Every full form instance record opened for <paramref name="enrollment"/> in <paramref name="tenant"/>, oldest first.</summary>
    Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([]);

    /// <summary>Every full form instance record opened for <paramref name="study"/> in <paramref name="tenant"/>, oldest first.</summary>
    Task<IReadOnlyList<CollectionInstanceRecord>> ListForStudyAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([]);
}

/// <summary>Summary of an opened form instance for an enrollment's form list.</summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="EnrollmentId">Its participant enrollment.</param>
/// <param name="ParticipantId">The enrollment's participant.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs the form (K-TIME-8).</param>
/// <param name="CollectionSpecId">The CollectionSpec it collects.</param>
/// <param name="PlannedActivityId">The planned activity (visit) it belongs to, when scheduled.</param>
/// <param name="CollectedAt">When its values were collected in the trial world.</param>
/// <param name="OpenedBy">Who opened the form.</param>
/// <param name="OpenedAt">When it was opened.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
/// <param name="Complete">Whether every required item in the governing CollectionSpec has a value.</param>
/// <param name="ItemCount">Total items on the form.</param>
/// <param name="RecordedCount">Items on the form that currently hold a recorded observation.</param>
/// <param name="CollectionTitle">The CollectionSpec's title in the governing protocol version, when present.</param>
/// <param name="MissingRequiredCount">Required items on the form that still lack a recorded observation.</param>
/// <param name="VerifiedCount">Recorded items whose current revision carries an active source verification assertion.</param>
/// <param name="UnverifiedCount">Recorded items whose current revision is not source-verified.</param>
/// <param name="Locked">Whether an active hard lock covers the form or any of its covering scopes.</param>
/// <param name="Frozen">Whether an active data freeze covers the form or any of its covering scopes.</param>
/// <param name="LockCount">Active locks and freezes covering the form or its values.</param>
/// <param name="Signed">Whether at least one signature covers the form's current content hash.</param>
/// <param name="SignatureSuperseded">Whether the form has signatures, all superseded by later value changes.</param>
/// <param name="SignatureCount">Total signatures recorded over the form instance.</param>
public sealed record CollectionInstanceSummary(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    string OpenedBy,
    DateTimeOffset OpenedAt,
    long Version,
    bool Complete,
    int ItemCount,
    int RecordedCount,
    string? CollectionTitle = null,
    int MissingRequiredCount = 0,
    int VerifiedCount = 0,
    int UnverifiedCount = 0,
    bool Locked = false,
    bool Frozen = false,
    int LockCount = 0,
    bool Signed = false,
    bool SignatureSuperseded = false,
    int SignatureCount = 0);

/// <summary>A form instance and everything a form screen shows about it.</summary>
/// <remarks>
/// <para><c>ProtocolVersionId</c>: The protocol version that governs the form (K-TIME-8).</para>
/// <para><c>Version</c>: 1 when opened, one more per save that changed a value.</para>
/// <para><c>Items</c>: The CollectionSpec's items in the governing version, in the specification's order: what the form
/// collects, which items are required and the kind of value each takes. Empty when that version no longer has the
/// specification.</para>
/// <para><c>Values</c>: One per CollectionSpec item that has a value, ordered by item id.</para>
/// <para><c>Locks</c>: The locks in force at the form's study, study site, enrollment, the form itself or one of its
/// values, widest scope first (spec §10.5).</para>
/// <para><c>Assertions</c>: Verifications, queries and answers about the form's values, retracted ones included,
/// oldest first (spec §8.2).</para>
/// <para><c>Attestations</c>: The signatures over the form instance, oldest first (spec §9.5).</para>
/// <para><c>ContentHash</c>: The hash a signature over the form's current values would bind to (#5463); null while the
/// form has no values.</para>
/// <para><c>CollectionTitle</c>: The CollectionSpec's title in the governing protocol version, when present.</para>
/// <para><c>EnrollmentVersion</c>: Current version index of the governing enrollment for lineage linking.</para>
/// <para><c>BlindedItemIds</c> and <c>BlindedObservationIds</c>: Items and observations classified as blinded treatment data.</para>
/// <para><c>ItemCodelists</c>: Hydrated codelist options keyed by item id for items whose terminology declares a codelist.</para>
/// </remarks>
public sealed record CollectionInstanceRecord(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    string OpenedBy,
    DateTimeOffset OpenedAt,
    long Version,
    IReadOnlyList<CollectionItem> Items,
    IReadOnlyList<CollectedValueView> Values,
    IReadOnlyList<LockView> Locks,
    IReadOnlyList<AssertionView> Assertions,
    IReadOnlyList<FormAttestationView> Attestations,
    string? ContentHash,
    string? CollectionTitle = null,
    long EnrollmentVersion = 1,
    IReadOnlySet<string>? BlindedItemIds = null,
    IReadOnlySet<ObservationId>? BlindedObservationIds = null,
    IReadOnlyDictionary<string, IReadOnlyList<CodelistOptionView>>? ItemCodelists = null)
{
    private const string MaskedLiteral = "masked";

    /// <summary>True when <paramref name="itemId"/> or <paramref name="observationId"/> is classified as blinded treatment data.</summary>
    public bool IsValueBlinded(string itemId, ObservationId observationId) =>
        (BlindedItemIds?.Contains(itemId) ?? false)
        || (BlindedObservationIds?.Contains(observationId) ?? false);

    /// <summary>True when a query over <paramref name="itemId"/> / <paramref name="observationId"/> targets blinded treatment data.</summary>
    public bool IsQueryBlinded(string itemId, ObservationId? observationId) =>
        (BlindedItemIds?.Contains(itemId) ?? false)
        || (observationId is { } obs && (BlindedObservationIds?.Contains(obs) ?? false));

    /// <summary>Returns a copy with blinded treatment values, signed values, content hashes, and assertions redacted (K-AUTH-4).</summary>
    public CollectionInstanceRecord MaskBlinded()
    {
        var blindedItems = BlindedItemIds ?? FrozenSet<string>.Empty;
        var blindedObservations = BlindedObservationIds ?? FrozenSet<ObservationId>.Empty;
        if (blindedItems.Count == 0 && blindedObservations.Count == 0)
        {
            return this;
        }

        var effectiveBlindedItems = new HashSet<string>(blindedItems, StringComparer.Ordinal);
        foreach (var v in Values.Where(v => blindedObservations.Contains(v.ObservationId)))
        {
            effectiveBlindedItems.Add(v.ItemId);
        }

        var blindedObservationGuids = blindedObservations.Select(o => o.Value).ToHashSet();
        foreach (var v in Values.Where(v => blindedItems.Contains(v.ItemId)))
        {
            blindedObservationGuids.Add(v.ObservationId.Value);
        }

        var maskedValues = Values
            .Select(v => MaskCollectedValue(v))
            .ToList();

        var blindedQueryIds = Assertions
            .Where(a => a.Kind == AssertionKind.Queried
                && a.Target.Kind == NodeKinds.Observation
                && blindedObservationGuids.Contains(a.Target.Id))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();

        var maskedAssertions = Assertions
            .Select(a => MaskAssertionIfBlinded(a, blindedObservationGuids, blindedQueryIds))
            .ToList();

        var maskedLocks = Locks
            .Select(l => l.Scope.Kind == NodeKinds.Observation && blindedObservationGuids.Contains(l.Scope.Id)
                ? l with { LockedBy = MaskedLiteral, ReasonForChange = MaskedLiteral }
                : l)
            .ToList();

        var maskedAttestations = Attestations
            .Select(sig => sig with
            {
                ContentHash = MaskedLiteral,
                Reason = MaskedLiteral,
                SignedValues = sig.SignedValues?
                    .Select(sv => IsValueBlinded(sv.ItemId, sv.ObservationId)
                        ? sv with { Value = new MissingValue(NullFlavor.Masked) }
                        : sv)
                    .ToList(),
            })
            .ToList();

        IReadOnlyDictionary<string, IReadOnlyList<CodelistOptionView>>? maskedItemCodelists = null;
        if (ItemCodelists is not null)
        {
            var remaining = ItemCodelists
                .Where(kvp => !effectiveBlindedItems.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
            if (remaining.Count > 0)
            {
                maskedItemCodelists = remaining;
            }
        }

        return this with
        {
            Values = maskedValues,
            Locks = maskedLocks,
            Assertions = maskedAssertions,
            Attestations = maskedAttestations,
            ContentHash = null,
            ItemCodelists = maskedItemCodelists,
        };
    }

    private CollectedValueView MaskCollectedValue(CollectedValueView value)
    {
        if (!IsValueBlinded(value.ItemId, value.ObservationId))
        {
            return value;
        }

        var maskedValue = value.Value is MissingValue { Reason: NullFlavor.NotAsked }
            ? value.Value
            : new MissingValue(NullFlavor.Masked);
        return value with
        {
            Value = maskedValue,
            RecordedBy = MaskedLiteral,
        };
    }

    private static AssertionView MaskAssertionIfBlinded(
        AssertionView assertion,
        HashSet<Guid> blindedObservationGuids,
        HashSet<Guid> blindedQueryIds)
    {
        var isBlinded = (assertion.Target.Kind == NodeKinds.Observation && blindedObservationGuids.Contains(assertion.Target.Id))
            || (assertion.Target.Kind == NodeKinds.Assertion && blindedQueryIds.Contains(assertion.Target.Id));
        if (!isBlinded)
        {
            return assertion;
        }

        var maskedText = assertion.Text is null ? null : MaskedLiteral;
        return assertion with
        {
            Text = maskedText,
            AssertedBy = MaskedLiteral,
            ReasonForChange = MaskedLiteral,
        };
    }
}

/// <summary>One option in a collection item's governed codelist.</summary>
/// <param name="Code">The code recorded in <see cref="CodedValue.Code"/>.</param>
/// <param name="Display">Human-readable label for the code.</param>
/// <param name="SubmissionValue">Optional standards submission value (for example CDISC CT submission value).</param>
public sealed record CodelistOptionView(string Code, string Display, string? SubmissionValue = null);

/// <summary>One value of a form instance.</summary>
/// <remarks>
/// <para><c>Revision</c>: The value's current revision, the expected revision of its next edit.</para>
/// <para><c>RecordedBy</c> and <c>RecordedAt</c>: Who entered the current revision, and when.</para>
/// </remarks>
public sealed record CollectedValueView(
    string ItemId,
    ObservationId ObservationId,
    ConceptRef Concept,
    ObservationValue Value,
    DateTimeOffset ObservedAt,
    int Revision,
    string RecordedBy,
    DateTimeOffset RecordedAt);

/// <summary>A signature over a form instance (spec §9.5).</summary>
/// <remarks>
/// <para><c>ContentHash</c>: The hash of the member revisions it signed; a later correction leaves it bound to them.</para>
/// <para><c>Superseded</c>: True once a correction or a new value changed the form after it was signed: the signature
/// stands for the revisions it signed, not for the form as it is now (#5463).</para>
/// <para><c>SignedValues</c>: The revisions it signed, ordered by item id, rebuilt from the values' history; null in the
/// rare case they cannot be rebuilt to the signed hash (a save recorded at the same instant as the signature).</para>
/// </remarks>
public sealed record FormAttestationView(
    AttestationId AttestationId,
    AttestationMeaning Meaning,
    string SignedBy,
    DateTimeOffset SignedAt,
    string ContentHash,
    string Reason,
    bool Superseded,
    IReadOnlyList<SignedValueView>? SignedValues);

/// <summary>One value as a signature signed it.</summary>
public sealed record SignedValueView(string ItemId, ObservationId ObservationId, int Revision, ObservationValue Value);
