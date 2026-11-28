using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Evidence;

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
    bool Complete = true,
    IReadOnlyList<string>? MissingRequired = null,
    int OpenQueryCount = 0,
    int AnsweredQueryCount = 0,
    int OpenErrorFindingCount = 0,
    IReadOnlyList<string>? ReadinessBlockers = null,
    IReadOnlySet<string>? BlindedItemIds = null,
    IReadOnlySet<ObservationId>? BlindedObservationIds = null,
    IReadOnlyDictionary<string, IReadOnlyList<CodelistOptionView>>? ItemCodelists = null,
    DateTimeOffset? EnrollmentReferenceAt = null,
    VisitTimingAssessment? VisitTimingAssessment = null,
    bool VisitTimingEvaluated = false)
{
    private const string MaskedLiteral = "masked";

    /// <summary>True when <paramref name="itemId"/> or <paramref name="observationId"/> is classified as blinded treatment data.</summary>
    public bool IsValueBlinded(string itemId, ObservationId observationId) =>
        (BlindedItemIds?.Contains(itemId) ?? false)
        || (BlindedObservationIds?.Contains(observationId) ?? false);

    /// <summary>True when a query over <paramref name="itemId"/> / <paramref name="observationId"/> targets blinded treatment data.</summary>
    public bool IsQueryBlinded(string itemId, ObservationId observationId) =>
        IsValueBlinded(itemId, observationId);

    /// <summary>Returns a copy with blinded treatment values, signed values, content hashes, and assertions redacted (K-AUTH-4).</summary>
    public CollectionInstanceRecord MaskBlinded()
    {
        var blindedItems = BlindedItemIds ?? FrozenSet<string>.Empty;
        var blindedObservations = BlindedObservationIds ?? FrozenSet<ObservationId>.Empty;
        var effectiveBlindedItems = new HashSet<string>(blindedItems, StringComparer.Ordinal);
        var specBlinded = BlindedTreatmentClassifier.Instance.IsBlindedCollectionSpec(CollectionSpecId, CollectionTitle);
        foreach (var item in Items)
        {
            if (specBlinded || BlindedTreatmentClassifier.Instance.IsBlindedCollectionItem(CollectionSpecId, CollectionTitle, item))
            {
                effectiveBlindedItems.Add(item.Id);
            }
        }

        foreach (var v in Values)
        {
            if (specBlinded
                || blindedObservations.Contains(v.ObservationId)
                || BlindedTreatmentClassifier.Instance.IsBlindedObservation(
                    v.Concept.ConceptId,
                    v.Value,
                    concept: null,
                    CollectionSpecId,
                    v.ItemId,
                    []))
            {
                effectiveBlindedItems.Add(v.ItemId);
            }
        }

        foreach (var sv in Attestations
            .Where(s => s.SignedValues is not null)
            .SelectMany(s => s.SignedValues!))
        {
            if (specBlinded
                || blindedObservations.Contains(sv.ObservationId)
                || BlindedTreatmentClassifier.Instance.IsBlindedObservation(
                    ConceptId.Empty,
                    sv.Value,
                    concept: null,
                    CollectionSpecId,
                    sv.ItemId,
                    []))
            {
                effectiveBlindedItems.Add(sv.ItemId);
            }
        }

        if (effectiveBlindedItems.Count == 0 && blindedObservations.Count == 0)
        {
            return this;
        }

        var blindedObservationGuids = blindedObservations.Select(o => o.Value).ToHashSet();
        foreach (var v in Values.Where(v => effectiveBlindedItems.Contains(v.ItemId) && !v.ObservationId.IsEmpty))
        {
            blindedObservationGuids.Add(v.ObservationId.Value);
        }

        foreach (var sv in Attestations
            .Where(s => s.SignedValues is not null)
            .SelectMany(s => s.SignedValues!)
            .Where(sv => effectiveBlindedItems.Contains(sv.ItemId) && !sv.ObservationId.IsEmpty))
        {
            blindedObservationGuids.Add(sv.ObservationId.Value);
        }

        var maskedScope = NodeRef.For(CollectionInstanceId);

        var maskedValues = Values
            .Select(v => MaskCollectedValue(v, effectiveBlindedItems, blindedObservationGuids))
            .ToList();

        var blindedQueryIds = Assertions
            .Where(a => a.Kind == AssertionKind.Queried
                && a.Target.Kind == NodeKinds.Observation
                && blindedObservationGuids.Contains(a.Target.Id))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();

        var maskedAssertions = Assertions
            .Select(a => MaskAssertionIfBlinded(a, maskedScope, blindedObservationGuids, blindedQueryIds))
            .ToList();

        var maskedLocks = Locks
            .Select(l => l.Scope.Kind == NodeKinds.Observation && blindedObservationGuids.Contains(l.Scope.Id)
                ? l with
                {
                    Scope = maskedScope,
                    LockedBy = MaskedLiteral,
                    ReasonForChange = MaskedLiteral,
                    Masked = true,
                }
                : l)
            .ToList();

        var maskedAttestations = Attestations
            .Select(sig => sig with
            {
                ContentHash = MaskedLiteral,
                Reason = MaskedLiteral,
                SignedValues = sig.SignedValues?
                    .Select(sv => MaskSignedValue(sv, effectiveBlindedItems, blindedObservationGuids))
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
            BlindedItemIds = effectiveBlindedItems.ToFrozenSet(StringComparer.Ordinal),
            BlindedObservationIds = FrozenSet<ObservationId>.Empty,
            ItemCodelists = maskedItemCodelists,
        };
    }

    private static CollectedValueView MaskCollectedValue(
        CollectedValueView value,
        HashSet<string> effectiveBlindedItems,
        HashSet<Guid> blindedObservationGuids)
    {
        if (!effectiveBlindedItems.Contains(value.ItemId)
            && (value.ObservationId.IsEmpty || !blindedObservationGuids.Contains(value.ObservationId.Value)))
        {
            return value;
        }

        var maskedValue = value.Value is MissingValue { Reason: NullFlavor.NotAsked }
            ? value.Value
            : new MissingValue(NullFlavor.Masked);
        return value with
        {
            ObservationId = ObservationId.Empty,
            Value = maskedValue,
            RecordedBy = MaskedLiteral,
        };
    }

    private static SignedValueView MaskSignedValue(
        SignedValueView signedValue,
        HashSet<string> effectiveBlindedItems,
        HashSet<Guid> blindedObservationGuids)
    {
        if (!effectiveBlindedItems.Contains(signedValue.ItemId)
            && (signedValue.ObservationId.IsEmpty || !blindedObservationGuids.Contains(signedValue.ObservationId.Value)))
        {
            return signedValue;
        }

        var maskedValue = signedValue.Value is MissingValue { Reason: NullFlavor.NotAsked }
            ? signedValue.Value
            : new MissingValue(NullFlavor.Masked);
        return signedValue with
        {
            ObservationId = ObservationId.Empty,
            Value = maskedValue,
        };
    }

    private static AssertionView MaskAssertionIfBlinded(
        AssertionView assertion,
        NodeRef maskedScope,
        HashSet<Guid> blindedObservationGuids,
        HashSet<Guid> blindedQueryIds)
    {
        var isBlinded = assertion.Masked
            || (assertion.Target.Kind == NodeKinds.Observation && blindedObservationGuids.Contains(assertion.Target.Id))
            || (assertion.Target.Kind == NodeKinds.Assertion && blindedQueryIds.Contains(assertion.Target.Id));
        if (!isBlinded)
        {
            return assertion;
        }

        var maskedText = assertion.Text is null ? null : MaskedLiteral;
        return assertion with
        {
            Target = maskedScope,
            Text = maskedText,
            AssertedBy = MaskedLiteral,
            ReasonForChange = MaskedLiteral,
            Masked = true,
        };
    }
}

/// <summary>Shared completeness and signature readiness rules for form instances.</summary>
public static class CollectionReadiness
{
    /// <summary>True only when required items are recorded, no active queries remain, and no active Error findings remain.</summary>
    public static bool IsComplete(
        int missingRequiredCount,
        int openQueryCount,
        int answeredQueryCount,
        int openErrorFindingCount) =>
        missingRequiredCount == 0
        && openQueryCount == 0
        && answeredQueryCount == 0
        && openErrorFindingCount == 0;

    /// <summary>Formats diagnostic readiness blockers for a form instance.</summary>
    public static IReadOnlyList<string> BuildBlockers(
        IReadOnlyList<string>? missingRequired,
        int missingRequiredCount,
        int openQueryCount,
        int answeredQueryCount,
        int openErrorFindingCount)
    {
        var blockers = new List<string>(4);
        if (missingRequired is { Count: > 0 })
        {
            blockers.Add(missingRequired.Count == 1
                ? $"Required item '{missingRequired[0]}' has no recorded value."
                : $"{missingRequired.Count} required items have no recorded value: {string.Join(", ", missingRequired)}.");
        }
        else if (missingRequiredCount > 0)
        {
            blockers.Add(missingRequiredCount == 1
                ? "1 required item has no recorded value."
                : $"{missingRequiredCount} required items have no recorded value.");
        }

        if (openQueryCount > 0)
        {
            blockers.Add(openQueryCount == 1
                ? "1 open query must be answered and closed."
                : $"{openQueryCount} open queries must be answered and closed.");
        }

        if (answeredQueryCount > 0)
        {
            blockers.Add(answeredQueryCount == 1
                ? "1 answered query is awaiting monitor closure."
                : $"{answeredQueryCount} answered queries are awaiting monitor closure.");
        }

        if (openErrorFindingCount > 0)
        {
            blockers.Add(openErrorFindingCount == 1
                ? "1 active Error validation finding must be resolved, accepted, or explained."
                : $"{openErrorFindingCount} active Error validation findings must be resolved, accepted, or explained.");
        }

        return blockers;
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
