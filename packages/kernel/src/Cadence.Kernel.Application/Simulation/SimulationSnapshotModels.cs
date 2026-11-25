using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Amendment;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Kernel.Application.Simulation;

/// <summary>Bitemporal snapshot of a study site read at a pinned simulation instant.</summary>
public sealed record SimulationSiteSnapshot(
    StudySiteId Id,
    SiteId SiteId,
    string Country,
    string Status,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>Bitemporal snapshot of an effectivity scope read at a pinned simulation instant.</summary>
public sealed record SimulationEffectivitySnapshot(
    EffectivityId Id,
    ProtocolVersionId ProtocolVersionId,
    EffectivityScope Scope,
    EffectivityStatus? StatusAsOf);

/// <summary>Bitemporal snapshot of an enrollment read at a pinned simulation instant.</summary>
public sealed record SimulationEnrollmentSnapshot(
    EnrollmentId Id,
    StudySiteId StudySiteId,
    string SubjectCode,
    EnrollmentStatus StatusAsOf,
    DateOnly ReferenceStartDate);

/// <summary>Bitemporal snapshot of a collected observation revision read at a pinned simulation instant.</summary>
public sealed record SimulationObservationSnapshot(
    ObservationId Id,
    EnrollmentId EnrollmentId,
    StudySiteId StudySiteId,
    ConceptRef Concept,
    ObservationValue Value,
    CollectionInstanceId? CollectionInstanceId = null,
    ProtocolVersionId? GoverningProtocolVersionId = null,
    string? CollectionSpecId = null,
    string? PlannedActivityId = null,
    string? ItemId = null);

/// <summary>One collected item value on a <see cref="SimulationCollectionSnapshot"/>.</summary>
public sealed record SimulationCollectedItemSnapshot(
    ObservationId ObservationId,
    ConceptRef Concept,
    ObservationValue Value);

/// <summary>Bitemporal snapshot of a collection form instance read at a pinned simulation instant.</summary>
public sealed record SimulationCollectionSnapshot(
    CollectionInstanceId Id,
    EnrollmentId EnrollmentId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    DateTimeOffset OpenedAt,
    IReadOnlyDictionary<string, SimulationCollectedItemSnapshot> ValuesByItemId);

/// <summary>Read-only bitemporal study snapshot loaded for simulation evaluation (K-SIM-1, K-SIM-2).</summary>
public sealed record SimulationStudySnapshot(
    IReadOnlyList<SimulationSiteSnapshot> Sites,
    IReadOnlyList<SimulationEffectivitySnapshot> Effectivities,
    IReadOnlyList<SimulationEnrollmentSnapshot> Enrollments,
    IReadOnlyList<SimulationObservationSnapshot> Observations,
    IReadOnlyList<SimulationCollectionSnapshot> Collections,
    IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign> ProtocolDesigns,
    IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> Concepts,
    IReadOnlyList<LibraryItemView> PinnedLibraryItems,
    IReadOnlyList<LibraryItemView> PinnedRulePacks,
    IReadOnlyList<StandardVersionId> DeclaredStandardVersions,
    IReadOnlyDictionary<(string Codelist, string Code), string> CodelistDecodes,
    bool Loaded = true,
    IReadOnlyList<(string Standard, string Version)>? DeclaredStandards = null)
{
    /// <summary>An empty study snapshot when no operational persistence snapshot is supplied.</summary>
    public static SimulationStudySnapshot Empty { get; } = new(
        [],
        [],
        [],
        [],
        [],
        new Dictionary<ProtocolVersionId, ProtocolDesign>(),
        new Dictionary<ConceptId, BlindedConceptMetadata>(),
        [],
        [],
        [],
        new Dictionary<(string Codelist, string Code), string>(),
        Loaded: false);

    /// <summary>True when the snapshot contains any operational, clinical, or standards records.</summary>
    public bool HasAnyRecords =>
        Sites.Count > 0
        || Effectivities.Count > 0
        || Enrollments.Count > 0
        || Observations.Count > 0
        || Collections.Count > 0
        || PinnedLibraryItems.Count > 0
        || PinnedRulePacks.Count > 0;
}

/// <summary>
/// Visibility- and blinding-filtered study snapshot produced before simulation evaluation or hashing (K-SIM-4, K-AUTH-4, ADR-2590).
/// </summary>
internal sealed record MaskedSimulationSnapshot(
    IReadOnlyList<SimulationSiteSnapshot> VisibleSites,
    IReadOnlyList<SimulationEffectivitySnapshot> VisibleEffectivities,
    IReadOnlyList<SimulationEnrollmentSnapshot> VisibleEnrollments,
    IReadOnlyList<SimulationObservationSnapshot> VisibleObservations,
    IReadOnlyList<SimulationCollectionSnapshot> VisibleCollections,
    IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign> ProtocolDesigns,
    IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> Concepts,
    IReadOnlyList<LibraryItemView> PinnedLibraryItems,
    IReadOnlyList<LibraryItemView> PinnedRulePacks,
    IReadOnlyList<StandardVersionId> DeclaredStandardVersions,
    IReadOnlyList<(string Standard, string Version)> DeclaredStandards,
    IReadOnlyDictionary<(string Codelist, string Code), string> CodelistDecodes,
    IReadOnlySet<EnrollmentId> UnblindedEnrollments,
    bool CanUnblindStudy,
    HiddenObjects Hidden,
    IReadOnlyList<MaskedPart> MaskedParts,
    string VisibleStateDigest);

/// <summary>
/// Filters a <see cref="SimulationStudySnapshot"/> through <see cref="INodeVisibility"/> and
/// <see cref="IBlindedTreatmentClassifier"/> before any counts, projections, rule deltas, or hashes are computed.
/// </summary>
internal static class SimulationSnapshotMasking
{
    public const string SitesPart = "sites";
    public const string ParticipantsPart = "participants";
    public const string BlindedTreatmentPart = "blinded_treatment";
    public const string ObservationsPart = "observations";

    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    public static async Task<MaskedSimulationSnapshot> MaskAsync(
        TenantId tenant,
        StudyId studyId,
        Actor actor,
        SimulationStudySnapshot raw,
        IReadOnlyList<ProtocolDesign> extraDesigns,
        INodeVisibility visibility,
        IBlindedTreatmentClassifier blinding,
        IAuthorizationProbe? probe,
        CancellationToken cancellationToken,
        IAuthorizationEvaluator? evaluator = null,
        SimulationAsOf? asOf = null,
        bool includeVisibleDigest = true)
    {
        var estimatedNodeCount = (raw.Sites.Count * 2) + raw.Enrollments.Count + raw.Collections.Count + raw.Observations.Count;
        var nodes = new HashSet<NodeRef>(estimatedNodeCount);
        foreach (var site in raw.Sites)
        {
            nodes.Add(NodeRef.For(site.Id));
            nodes.Add(NodeRef.For(site.SiteId));
        }

        foreach (var enrollment in raw.Enrollments)
        {
            nodes.Add(NodeRef.For(enrollment.Id));
        }

        foreach (var collection in raw.Collections)
        {
            nodes.Add(NodeRef.For(collection.Id));
        }

        foreach (var observation in raw.Observations)
        {
            nodes.Add(NodeRef.For(observation.Id));
        }

        var masks = nodes.Count == 0
            ? new Dictionary<NodeRef, MaskReason>()
            : await visibility.MaskAsync(tenant, actor, nodes, cancellationToken).ConfigureAwait(false);

        var hiddenReasons = new HashSet<MaskReason>();
        var maskedParts = new SortedSet<(string Part, MaskReason Reason)>(
            Comparer<(string Part, MaskReason Reason)>.Create((a, b) =>
            {
                var partCmp = string.CompareOrdinal(a.Part, b.Part);
                return partCmp != 0 ? partCmp : a.Reason.CompareTo(b.Reason);
            }));

        var visibleSites = new List<SimulationSiteSnapshot>(raw.Sites.Count);
        var hiddenStudySiteIds = new HashSet<StudySiteId>();
        foreach (var site in raw.Sites)
        {
            var reason = MaskOf(masks, NodeRef.For(site.Id)) ?? MaskOf(masks, NodeRef.For(site.SiteId));
            if (reason is { } siteMask)
            {
                hiddenStudySiteIds.Add(site.Id);
                hiddenReasons.Add(siteMask);
                maskedParts.Add((SitesPart, siteMask));
                continue;
            }

            visibleSites.Add(site);
        }

        var visibleEnrollments = new List<SimulationEnrollmentSnapshot>(raw.Enrollments.Count);
        var hiddenEnrollmentIds = new HashSet<EnrollmentId>();
        foreach (var enrollment in raw.Enrollments)
        {
            var reason = MaskOf(masks, NodeRef.For(enrollment.Id));
            if (reason is null && hiddenStudySiteIds.Contains(enrollment.StudySiteId))
            {
                reason = MaskOf(masks, NodeRef.For(enrollment.StudySiteId)) ?? MaskReason.NotPermitted;
            }

            if (reason is { } enrollmentMask)
            {
                hiddenEnrollmentIds.Add(enrollment.Id);
                hiddenReasons.Add(enrollmentMask);
                maskedParts.Add((ParticipantsPart, enrollmentMask));
                continue;
            }

            visibleEnrollments.Add(enrollment);
        }

        var visibleEffectivities = raw.Effectivities
            .Where(e => e.Scope.StudySiteId is not { } siteId || !hiddenStudySiteIds.Contains(siteId))
            .ToList();

        async ValueTask<bool> CanUnblindResourceAsync(NodeRef resource)
        {
            if (probe is not null)
            {
                return (await probe.CanIAsync(tenant, EnrollmentUnblindCapability, resource, cancellationToken).ConfigureAwait(false)).Permitted;
            }

            if (evaluator is not null && asOf is not null)
            {
                var authRequest = new AuthorizationRequest(actor, tenant, EnrollmentUnblindCapability, resource, asOf.ValidAt, asOf.KnownAt);
                return (await evaluator.EvaluateAsync(authRequest, cancellationToken).ConfigureAwait(false)).Permitted;
            }

            return true;
        }

        var allDesigns = raw.ProtocolDesigns.Values.Concat(extraDesigns).ToList();
        var firstSpecById = ProtocolBlindingIndex.IndexFirstSpecsById(allDesigns);
        var blindingIndex = blinding is BlindedTreatmentClassifier
            ? ProtocolBlindingIndex.Create(allDesigns, raw.Concepts)
            : null;
        var canUnblindStudy = await CanUnblindResourceAsync(NodeRef.For(studyId)).ConfigureAwait(false);

        var unblindByEnrollment = new Dictionary<EnrollmentId, bool>(visibleEnrollments.Count);
        var unblindedEnrollments = new HashSet<EnrollmentId>();

        async ValueTask<bool> CanUnblindEnrollmentAsync(EnrollmentId enrollmentId)
        {
            if (!unblindByEnrollment.TryGetValue(enrollmentId, out var allowed))
            {
                allowed = await CanUnblindResourceAsync(NodeRef.For(enrollmentId)).ConfigureAwait(false);
                unblindByEnrollment[enrollmentId] = allowed;
                if (allowed)
                {
                    unblindedEnrollments.Add(enrollmentId);
                }
            }

            return allowed;
        }

        foreach (var visibleEnrollment in visibleEnrollments)
        {
            _ = await CanUnblindEnrollmentAsync(visibleEnrollment.Id).ConfigureAwait(false);
        }

        var maskedObservationIds = new HashSet<ObservationId>();
        var visibleObservations = new List<SimulationObservationSnapshot>(raw.Observations.Count);
        foreach (var obs in raw.Observations)
        {
            if (hiddenEnrollmentIds.Contains(obs.EnrollmentId) || hiddenStudySiteIds.Contains(obs.StudySiteId))
            {
                maskedObservationIds.Add(obs.Id);
                continue;
            }

            var obsMask = MaskOf(masks, NodeRef.For(obs.Id));
            if (obsMask is null && obs.CollectionInstanceId is { } cid)
            {
                obsMask = MaskOf(masks, NodeRef.For(cid));
            }

            if (obsMask is null && IsObservationBlinded(obs, raw.Concepts, allDesigns, blinding, blindingIndex))
            {
                var canUnblind = await CanUnblindEnrollmentAsync(obs.EnrollmentId).ConfigureAwait(false);
                if (!canUnblind)
                {
                    obsMask = MaskReason.Blinded;
                }
            }

            if (obsMask is { } reason)
            {
                maskedObservationIds.Add(obs.Id);
                hiddenReasons.Add(reason);
                var partName = reason == MaskReason.Blinded ? BlindedTreatmentPart : ObservationsPart;
                maskedParts.Add((partName, reason));
                continue;
            }

            visibleObservations.Add(obs);
        }

        var visibleCollections = new List<SimulationCollectionSnapshot>(raw.Collections.Count);
        foreach (var collection in raw.Collections)
        {
            if (hiddenEnrollmentIds.Contains(collection.EnrollmentId) || hiddenStudySiteIds.Contains(collection.StudySiteId))
            {
                continue;
            }

            var collectionMask = MaskOf(masks, NodeRef.For(collection.Id));
            if (collectionMask is null && IsCollectionSpecBlinded(collection.CollectionSpecId, allDesigns, blinding, blindingIndex))
            {
                var canUnblind = await CanUnblindEnrollmentAsync(collection.EnrollmentId).ConfigureAwait(false);
                if (!canUnblind)
                {
                    collectionMask = MaskReason.Blinded;
                }
            }

            if (collectionMask is { } cMask)
            {
                hiddenReasons.Add(cMask);
                var partName = cMask == MaskReason.Blinded ? BlindedTreatmentPart : ObservationsPart;
                maskedParts.Add((partName, cMask));
                continue;
            }

            firstSpecById.TryGetValue(collection.CollectionSpecId, out var matchingSpecIndex);
            var matchingSpec = matchingSpecIndex?.Spec;

            var filteredItems = new Dictionary<string, SimulationCollectedItemSnapshot>(collection.ValuesByItemId.Count, StringComparer.Ordinal);
            foreach (var (itemId, itemValue) in collection.ValuesByItemId)
            {
                if (maskedObservationIds.Contains(itemValue.ObservationId))
                {
                    continue;
                }

                CollectionItem? specItem = null;
                matchingSpecIndex?.ItemsById.TryGetValue(itemId, out specItem);
                if (IsCollectionItemBlinded(collection.CollectionSpecId, matchingSpec?.Name, specItem, itemValue, raw.Concepts, allDesigns, blinding, blindingIndex))
                {
                    var canUnblind = await CanUnblindEnrollmentAsync(collection.EnrollmentId).ConfigureAwait(false);
                    if (!canUnblind)
                    {
                        hiddenReasons.Add(MaskReason.Blinded);
                        maskedParts.Add((BlindedTreatmentPart, MaskReason.Blinded));
                        continue;
                    }
                }

                filteredItems[itemId] = itemValue;
            }

            visibleCollections.Add(collection with { ValuesByItemId = filteredItems });
        }

        var orderedReasons = hiddenReasons.OrderBy(r => r).ToArray();
        var hidden = orderedReasons.Length == 0 ? HiddenObjects.None : new HiddenObjects(MoreExist: true, orderedReasons);
        var parts = maskedParts.Select(p => new MaskedPart(p.Part, p.Reason)).ToList();
        var digest = includeVisibleDigest
            ? ComputeVisibleDigest(visibleSites, visibleEffectivities, visibleEnrollments, visibleObservations, visibleCollections)
            : string.Empty;

        return new MaskedSimulationSnapshot(
            visibleSites,
            visibleEffectivities,
            visibleEnrollments,
            visibleObservations,
            visibleCollections,
            raw.ProtocolDesigns,
            raw.Concepts,
            raw.PinnedLibraryItems,
            raw.PinnedRulePacks,
            raw.DeclaredStandardVersions,
            raw.DeclaredStandards ?? [],
            raw.CodelistDecodes,
            unblindedEnrollments,
            canUnblindStudy,
            hidden,
            parts,
            digest);
    }

    /// <summary>
    /// Omits masked objects from <paramref name="report"/> so counts never leak restricted sites or blinded records,
    /// returning the filtered report and any mask reasons encountered (ADR-2590, K-SIM-4).
    /// </summary>
    public static (DependentsReport Filtered, IReadOnlyList<MaskReason> Reasons) OmitMaskedDependents(DependentsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var reasons = new SortedSet<MaskReason>();
        var groups = new List<DependentGroup>(report.Groups.Count);
        var total = 0;

        foreach (var group in report.Groups)
        {
            var visibleObjects = new List<LineageNode>(group.Objects.Count);
            foreach (var obj in group.Objects)
            {
                if (obj.Mask is { } mask)
                {
                    reasons.Add(mask);
                    continue;
                }

                visibleObjects.Add(obj);
            }

            if (visibleObjects.Count > 0)
            {
                groups.Add(new DependentGroup(group.Kind, visibleObjects.Count, visibleObjects));
                total += visibleObjects.Count;
            }
        }

        if (reasons.Count == 0)
        {
            return (report, []);
        }

        var filtered = new DependentsReport(report.Start, report.Depth, report.ValidAt, report.KnownAt, groups, total, report.Truncated);
        return (filtered, [.. reasons]);
    }

    /// <summary>Merges additional mask reasons into <paramref name="existing"/>.</summary>
    public static HiddenObjects MergeHidden(HiddenObjects existing, IEnumerable<MaskReason> additional)
    {
        var all = existing.Reasons.Concat(additional).Distinct().OrderBy(r => r).ToArray();
        return all.Length == 0 ? HiddenObjects.None : new HiddenObjects(MoreExist: true, all);
    }

    private static MaskReason? MaskOf(IReadOnlyDictionary<NodeRef, MaskReason> masks, NodeRef node) =>
        masks.TryGetValue(node, out var reason) ? reason : null;

    private static bool IsObservationBlinded(
        SimulationObservationSnapshot obs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> concepts,
        IReadOnlyList<ProtocolDesign> allDesigns,
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex)
    {
        if (blindingIndex is not null)
        {
            return blindingIndex.IsBlindedObservation(
                obs.Concept.ConceptId,
                obs.Value,
                obs.Concept.ConceptId,
                obs.CollectionSpecId,
                obs.ItemId);
        }

        BlindedConceptMetadata? conceptMeta = concepts.TryGetValue(obs.Concept.ConceptId, out var foundConcept)
            ? foundConcept
            : null;
        return blinding.IsBlindedObservation(obs.Concept.ConceptId, obs.Value, conceptMeta, obs.CollectionSpecId, obs.ItemId, allDesigns);
    }

    private static bool IsCollectionSpecBlinded(
        string collectionSpecId,
        IReadOnlyList<ProtocolDesign> allDesigns,
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex) =>
        blindingIndex is not null
            ? blindingIndex.IsBlindedCollectionSpec(collectionSpecId)
            : blinding.IsBlindedCollectionSpec(collectionSpecId, designs: allDesigns);

    private static bool IsCollectionItemBlinded(
        string collectionSpecId,
        string? collectionTitle,
        CollectionItem? specItem,
        SimulationCollectedItemSnapshot itemValue,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> concepts,
        IReadOnlyList<ProtocolDesign> allDesigns,
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex)
    {
        if (blindingIndex is not null)
        {
            return blindingIndex.IsBlindedCollectionItem(
                collectionSpecId,
                collectionTitle,
                specItem,
                itemValue.Value,
                itemValue.Concept.ConceptId);
        }

        BlindedConceptMetadata? itemConceptMeta = concepts.TryGetValue(itemValue.Concept.ConceptId, out var foundMeta)
            ? foundMeta
            : null;
        return blinding.IsBlindedCollectionItem(collectionSpecId, collectionTitle, specItem, itemValue.Value, itemConceptMeta, allDesigns);
    }

    internal static string ComputeVisibleDigest(
        IReadOnlyList<SimulationSiteSnapshot> sites,
        IReadOnlyList<SimulationEffectivitySnapshot> effectivities,
        IReadOnlyList<SimulationEnrollmentSnapshot> enrollments,
        IReadOnlyList<SimulationObservationSnapshot> observations,
        IReadOnlyList<SimulationCollectionSnapshot> collections) =>
        ContentHash.ComputeStreaming(writer =>
        {
            writer.WriteStartObject();

            writer.WritePropertyName("collections"u8);
            writer.WriteStartArray();
            ZeroLohOrdering.ForEachOrderedByGuid(
                collections,
                static c => c.Id.Value,
                writer,
                static (w, c) => WriteCollectionDigestEntry(w, c));
            writer.WriteEndArray();

            writer.WritePropertyName("effectivities"u8);
            writer.WriteStartArray();
            ZeroLohOrdering.ForEachOrderedByGuid(
                effectivities,
                static e => e.Id.Value,
                writer,
                static (w, e) => WriteEffectivityDigestEntry(w, e));
            writer.WriteEndArray();

            writer.WritePropertyName("enrollments"u8);
            writer.WriteStartArray();
            ZeroLohOrdering.ForEachOrderedByGuid(
                enrollments,
                static e => e.Id.Value,
                writer,
                static (w, e) => WriteEnrollmentDigestEntry(w, e));
            writer.WriteEndArray();

            writer.WritePropertyName("observations"u8);
            writer.WriteStartArray();
            ZeroLohOrdering.ForEachOrderedByGuid(
                observations,
                static o => o.Id.Value,
                writer,
                static (w, o) => WriteObservationDigestEntry(w, o));
            writer.WriteEndArray();

            writer.WritePropertyName("sites"u8);
            writer.WriteStartArray();
            ZeroLohOrdering.ForEachOrderedByGuid(
                sites,
                static s => s.Id.Value,
                writer,
                static (w, s) => WriteSiteDigestEntry(w, s));
            writer.WriteEndArray();

            writer.WriteEndObject();
        });

    private static void WriteCollectionDigestEntry(Utf8JsonWriter writer, SimulationCollectionSnapshot collection)
    {
        Span<char> guidChars = stackalloc char[36];
        writer.WriteStartObject();
        writer.WriteString("activity"u8, collection.PlannedActivityId);
        WriteGuidProperty(writer, "id"u8, collection.Id.Value, guidChars);
        writer.WritePropertyName("items"u8);
        writer.WriteStartArray();
        WriteSortedCollectionItems(writer, collection.ValuesByItemId);
        writer.WriteEndArray();
        writer.WriteString("spec"u8, collection.CollectionSpecId);
        writer.WriteEndObject();
    }

    private static void WriteSortedCollectionItems(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, SimulationCollectedItemSnapshot> items)
    {
        var count = items.Count;
        if (count == 0)
        {
            return;
        }

        if (count <= ZeroLohOrdering.MaxSohChunkLength)
        {
            var rented = ArrayPool<KeyValuePair<string, SimulationCollectedItemSnapshot>>.Shared.Rent(count);
            try
            {
                var idx = 0;
                foreach (var kv in items)
                {
                    rented[idx++] = kv;
                }

                if (idx > 1)
                {
                    Array.Sort(rented, 0, idx, CollectedItemKeyComparer.Instance);
                }

                for (var i = 0; i < idx; i++)
                {
                    var entry = rented[i];
                    writer.WriteStartObject();
                    writer.WriteString("item"u8, entry.Key);
                    WriteObservationDisplayValue(writer, "value"u8, entry.Value.Value);
                    writer.WriteEndObject();
                }
            }
            finally
            {
                ArrayPool<KeyValuePair<string, SimulationCollectedItemSnapshot>>.Shared.Return(rented, clearArray: true);
            }

            return;
        }

        ZeroLohOrdering.ForEachOrderedByKey(
            items,
            static kv => kv.Key,
            writer,
            static (w, entry) =>
            {
                w.WriteStartObject();
                w.WriteString("item"u8, entry.Key);
                WriteObservationDisplayValue(w, "value"u8, entry.Value.Value);
                w.WriteEndObject();
            });
    }

    private static void WriteEffectivityDigestEntry(Utf8JsonWriter writer, SimulationEffectivitySnapshot effectivity)
    {
        Span<char> guidChars = stackalloc char[36];
        writer.WriteStartObject();
        WriteGuidProperty(writer, "id"u8, effectivity.Id.Value, guidChars);
        WriteGuidProperty(writer, "protocolVersion"u8, effectivity.ProtocolVersionId.Value, guidChars);
        writer.WriteString("status"u8, effectivity.StatusAsOf?.ToString());
        writer.WriteEndObject();
    }

    private static void WriteEnrollmentDigestEntry(Utf8JsonWriter writer, SimulationEnrollmentSnapshot enrollment)
    {
        Span<char> guidChars = stackalloc char[36];
        Span<char> dateChars = stackalloc char[10];
        writer.WriteStartObject();
        WriteGuidProperty(writer, "id"u8, enrollment.Id.Value, guidChars);
        _ = enrollment.ReferenceStartDate.TryFormat(dateChars, out var dateLen, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        writer.WriteString("referenceStart"u8, dateChars[..dateLen]);
        writer.WriteString("status"u8, enrollment.StatusAsOf.ToString());
        writer.WriteString("subjectCode"u8, enrollment.SubjectCode);
        writer.WriteEndObject();
    }

    private static void WriteObservationDigestEntry(Utf8JsonWriter writer, SimulationObservationSnapshot observation)
    {
        Span<char> scratch = stackalloc char[64];
        writer.WriteStartObject();
        WriteConceptRefProperty(writer, "concept"u8, observation.Concept, scratch);
        WriteGuidProperty(writer, "id"u8, observation.Id.Value, scratch[..36]);
        WriteObservationDisplayValue(writer, "value"u8, observation.Value);
        writer.WriteEndObject();
    }

    private static void WriteSiteDigestEntry(Utf8JsonWriter writer, SimulationSiteSnapshot site)
    {
        Span<char> guidChars = stackalloc char[36];
        writer.WriteStartObject();
        writer.WriteBoolean("active"u8, site.IsActive);
        writer.WriteString("country"u8, site.Country);
        WriteGuidProperty(writer, "id"u8, site.Id.Value, guidChars);
        writer.WriteString("status"u8, site.Status);
        writer.WriteEndObject();
    }

    private static void WriteGuidProperty(Utf8JsonWriter writer, ReadOnlySpan<byte> propertyName, Guid id, Span<char> scratch)
    {
        _ = id.TryFormat(scratch, out var len, "D");
        writer.WriteString(propertyName, scratch[..len]);
    }

    private static void WriteConceptRefProperty(Utf8JsonWriter writer, ReadOnlySpan<byte> propertyName, ConceptRef concept, Span<char> scratch)
    {
        _ = concept.ConceptId.Value.TryFormat(scratch, out var guidLen, "D");
        scratch[guidLen] = '@';
        _ = concept.Version.TryFormat(scratch[(guidLen + 1)..], out var verLen, default, CultureInfo.InvariantCulture);
        writer.WriteString(propertyName, scratch[..(guidLen + 1 + verLen)]);
    }

    private static void WriteObservationDisplayValue(Utf8JsonWriter writer, ReadOnlySpan<byte> propertyName, ObservationValue value)
    {
        switch (value)
        {
            case QuantityValue q:
                var comp = q.Comparator switch
                {
                    QuantityComparator.LessThan => "<",
                    QuantityComparator.LessOrEqual => "<=",
                    QuantityComparator.GreaterThan => ">",
                    QuantityComparator.GreaterOrEqual => ">=",
                    _ => string.Empty,
                };
                Span<char> scratch = stackalloc char[160];
                comp.AsSpan().CopyTo(scratch);
                var pos = comp.Length;
                if (q.Value.TryFormat(scratch[pos..], out var valLen, default, CultureInfo.InvariantCulture)
                    && pos + valLen + 1 + q.Unit.Length <= scratch.Length)
                {
                    pos += valLen;
                    scratch[pos++] = ' ';
                    q.Unit.AsSpan().CopyTo(scratch[pos..]);
                    pos += q.Unit.Length;
                    writer.WriteString(propertyName, scratch[..pos]);
                    return;
                }

                writer.WriteString(propertyName, q.ToDisplayString());
                return;
            case CodedValue coded:
                writer.WriteString(propertyName, coded.Code);
                return;
            case TextValue text:
                writer.WriteString(propertyName, text.Value);
                return;
            case BooleanValue boolean:
                writer.WriteString(propertyName, boolean.Value ? "true"u8 : "false"u8);
                return;
            case DateTimeValue dateTime:
                writer.WriteString(propertyName, dateTime.Value);
                return;
            case MissingValue { Detail: null } missing:
                writer.WriteString(propertyName, missing.Reason.ToString());
                return;
            default:
                writer.WriteString(propertyName, value.ToDisplayString());
                return;
        }
    }

    private sealed class CollectedItemKeyComparer : IComparer<KeyValuePair<string, SimulationCollectedItemSnapshot>>
    {
        public static readonly CollectedItemKeyComparer Instance = new();

        public int Compare(KeyValuePair<string, SimulationCollectedItemSnapshot> x, KeyValuePair<string, SimulationCollectedItemSnapshot> y) =>
            string.CompareOrdinal(x.Key, y.Key);
    }
}

/// <summary>
/// Pre-indexed protocol blinding markers built once per snapshot masking or validation call so observation, item, and
/// finding checks run in constant time without rescanning <see cref="ProtocolDesign"/> trees.
/// </summary>
internal sealed class ProtocolBlindingIndex
{
    private readonly HashSet<ConceptId> _interventionProductConcepts;
    private readonly HashSet<ConceptId> _blindedRawConcepts;
    private readonly HashSet<string> _blindedDesignSpecIds;
    private readonly HashSet<ConceptId> _conceptsInBlindedSpecs;
    private readonly HashSet<(string SpecId, string ItemId)> _blindedSpecItemPairs;
    private readonly HashSet<(string SpecId, ConceptId ConceptId)> _blindedSpecConceptPairs;
    private readonly HashSet<string> _blindedAnySpecItemIds;
    private readonly HashSet<ConceptId> _blindedAnySpecItemConcepts;
    private readonly HashSet<(string SpecId, string ItemId)> _blindedVariablesBySpecAndItem;
    private readonly HashSet<string> _blindedVariablesAnySpecByItem;
    private readonly Dictionary<string, bool> _markerMemo = new(StringComparer.Ordinal);

    private ProtocolBlindingIndex(
        HashSet<ConceptId> interventionProductConcepts,
        HashSet<ConceptId> blindedRawConcepts,
        HashSet<string> blindedDesignSpecIds,
        HashSet<ConceptId> conceptsInBlindedSpecs,
        HashSet<(string SpecId, string ItemId)> blindedSpecItemPairs,
        HashSet<(string SpecId, ConceptId ConceptId)> blindedSpecConceptPairs,
        HashSet<string> blindedAnySpecItemIds,
        HashSet<ConceptId> blindedAnySpecItemConcepts,
        HashSet<(string SpecId, string ItemId)> blindedVariablesBySpecAndItem,
        HashSet<string> blindedVariablesAnySpecByItem)
    {
        _interventionProductConcepts = interventionProductConcepts;
        _blindedRawConcepts = blindedRawConcepts;
        _blindedDesignSpecIds = blindedDesignSpecIds;
        _conceptsInBlindedSpecs = conceptsInBlindedSpecs;
        _blindedSpecItemPairs = blindedSpecItemPairs;
        _blindedSpecConceptPairs = blindedSpecConceptPairs;
        _blindedAnySpecItemIds = blindedAnySpecItemIds;
        _blindedAnySpecItemConcepts = blindedAnySpecItemConcepts;
        _blindedVariablesBySpecAndItem = blindedVariablesBySpecAndItem;
        _blindedVariablesAnySpecByItem = blindedVariablesAnySpecByItem;
    }

    internal sealed record IndexedDesignSpec(
        CollectionSpec Spec,
        Dictionary<string, CollectionItem> ItemsById);

    public static Dictionary<string, IndexedDesignSpec> IndexFirstSpecsById(IReadOnlyList<ProtocolDesign> designs)
    {
        var map = new Dictionary<string, IndexedDesignSpec>(StringComparer.Ordinal);
        for (var d = 0; d < designs.Count; d++)
        {
            var specs = designs[d].CollectionSpecs;
            for (var s = 0; s < specs.Count; s++)
            {
                var spec = specs[s];
                if (map.ContainsKey(spec.Id))
                {
                    continue;
                }

                var itemsById = new Dictionary<string, CollectionItem>(spec.Items.Count, StringComparer.Ordinal);
                for (var i = 0; i < spec.Items.Count; i++)
                {
                    var item = spec.Items[i];
                    itemsById.TryAdd(item.Id, item);
                }

                map.Add(spec.Id, new IndexedDesignSpec(spec, itemsById));
            }
        }

        return map;
    }

    public static ProtocolBlindingIndex Create(
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> concepts)
    {
        var interventionProductConcepts = new HashSet<ConceptId>();
        var blindedRawConcepts = new HashSet<ConceptId>();
        var blindedDesignSpecIds = new HashSet<string>(StringComparer.Ordinal);
        var conceptsInBlindedSpecs = new HashSet<ConceptId>();
        var blindedSpecItemPairs = new HashSet<(string SpecId, string ItemId)>();
        var blindedSpecConceptPairs = new HashSet<(string SpecId, ConceptId ConceptId)>();
        var blindedAnySpecItemIds = new HashSet<string>(StringComparer.Ordinal);
        var blindedAnySpecItemConcepts = new HashSet<ConceptId>();
        var blindedVariablesBySpecAndItem = new HashSet<(string SpecId, string ItemId)>();
        var blindedVariablesAnySpecByItem = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (conceptId, meta) in concepts)
        {
            if (MatchesConceptMarkers(meta))
            {
                blindedRawConcepts.Add(conceptId);
            }
        }

        for (var d = 0; d < designs.Count; d++)
        {
            var design = designs[d];
            var designInterventions = new HashSet<ConceptId>();
            for (var i = 0; i < design.Interventions.Count; i++)
            {
                if (design.Interventions[i].Product is { } product)
                {
                    interventionProductConcepts.Add(product.ConceptId);
                    designInterventions.Add(product.ConceptId);
                }
            }

            for (var s = 0; s < design.CollectionSpecs.Count; s++)
            {
                var spec = design.CollectionSpecs[s];
                var specBlinded = MatchesSpecMarkers(spec);
                if (specBlinded)
                {
                    blindedDesignSpecIds.Add(spec.Id);
                }

                var seenSpecItems = new HashSet<string>(spec.Items.Count, StringComparer.Ordinal);
                for (var i = 0; i < spec.Items.Count; i++)
                {
                    var item = spec.Items[i];
                    var itemConceptId = item.Concept.ConceptId;
                    if (specBlinded)
                    {
                        conceptsInBlindedSpecs.Add(itemConceptId);
                    }

                    var itemBlinded = MatchesItemMarkers(item);
                    if (itemBlinded)
                    {
                        blindedSpecItemPairs.Add((spec.Id, item.Id));
                        blindedSpecConceptPairs.Add((spec.Id, itemConceptId));
                        blindedAnySpecItemIds.Add(item.Id);
                        blindedAnySpecItemConcepts.Add(itemConceptId);
                    }

                    if (seenSpecItems.Add(item.Id)
                        && (specBlinded
                            || itemBlinded
                            || designInterventions.Contains(itemConceptId)
                            || blindedRawConcepts.Contains(itemConceptId)))
                    {
                        blindedVariablesBySpecAndItem.Add((spec.Id, item.Id));
                        blindedVariablesAnySpecByItem.Add(item.Id);
                    }
                }
            }
        }

        return new ProtocolBlindingIndex(
            interventionProductConcepts,
            blindedRawConcepts,
            blindedDesignSpecIds,
            conceptsInBlindedSpecs,
            blindedSpecItemPairs,
            blindedSpecConceptPairs,
            blindedAnySpecItemIds,
            blindedAnySpecItemConcepts,
            blindedVariablesBySpecAndItem,
            blindedVariablesAnySpecByItem);
    }

    public bool IsBlindedObservation(
        ConceptId conceptId,
        ObservationValue? value,
        ConceptId metadataConceptId,
        string? collectionSpecId,
        string? itemId)
    {
        if (value is MissingValue { Reason: NullFlavor.Masked })
        {
            return true;
        }

        if (MatchesMarkersCached(collectionSpecId) || MatchesMarkersCached(itemId))
        {
            return true;
        }

        if (_interventionProductConcepts.Contains(conceptId))
        {
            return true;
        }

        if (collectionSpecId is not null)
        {
            if (_blindedDesignSpecIds.Contains(collectionSpecId))
            {
                return true;
            }

            var itemMatch = itemId is not null
                ? _blindedSpecItemPairs.Contains((collectionSpecId, itemId))
                : _blindedSpecConceptPairs.Contains((collectionSpecId, conceptId));
            if (itemMatch)
            {
                return true;
            }
        }
        else
        {
            if (_conceptsInBlindedSpecs.Contains(conceptId))
            {
                return true;
            }

            var anySpecItemMatch = itemId is not null
                ? _blindedAnySpecItemIds.Contains(itemId)
                : _blindedAnySpecItemConcepts.Contains(conceptId);
            if (anySpecItemMatch)
            {
                return true;
            }
        }

        return _blindedRawConcepts.Contains(metadataConceptId);
    }

    public bool IsBlindedCollectionSpec(string? collectionSpecId, string? collectionTitle = null) =>
        MatchesMarkersCached(collectionSpecId)
        || MatchesMarkersCached(collectionTitle)
        || (!string.IsNullOrWhiteSpace(collectionSpecId) && _blindedDesignSpecIds.Contains(collectionSpecId));

    public bool IsBlindedCollectionItem(
        string? collectionSpecId,
        string? collectionTitle,
        CollectionItem? item,
        ObservationValue? value,
        ConceptId valueConceptId)
    {
        if (value is MissingValue { Reason: NullFlavor.Masked })
        {
            return true;
        }

        if (IsBlindedCollectionSpec(collectionSpecId, collectionTitle))
        {
            return true;
        }

        if (item is not null)
        {
            if (MatchesItemMarkersCached(item))
            {
                return true;
            }

            return IsBlindedObservation(item.Concept.ConceptId, value, valueConceptId, collectionSpecId, item.Id);
        }

        return _blindedRawConcepts.Contains(valueConceptId);
    }

    public bool IsBlindedViolation(RuleViolation violation)
    {
        if (IsBlindedCollectionSpec(violation.Dataset))
        {
            return true;
        }

        var observed = violation.Observed;
        if (observed.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < observed.Count; i++)
        {
            if (IsBlindedVariable(violation.Dataset, observed[i].Variable))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsBlindedVariable(string? defaultDataset, string? variable)
    {
        if (MatchesMarkersCached(defaultDataset) || MatchesMarkersCached(variable))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(variable))
        {
            return false;
        }

        string? targetSpec = defaultDataset;
        var targetItem = variable;
        var dot = variable.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot < variable.Length - 1)
        {
            targetSpec = variable[..dot];
            targetItem = variable[(dot + 1)..];
            if (MatchesMarkersCached(targetSpec) || MatchesMarkersCached(targetItem))
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(targetSpec)
            ? _blindedVariablesBySpecAndItem.Contains((targetSpec, targetItem))
            : _blindedVariablesAnySpecByItem.Contains(targetItem);
    }

    private bool MatchesItemMarkersCached(CollectionItem item) =>
        MatchesMarkersCached(item.Id)
        || MatchesMarkersCached(item.Purpose?.OperationalNeed)
        || MatchesMarkersCached(item.Prompt);

    private bool MatchesMarkersCached(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!_markerMemo.TryGetValue(text, out var matched))
        {
            matched = BlindedTreatmentClassifier.MatchesMarkers(text);
            _markerMemo[text] = matched;
        }

        return matched;
    }

    private static bool MatchesSpecMarkers(CollectionSpec spec) =>
        BlindedTreatmentClassifier.MatchesMarkers(spec.Id)
        || BlindedTreatmentClassifier.MatchesMarkers(spec.Name)
        || BlindedTreatmentClassifier.MatchesMarkers(spec.Purpose?.OperationalNeed);

    private static bool MatchesItemMarkers(CollectionItem item) =>
        BlindedTreatmentClassifier.MatchesMarkers(item.Id)
        || BlindedTreatmentClassifier.MatchesMarkers(item.Purpose?.OperationalNeed)
        || BlindedTreatmentClassifier.MatchesMarkers(item.Prompt);

    private static bool MatchesConceptMarkers(BlindedConceptMetadata concept) =>
        BlindedTreatmentClassifier.MatchesMarkers(concept.ExternalCode)
        || BlindedTreatmentClassifier.MatchesMarkers(concept.PreferredLabel)
        || BlindedTreatmentClassifier.MatchesMarkers(concept.Definition);
}

/// <summary>
/// SOH-bounded stable ordering helpers that stream items in <see cref="Guid"/> or ordinal key order without ever
/// allocating buffers on the Large Object Heap (<c>&gt;= 85,000</c> bytes).
/// </summary>
internal static class ZeroLohOrdering
{
    public const int MaxSohChunkLength = 1024;

    public static void ForEachOrderedByGuid<TItem, TState>(
        IReadOnlyList<TItem> items,
        Func<TItem, Guid> getId,
        TState state,
        Action<TState, TItem> visit)
    {
        var count = items.Count;
        if (count == 0)
        {
            return;
        }

        if (count == 1 || IsSortedByGuid(items, getId))
        {
            for (var i = 0; i < count; i++)
            {
                visit(state, items[i]);
            }

            return;
        }

        if (count <= MaxSohChunkLength)
        {
            var singleChunk = BuildGuidChunk(items, 0, count, getId);
            for (var i = 0; i < singleChunk.Length; i++)
            {
                visit(state, singleChunk[i].Value);
            }

            return;
        }

        var chunkCount = (count + MaxSohChunkLength - 1) / MaxSohChunkLength;
        var chunks = new IndexedGuidItem<TItem>[chunkCount][];
        for (var c = 0; c < chunkCount; c++)
        {
            var start = c * MaxSohChunkLength;
            var length = Math.Min(MaxSohChunkLength, count - start);
            chunks[c] = BuildGuidChunk(items, start, length, getId);
        }

        MergeGuidChunks(chunks, state, visit);
    }

    public static void ForEachOrderedByKey<TItem, TState>(
        IEnumerable<TItem> items,
        Func<TItem, string> getKey,
        TState state,
        Action<TState, TItem> visit)
    {
        var chunks = new List<IndexedKeyItem<TItem>[]>();
        var current = new IndexedKeyItem<TItem>[MaxSohChunkLength];
        var currentCount = 0;
        var order = 0;

        foreach (var item in items)
        {
            current[currentCount++] = new IndexedKeyItem<TItem>(getKey(item), order++, item);
            if (currentCount == MaxSohChunkLength)
            {
                Array.Sort(current, 0, currentCount, IndexedKeyItemComparer<TItem>.Instance);
                chunks.Add(current);
                current = new IndexedKeyItem<TItem>[MaxSohChunkLength];
                currentCount = 0;
            }
        }

        if (currentCount > 0)
        {
            var tail = new IndexedKeyItem<TItem>[currentCount];
            Array.Copy(current, tail, currentCount);
            Array.Sort(tail, 0, currentCount, IndexedKeyItemComparer<TItem>.Instance);
            chunks.Add(tail);
        }

        if (chunks.Count == 0)
        {
            return;
        }

        if (chunks.Count == 1)
        {
            var only = chunks[0];
            for (var i = 0; i < only.Length; i++)
            {
                visit(state, only[i].Value);
            }

            return;
        }

        MergeKeyChunks([.. chunks], state, visit);
    }

    private static bool IsSortedByGuid<TItem>(IReadOnlyList<TItem> items, Func<TItem, Guid> getId)
    {
        var previous = getId(items[0]);
        for (var i = 1; i < items.Count; i++)
        {
            var current = getId(items[i]);
            if (previous.CompareTo(current) > 0)
            {
                return false;
            }

            previous = current;
        }

        return true;
    }

    private static IndexedGuidItem<TItem>[] BuildGuidChunk<TItem>(
        IReadOnlyList<TItem> items,
        int start,
        int length,
        Func<TItem, Guid> getId)
    {
        var chunk = new IndexedGuidItem<TItem>[length];
        for (var i = 0; i < length; i++)
        {
            var index = start + i;
            var item = items[index];
            chunk[i] = new IndexedGuidItem<TItem>(getId(item), index, item);
        }

        Array.Sort(chunk, 0, length, IndexedGuidItemComparer<TItem>.Instance);
        return chunk;
    }

    private static void MergeGuidChunks<TItem, TState>(
        IndexedGuidItem<TItem>[][] chunks,
        TState state,
        Action<TState, TItem> visit)
    {
        var chunkCount = chunks.Length;
        var positions = new int[chunkCount];
        var heap = new int[chunkCount];
        var heapSize = 0;

        for (var c = 0; c < chunkCount; c++)
        {
            if (chunks[c].Length > 0)
            {
                heap[heapSize] = c;
                SiftUpGuid(heap, heapSize, chunks, positions);
                heapSize++;
            }
        }

        while (heapSize > 0)
        {
            var topChunk = heap[0];
            var topPos = positions[topChunk];
            visit(state, chunks[topChunk][topPos].Value);

            topPos++;
            positions[topChunk] = topPos;
            if (topPos < chunks[topChunk].Length)
            {
                SiftDownGuid(heap, heapSize, 0, chunks, positions);
            }
            else
            {
                heapSize--;
                if (heapSize > 0)
                {
                    heap[0] = heap[heapSize];
                    SiftDownGuid(heap, heapSize, 0, chunks, positions);
                }
            }
        }
    }

    private static void SiftUpGuid<TItem>(
        int[] heap,
        int index,
        IndexedGuidItem<TItem>[][] chunks,
        int[] positions)
    {
        while (index > 0)
        {
            var parent = (index - 1) >> 1;
            var childChunk = heap[index];
            var parentChunk = heap[parent];
            if (CompareGuidEntry(chunks[childChunk][positions[childChunk]], chunks[parentChunk][positions[parentChunk]]) >= 0)
            {
                break;
            }

            heap[index] = parentChunk;
            heap[parent] = childChunk;
            index = parent;
        }
    }

    private static void SiftDownGuid<TItem>(
        int[] heap,
        int heapSize,
        int index,
        IndexedGuidItem<TItem>[][] chunks,
        int[] positions)
    {
        while (true)
        {
            var left = (index << 1) + 1;
            if (left >= heapSize)
            {
                return;
            }

            var smallest = index;
            if (CompareGuidEntry(chunks[heap[left]][positions[heap[left]]], chunks[heap[smallest]][positions[heap[smallest]]]) < 0)
            {
                smallest = left;
            }

            var right = left + 1;
            if (right < heapSize
                && CompareGuidEntry(chunks[heap[right]][positions[heap[right]]], chunks[heap[smallest]][positions[heap[smallest]]]) < 0)
            {
                smallest = right;
            }

            if (smallest == index)
            {
                return;
            }

            (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
            index = smallest;
        }
    }

    private static int CompareGuidEntry<TItem>(in IndexedGuidItem<TItem> x, in IndexedGuidItem<TItem> y)
    {
        var cmp = x.Id.CompareTo(y.Id);
        return cmp != 0 ? cmp : x.Order.CompareTo(y.Order);
    }

    private static void MergeKeyChunks<TItem, TState>(
        IndexedKeyItem<TItem>[][] chunks,
        TState state,
        Action<TState, TItem> visit)
    {
        var chunkCount = chunks.Length;
        var positions = new int[chunkCount];
        var heap = new int[chunkCount];
        var heapSize = 0;

        for (var c = 0; c < chunkCount; c++)
        {
            if (chunks[c].Length > 0)
            {
                heap[heapSize] = c;
                SiftUpKey(heap, heapSize, chunks, positions);
                heapSize++;
            }
        }

        while (heapSize > 0)
        {
            var topChunk = heap[0];
            var topPos = positions[topChunk];
            visit(state, chunks[topChunk][topPos].Value);

            topPos++;
            positions[topChunk] = topPos;
            if (topPos < chunks[topChunk].Length)
            {
                SiftDownKey(heap, heapSize, 0, chunks, positions);
            }
            else
            {
                heapSize--;
                if (heapSize > 0)
                {
                    heap[0] = heap[heapSize];
                    SiftDownKey(heap, heapSize, 0, chunks, positions);
                }
            }
        }
    }

    private static void SiftUpKey<TItem>(
        int[] heap,
        int index,
        IndexedKeyItem<TItem>[][] chunks,
        int[] positions)
    {
        while (index > 0)
        {
            var parent = (index - 1) >> 1;
            if (CompareKeyEntry(chunks[heap[index]][positions[heap[index]]], chunks[heap[parent]][positions[heap[parent]]]) >= 0)
            {
                return;
            }

            (heap[index], heap[parent]) = (heap[parent], heap[index]);
            index = parent;
        }
    }

    private static void SiftDownKey<TItem>(
        int[] heap,
        int heapSize,
        int index,
        IndexedKeyItem<TItem>[][] chunks,
        int[] positions)
    {
        while (true)
        {
            var left = (index << 1) + 1;
            if (left >= heapSize)
            {
                return;
            }

            var smallest = index;
            if (CompareKeyEntry(chunks[heap[left]][positions[heap[left]]], chunks[heap[smallest]][positions[heap[smallest]]]) < 0)
            {
                smallest = left;
            }

            var right = left + 1;
            if (right < heapSize
                && CompareKeyEntry(chunks[heap[right]][positions[heap[right]]], chunks[heap[smallest]][positions[heap[smallest]]]) < 0)
            {
                smallest = right;
            }

            if (smallest == index)
            {
                return;
            }

            (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
            index = smallest;
        }
    }

    private static int CompareKeyEntry<TItem>(in IndexedKeyItem<TItem> x, in IndexedKeyItem<TItem> y)
    {
        var cmp = string.CompareOrdinal(x.Key, y.Key);
        return cmp != 0 ? cmp : x.Order.CompareTo(y.Order);
    }

    private readonly record struct IndexedGuidItem<TItem>(Guid Id, int Order, TItem Value);

    private readonly record struct IndexedKeyItem<TItem>(string Key, int Order, TItem Value);

    private sealed class IndexedGuidItemComparer<TItem> : IComparer<IndexedGuidItem<TItem>>
    {
        public static readonly IndexedGuidItemComparer<TItem> Instance = new();

        public int Compare(IndexedGuidItem<TItem> x, IndexedGuidItem<TItem> y) => CompareGuidEntry(x, y);
    }

    private sealed class IndexedKeyItemComparer<TItem> : IComparer<IndexedKeyItem<TItem>>
    {
        public static readonly IndexedKeyItemComparer<TItem> Instance = new();

        public int Compare(IndexedKeyItem<TItem> x, IndexedKeyItem<TItem> y) => CompareKeyEntry(x, y);
    }
}

/// <summary>
/// Pure evaluator of bitemporal active/pending site counts, total/affected participant counts, per-participant schedule
/// shifts, and re-consent cohorts (CLINICAL_CANVAS §4.2, K-PROT-2, K-TIME-7, ADR-2590).
/// </summary>
internal static class SimulationOperationalEvaluator
{
    public static readonly EngineVersion Engine = new("protocol.operational_impact", "1");

    public static OperationalImpactSummary Evaluate(
        MaskedSimulationSnapshot snapshot,
        SimulationAsOf asOf,
        ProtocolDesign? baseDesign,
        ProtocolDesign? candidateDesign,
        ProtocolAmendment? amendment,
        SoaAmendmentDiff? soaDiff,
        IReadOnlySet<EnrollmentId>? additionalImpactedEnrollments = null)
    {
        var (activeSites, pendingSites) = CountSites(snapshot.VisibleSites, snapshot.VisibleEffectivities, asOf);

        var asOfDate = DateOnly.FromDateTime(asOf.ValidAt.UtcDateTime);
        var completedActivitiesByEnrollment = snapshot.VisibleCollections
            .Where(c => !string.IsNullOrWhiteSpace(c.PlannedActivityId) && c.CollectedAt <= asOf.ValidAt && c.OpenedAt <= asOf.KnownAt)
            .GroupBy(c => c.EnrollmentId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlySet<string>)g.Select(c => c.PlannedActivityId!).ToHashSet(StringComparer.Ordinal));

        var hasEligibilityChanges = soaDiff?.ChangesEligibility is true;

        var hasBurdensomeScheduleChange = soaDiff?.Directives.Any(d =>
            d.Kind is SoaDirectiveKind.VisitAdded
                or SoaDirectiveKind.ActivityAdded
                or SoaDirectiveKind.ActivityConceptsChanged
            || (d.Kind == SoaDirectiveKind.ActivityFrequencyChanged
                && ((d.FrequencyAfter ?? 0) > (d.FrequencyBefore ?? 0) || d.CollectedAtAdded is { Count: > 0 }))) is true;

        var participantImpacts = new List<ParticipantSimulationImpact>(snapshot.VisibleEnrollments.Count);
        var affectedCount = 0;
        var reconsentCount = 0;

        foreach (var enrollment in snapshot.VisibleEnrollments
            .OrderBy(e => e.SubjectCode, StringComparer.Ordinal)
            .ThenBy(e => e.Id.Value))
        {
            var isTerminal = enrollment.StatusAsOf is EnrollmentStatus.ScreenFailed
                or EnrollmentStatus.Completed
                or EnrollmentStatus.Withdrawn;

            IReadOnlyList<ParticipantScheduleShift> shifts = [];
            if (!isTerminal && candidateDesign is not null)
            {
                var completed = completedActivitiesByEnrollment.TryGetValue(enrollment.Id, out var done)
                    ? done
                    : new HashSet<string>(StringComparer.Ordinal);
                shifts = ComputeParticipantScheduleShifts(
                    baseDesign,
                    candidateDesign,
                    enrollment.ReferenceStartDate,
                    asOfDate,
                    completed);
            }

            var hasDataOrRuleImpact = additionalImpactedEnrollments?.Contains(enrollment.Id) is true;
            var (cohort, requiresReconsent) = ClassifyCohort(
                enrollment.StatusAsOf,
                isTerminal,
                amendment?.TransitionPolicy,
                hasEligibilityChanges,
                hasBurdensomeScheduleChange,
                shifts.Count > 0,
                hasDataOrRuleImpact);

            if (cohort == ParticipantImpactCohort.CohortSplit && enrollment.StatusAsOf != EnrollmentStatus.Screening)
            {
                // Under CohortSplit, already-enrolled participants stay on the parent version without schedule shifts.
                shifts = [];
            }

            var isAffected = !isTerminal && (requiresReconsent
                || shifts.Count > 0
                || cohort == ParticipantImpactCohort.EligibilityReview
                || hasDataOrRuleImpact);

            if (isAffected)
            {
                affectedCount++;
            }

            if (requiresReconsent)
            {
                reconsentCount++;
            }

            var effectiveCohort = isAffected ? cohort : ParticipantImpactCohort.Unaffected;
            participantImpacts.Add(new ParticipantSimulationImpact(
                NodeRef.For(enrollment.Id),
                enrollment.SubjectCode,
                enrollment.StatusAsOf,
                effectiveCohort,
                requiresReconsent,
                shifts));
        }

        return new OperationalImpactSummary(
            activeSites,
            pendingSites,
            snapshot.VisibleEnrollments.Count,
            affectedCount,
            reconsentCount,
            participantImpacts,
            snapshot.Hidden);
    }

    private static (int ActiveSiteCount, int PendingSiteCount) CountSites(
        IReadOnlyList<SimulationSiteSnapshot> visibleSites,
        IReadOnlyList<SimulationEffectivitySnapshot> visibleEffectivities,
        SimulationAsOf asOf)
    {
        var active = 0;
        var pending = 0;
        var validAt = KernelTimestamps.ToStoredPrecision(asOf.ValidAt);
        var knownAt = KernelTimestamps.ToStoredPrecision(asOf.KnownAt);

        foreach (var site in visibleSites)
        {
            if (KernelTimestamps.ToStoredPrecision(site.CreatedAt) > knownAt
                || KernelTimestamps.ToStoredPrecision(site.CreatedAt) > validAt)
            {
                continue;
            }

            var isSiteActivated = site.IsActive && string.Equals(site.Status, "Active", StringComparison.OrdinalIgnoreCase);
            var isSiteSelectedOrPending = string.Equals(site.Status, "Selected", StringComparison.OrdinalIgnoreCase)
                || string.Equals(site.Status, "Pending", StringComparison.OrdinalIgnoreCase);

            var covering = visibleEffectivities
                .Where(e => e.Scope.Covers(site.Id, site.Country) && e.StatusAsOf is not null)
                .GroupBy(e => e.ProtocolVersionId)
                .Select(g => g.MaxBy(e => (int)e.Scope.Level)!.StatusAsOf!.Value)
                .ToList();

            if (covering.Count > 0)
            {
                if (covering.Contains(EffectivityStatus.Effective))
                {
                    if (isSiteActivated)
                    {
                        active++;
                    }
                    else if (isSiteSelectedOrPending)
                    {
                        pending++;
                    }
                }
                else if ((isSiteActivated || isSiteSelectedOrPending)
                    && covering.Any(s => s is EffectivityStatus.Pending
                        or EffectivityStatus.RegulatoryApproved
                        or EffectivityStatus.EthicsApproved))
                {
                    pending++;
                }

                continue;
            }

            if (isSiteActivated)
            {
                active++;
            }
            else if (site.IsActive && isSiteSelectedOrPending)
            {
                pending++;
            }
        }

        return (active, pending);
    }

    private static IReadOnlyList<ParticipantScheduleShift> ComputeParticipantScheduleShifts(
        ProtocolDesign? baseDesign,
        ProtocolDesign candidateDesign,
        DateOnly referenceStart,
        DateOnly asOfDate,
        IReadOnlySet<string> completedActivities)
    {
        var before = baseDesign is null ? [] : ExpectedSchedule.Compute(baseDesign, referenceStart, asOfDate);
        var after = ExpectedSchedule.Compute(candidateDesign, referenceStart, asOfDate);
        var beforeById = before.ToDictionary(v => v.Visit.ActivityId, StringComparer.Ordinal);
        var afterById = after.ToDictionary(v => v.Visit.ActivityId, StringComparer.Ordinal);

        var shifts = new List<ParticipantScheduleShift>();
        foreach (var id in beforeById.Keys.Union(afterById.Keys).Order(StringComparer.Ordinal))
        {
            if (completedActivities.Contains(id))
            {
                continue;
            }

            var old = beforeById.GetValueOrDefault(id);
            var @new = afterById.GetValueOrDefault(id);

            if (old is null && @new is not null)
            {
                if (@new.WindowEnd >= asOfDate)
                {
                    shifts.Add(new ParticipantScheduleShift(id, ElementChangeKind.Added, null, ToParticipantVisit(@new)));
                }
            }
            else if (old is not null && @new is null)
            {
                if (old.WindowEnd >= asOfDate)
                {
                    shifts.Add(new ParticipantScheduleShift(id, ElementChangeKind.Removed, ToParticipantVisit(old), null));
                }
            }
            else if (old is not null && @new is not null
                && (old.Target != @new.Target || old.WindowStart != @new.WindowStart || old.WindowEnd != @new.WindowEnd)
                && (old.WindowEnd >= asOfDate || @new.WindowEnd >= asOfDate))
            {
                shifts.Add(new ParticipantScheduleShift(id, ElementChangeKind.Changed, ToParticipantVisit(old), ToParticipantVisit(@new)));
            }
        }

        return shifts;
    }

    private static ParticipantPlannedVisit ToParticipantVisit(ExpectedVisit visit) =>
        new(visit.Visit.ActivityId, visit.Target, visit.WindowStart, visit.WindowEnd);

    private static (ParticipantImpactCohort Cohort, bool RequiresReconsent) ClassifyCohort(
        EnrollmentStatus status,
        bool isTerminal,
        TransitionPolicy? policy,
        bool hasEligibilityChanges,
        bool hasBurdensomeScheduleChange,
        bool hasScheduleShifts,
        bool hasDataOrRuleImpact)
    {
        if (isTerminal)
        {
            return (ParticipantImpactCohort.Unaffected, false);
        }

        if (policy == TransitionPolicy.RequiresReconsent)
        {
            return (ParticipantImpactCohort.RequiresReconsent, true);
        }

        if (status == EnrollmentStatus.Screening && hasEligibilityChanges)
        {
            return (ParticipantImpactCohort.EligibilityReview, false);
        }

        if (policy == TransitionPolicy.NewParticipantsOnly)
        {
            if (status == EnrollmentStatus.Screening && (hasScheduleShifts || hasDataOrRuleImpact))
            {
                return (ParticipantImpactCohort.CohortSplit, false);
            }

            return (ParticipantImpactCohort.Unaffected, false);
        }

        if (hasBurdensomeScheduleChange && hasScheduleShifts && policy is null)
        {
            return (ParticipantImpactCohort.RequiresReconsent, true);
        }

        if (policy == TransitionPolicy.ByCohort)
        {
            if (hasScheduleShifts || hasDataOrRuleImpact)
            {
                return (ParticipantImpactCohort.CohortSplit, false);
            }

            return (ParticipantImpactCohort.Unaffected, false);
        }

        if (hasScheduleShifts || hasDataOrRuleImpact)
        {
            return (ParticipantImpactCohort.AutomaticTransition, false);
        }

        return (ParticipantImpactCohort.Unaffected, false);
    }
}

/// <summary>
/// Pure evaluator of dataset <see cref="ProjectionDatasetChange"/>s via <see cref="IMappingEngine"/> and edit-check
/// <see cref="ValidationDeltaSummary"/> via <see cref="IRuleEvaluator"/> (ADR-2480 §4.1, K-PROJ-2, K-RULE-4, K-FIND-1).
/// </summary>
internal static class SimulationProjectionAndValidationEvaluator
{
    public static readonly EngineVersion MappingEngineVersion = new("standards.mapping", "1");
    public static readonly EngineVersion RuleEvaluatorVersion = new("rules.evaluator", "1");

    private const int MaxSamplesPerDataset = 10;

    public static (
        IReadOnlyList<ProjectionDatasetChange> Changes,
        IReadOnlyList<RuleRecord> BaselineProjectedRecords,
        IReadOnlyList<RuleRecord> CandidateProjectedRecords,
        HashSet<EnrollmentId> ImpactedEnrollments) EvaluateProjections(
        IMappingEngine mappingEngine,
        MaskedSimulationSnapshot snapshot,
        IReadOnlyList<MappingSpecification> baselineSpecs,
        IReadOnlyList<MappingSpecification> candidateSpecs,
        ProtocolDesign? baselineDesign,
        ProtocolDesign? candidateDesign)
    {
        var decoder = new SnapshotCodelistDecoder(snapshot.CodelistDecodes);
        if (CanShareProjections(baselineSpecs, candidateSpecs, baselineDesign, candidateDesign))
        {
            var sharedRecords = ProjectRuleRecordsSinglePass(
                mappingEngine,
                snapshot.VisibleObservations,
                baselineSpecs,
                baselineDesign,
                decoder);
            return ([], sharedRecords, sharedRecords, []);
        }

        var baselineProjected = ProjectObservations(mappingEngine, snapshot.VisibleObservations, baselineSpecs, baselineDesign, decoder);
        var candidateProjected = ProjectObservations(mappingEngine, snapshot.VisibleObservations, candidateSpecs, candidateDesign, decoder);

        var allKeys = baselineProjected.Keys.Union(candidateProjected.Keys).Order(StringComparer.Ordinal).ToList();
        var byDataset = new SortedDictionary<string, DatasetAccumulator>(StringComparer.Ordinal);
        var impactedEnrollments = new HashSet<EnrollmentId>();

        foreach (var key in allKeys)
        {
            var before = baselineProjected.GetValueOrDefault(key);
            var after = candidateProjected.GetValueOrDefault(key);
            if (before is null && after is not null)
            {
                GetDataset(byDataset, after.Dataset).RecordAdded(after.RecordKey, after.Variables);
                impactedEnrollments.Add(after.EnrollmentId);
            }
            else if (before is not null && after is null)
            {
                GetDataset(byDataset, before.Dataset).RecordRemoved(before.RecordKey, before.Variables);
                impactedEnrollments.Add(before.EnrollmentId);
            }
            else if (before is not null && after is not null)
            {
                if (!string.Equals(before.Dataset, after.Dataset, StringComparison.Ordinal))
                {
                    GetDataset(byDataset, before.Dataset).RecordRemoved(before.RecordKey, before.Variables);
                    GetDataset(byDataset, after.Dataset).RecordAdded(after.RecordKey, after.Variables);
                    impactedEnrollments.Add(after.EnrollmentId);
                }
                else if (!VariablesEqual(before.Variables, after.Variables))
                {
                    GetDataset(byDataset, after.Dataset).RecordChanged(after.RecordKey, before.Variables, after.Variables);
                    impactedEnrollments.Add(after.EnrollmentId);
                }
            }
        }

        var changes = byDataset
            .Where(kv => kv.Value.HasChanges)
            .Select(kv => kv.Value.ToSummary(kv.Key))
            .ToList();

        return (
            changes,
            [.. baselineProjected.Values.OrderBy(p => p.RecordKey, StringComparer.Ordinal).Select(ToRuleRecord)],
            [.. candidateProjected.Values.OrderBy(p => p.RecordKey, StringComparer.Ordinal).Select(ToRuleRecord)],
            impactedEnrollments);
    }

    public static (ValidationDeltaSummary Delta, HashSet<EnrollmentId> ImpactedEnrollments, bool MaskedBlindedFinding) EvaluateValidationDelta(
        IRuleEvaluator evaluator,
        IBlindedTreatmentClassifier blinding,
        MaskedSimulationSnapshot snapshot,
        DateOnly asOfDate,
        IReadOnlyList<RulePack> baselinePacks,
        IReadOnlyList<RulePack> candidatePacks,
        ProtocolDesign? baselineDesign,
        ProtocolDesign? candidateDesign,
        IReadOnlyList<RuleRecord> baselineProjectedRecords,
        IReadOnlyList<RuleRecord> candidateProjectedRecords)
    {
        if (baselinePacks.Count == 0 && candidatePacks.Count == 0)
        {
            return (new ValidationDeltaSummary([], [], 0), [], false);
        }

        var sameCollectionDesign = CollectionDesignsMatch(baselineDesign, candidateDesign);
        var sameRulePacks = RulePacksMatch(baselinePacks, candidatePacks);

        var baselineCollectionIndex = BuildCollectionIndex(snapshot, baselineDesign);
        var candidateCollectionIndex = sameCollectionDesign
            ? baselineCollectionIndex
            : BuildCollectionIndex(snapshot, candidateDesign);

        var enrollmentLookup = new LazyRecordEnrollmentLookup(
            baselineCollectionIndex,
            candidateCollectionIndex,
            snapshot.VisibleObservations);

        var allDesigns = snapshot.ProtocolDesigns.Values
            .Concat(new[] { baselineDesign, candidateDesign }.OfType<ProtocolDesign>())
            .ToList();
        var blindingIndex = blinding is BlindedTreatmentClassifier
            ? ProtocolBlindingIndex.Create(allDesigns, snapshot.Concepts)
            : null;

        BlindedConceptMetadata? ResolveConcept(ConceptId cid) =>
            snapshot.Concepts.TryGetValue(cid, out var meta) ? meta : null;

        bool IsRecordUnblinded(string recordKey)
        {
            if (enrollmentLookup.TryResolveEnrollment(recordKey, out var enrollmentId))
            {
                return snapshot.UnblindedEnrollments.Contains(enrollmentId);
            }

            return snapshot.CanUnblindStudy;
        }

        var baselineDesignRecords = BuildDesignRuleRecords(baselineDesign);
        var sameDesignRecords = DesignRecordsMatch(baselineDesign, candidateDesign);
        var candidateDesignRecords = sameDesignRecords ? baselineDesignRecords : BuildDesignRuleRecords(candidateDesign);

        var maskedBlindedFinding = false;
        var unchanged = 0;
        var (baselineFindings, candidateFindings) = EvaluateAllStages(
            evaluator,
            blinding,
            blindingIndex,
            allDesigns,
            ResolveConcept,
            IsRecordUnblinded,
            asOfDate,
            snapshot.DeclaredStandards,
            baselinePacks,
            candidatePacks,
            sameRulePacks,
            baselineCollectionIndex,
            candidateCollectionIndex,
            sameCollectionDesign,
            baselineProjectedRecords,
            candidateProjectedRecords,
            baselineDesignRecords,
            candidateDesignRecords,
            sameDesignRecords,
            ref unchanged,
            ref maskedBlindedFinding);

        var opening = new List<SimulatedValidationFinding>();
        var resolving = new List<SimulatedValidationFinding>();
        var impactedEnrollments = new HashSet<EnrollmentId>();

        foreach (var (key, finding) in candidateFindings.OrderBy(kv => kv.Key.RuleId, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.Stage)
                     .ThenBy(kv => kv.Key.Dataset, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.Subject, StringComparer.Ordinal))
        {
            if (baselineFindings.ContainsKey(key))
            {
                unchanged++;
            }
            else
            {
                opening.Add(finding);
                enrollmentLookup.TrackEnrollment(finding.Subject, impactedEnrollments);
            }
        }

        foreach (var (key, finding) in baselineFindings.OrderBy(kv => kv.Key.RuleId, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.Stage)
                     .ThenBy(kv => kv.Key.Dataset, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.Subject, StringComparer.Ordinal))
        {
            if (!candidateFindings.ContainsKey(key))
            {
                resolving.Add(finding);
                enrollmentLookup.TrackEnrollment(finding.Subject, impactedEnrollments);
            }
        }

        return (new ValidationDeltaSummary(opening, resolving, unchanged), impactedEnrollments, maskedBlindedFinding);
    }

    private static IReadOnlyList<RuleRecord> ProjectRuleRecordsSinglePass(
        IMappingEngine engine,
        IReadOnlyList<SimulationObservationSnapshot> observations,
        IReadOnlyList<MappingSpecification> specifications,
        ProtocolDesign? designOverlay,
        ICodelistDecoder decoder)
    {
        if (specifications.Count == 0 || observations.Count == 0)
        {
            return [];
        }

        var winningSpecByConcept = IndexWinningMappingSpecs(specifications);
        if (winningSpecByConcept.Count == 0)
        {
            return [];
        }

        var designBindings = designOverlay is not null ? IndexDesignConceptBindings(designOverlay) : null;
        var records = new List<RuleRecord>();
        var state = new SinglePassProjectionState(engine, winningSpecByConcept, designBindings, decoder, records);

        ZeroLohOrdering.ForEachOrderedByGuid(
            observations,
            static o => o.Id.Value,
            state,
            static (s, obs) =>
            {
                if (!TryMapObservation(obs, s.Engine, s.WinningSpecByConcept, s.DesignBindings, s.Decoder, out var mapped))
                {
                    return;
                }

                var obsKey = obs.Id.Value.ToString("D", CultureInfo.InvariantCulture);
                var ruleRecord = ToRuleRecordFromMapped(mapped.Dataset!, obsKey, mapped.Values);
                if (s.HasPrevious && s.PreviousObservationId == obs.Id)
                {
                    s.Records[^1] = ruleRecord;
                }
                else
                {
                    s.HasPrevious = true;
                    s.PreviousObservationId = obs.Id;
                    s.Records.Add(ruleRecord);
                }
            });

        return records;
    }

    private static Dictionary<string, ProjectedObservation> ProjectObservations(
        IMappingEngine engine,
        IReadOnlyList<SimulationObservationSnapshot> observations,
        IReadOnlyList<MappingSpecification> specifications,
        ProtocolDesign? designOverlay,
        ICodelistDecoder decoder)
    {
        var projected = new Dictionary<string, ProjectedObservation>(StringComparer.Ordinal);
        if (specifications.Count == 0 || observations.Count == 0)
        {
            return projected;
        }

        var winningSpecByConcept = IndexWinningMappingSpecs(specifications);
        if (winningSpecByConcept.Count == 0)
        {
            return projected;
        }

        var designBindings = designOverlay is not null ? IndexDesignConceptBindings(designOverlay) : null;
        var state = (Engine: engine, WinningSpecByConcept: winningSpecByConcept, DesignBindings: designBindings, Decoder: decoder, Projected: projected);

        ZeroLohOrdering.ForEachOrderedByGuid(
            observations,
            static o => o.Id.Value,
            state,
            static (s, obs) =>
            {
                if (!TryMapObservation(obs, s.Engine, s.WinningSpecByConcept, s.DesignBindings, s.Decoder, out var mapped))
                {
                    return;
                }

                var obsKey = obs.Id.Value.ToString("D", CultureInfo.InvariantCulture);
                var vars = new SortedDictionary<string, string?>(StringComparer.Ordinal);
                for (var i = 0; i < mapped.Values.Count; i++)
                {
                    var mv = mapped.Values[i];
                    vars[mv.Variable] = mv.Value;
                }

                s.Projected[obsKey] = new ProjectedObservation(obsKey, obs.EnrollmentId, mapped.Dataset!, vars);
            });

        return projected;
    }

    private static bool TryMapObservation(
        SimulationObservationSnapshot obs,
        IMappingEngine engine,
        Dictionary<ConceptId, MappingSpecification> winningSpecByConcept,
        Dictionary<string, Dictionary<string, ConceptRef>>? designBindings,
        ICodelistDecoder decoder,
        out MappingResult mapped)
    {
        var concept = obs.Concept;
        if (designBindings is not null
            && obs.CollectionSpecId is not null
            && obs.ItemId is not null
            && (!designBindings.TryGetValue(obs.CollectionSpecId, out var specItems)
                || !specItems.TryGetValue(obs.ItemId, out concept)))
        {
            mapped = default!;
            return false;
        }

        if (!winningSpecByConcept.TryGetValue(concept.ConceptId, out var winningSpec))
        {
            mapped = default!;
            return false;
        }

        var input = MappingInput.From(concept, obs.Value);
        mapped = engine.Map(winningSpec, input, decoder);
        return mapped.Succeeded && !string.IsNullOrWhiteSpace(mapped.Dataset) && mapped.Values.Count > 0;
    }

    private static Dictionary<ConceptId, MappingSpecification> IndexWinningMappingSpecs(IReadOnlyList<MappingSpecification> specifications)
    {
        var map = new Dictionary<ConceptId, MappingSpecification>();
        for (var s = 0; s < specifications.Count; s++)
        {
            var spec = specifications[s];
            for (var m = 0; m < spec.Mappings.Count; m++)
            {
                map[spec.Mappings[m].Concept.ConceptId] = spec;
            }
        }

        return map;
    }

    private static Dictionary<string, Dictionary<string, ConceptRef>> IndexDesignConceptBindings(ProtocolDesign design)
    {
        var bySpec = new Dictionary<string, Dictionary<string, ConceptRef>>(design.CollectionSpecs.Count, StringComparer.Ordinal);
        for (var s = 0; s < design.CollectionSpecs.Count; s++)
        {
            var spec = design.CollectionSpecs[s];
            if (!bySpec.TryGetValue(spec.Id, out var byItem))
            {
                byItem = new Dictionary<string, ConceptRef>(spec.Items.Count, StringComparer.Ordinal);
                bySpec.Add(spec.Id, byItem);
            }

            for (var i = 0; i < spec.Items.Count; i++)
            {
                var item = spec.Items[i];
                byItem[item.Id] = item.Concept;
            }
        }

        return bySpec;
    }

    private static SimulationCollectionIndex BuildCollectionIndex(
        MaskedSimulationSnapshot snapshot,
        ProtocolDesign? designOverlay)
    {
        var collections = snapshot.VisibleCollections;
        if (collections.Count == 0)
        {
            return SimulationCollectionIndex.Empty;
        }

        var overlaySpecsById = designOverlay is not null ? IndexSpecsById(designOverlay) : null;
        Dictionary<ProtocolVersionId, Dictionary<string, CollectionSpec>>? storedSpecsByVersion = null;

        CollectionSpec? ResolveSpec(SimulationCollectionSnapshot form)
        {
            if (overlaySpecsById is not null)
            {
                return overlaySpecsById.GetValueOrDefault(form.CollectionSpecId);
            }

            if (snapshot.ProtocolDesigns.Count == 0
                || !snapshot.ProtocolDesigns.TryGetValue(form.ProtocolVersionId, out var storedDesign))
            {
                return null;
            }

            storedSpecsByVersion ??= new Dictionary<ProtocolVersionId, Dictionary<string, CollectionSpec>>(snapshot.ProtocolDesigns.Count);
            if (!storedSpecsByVersion.TryGetValue(form.ProtocolVersionId, out var storedById))
            {
                storedById = IndexSpecsById(storedDesign);
                storedSpecsByVersion.Add(form.ProtocolVersionId, storedById);
            }

            return storedById.GetValueOrDefault(form.CollectionSpecId);
        }

        var byEnrollment = new Dictionary<EnrollmentId, List<SimulationCollectionSnapshot>>();
        for (var i = 0; i < collections.Count; i++)
        {
            var form = collections[i];
            if (!byEnrollment.TryGetValue(form.EnrollmentId, out var group))
            {
                group = [];
                byEnrollment.Add(form.EnrollmentId, group);
            }

            group.Add(form);
        }

        var ownValuesByFormId = new Dictionary<CollectionInstanceId, Dictionary<string, RuleValue>>(collections.Count);
        var resolvedSpecByFormId = new Dictionary<CollectionInstanceId, CollectionSpec?>(collections.Count);
        var siblingIndexByEnrollment = new Dictionary<EnrollmentId, CollectionValidationService.EnrollmentSiblingIndex>(byEnrollment.Count);

        foreach (var (enrollmentId, enrollmentForms) in byEnrollment)
        {
            for (var i = 0; i < enrollmentForms.Count; i++)
            {
                var form = enrollmentForms[i];
                if (!ownValuesByFormId.ContainsKey(form.Id))
                {
                    var spec = ResolveSpec(form);
                    resolvedSpecByFormId.Add(form.Id, spec);
                    ownValuesByFormId.Add(form.Id, BuildSimulationOwnValues(form, spec));
                }
            }

            var latestBySpec = CollectionValidationService.SelectLatestFormsBySpec(
                enrollmentForms,
                static f => f.Id,
                static f => f.CollectionSpecId,
                static f => f.OpenedAt);
            siblingIndexByEnrollment.Add(
                enrollmentId,
                CollectionValidationService.BuildSiblingIndex(
                    latestBySpec,
                    static sibling => sibling.CollectionSpecId,
                    sibling => ownValuesByFormId[sibling.Id]));
        }

        var indexedForms = new List<IndexedSimulationForm>(collections.Count);
        var orderState = (
            DesignOverlay: designOverlay,
            ResolvedSpecs: resolvedSpecByFormId,
            OwnValues: ownValuesByFormId,
            Siblings: siblingIndexByEnrollment,
            Output: indexedForms);

        ZeroLohOrdering.ForEachOrderedByGuid(
            collections,
            static c => c.Id.Value,
            orderState,
            static (s, collection) =>
            {
                var spec = s.ResolvedSpecs[collection.Id];
                if (s.DesignOverlay is not null && spec is null)
                {
                    return;
                }

                s.Output.Add(new IndexedSimulationForm(
                    collection,
                    s.OwnValues[collection.Id],
                    s.Siblings[collection.EnrollmentId]));
            });

        return new SimulationCollectionIndex(indexedForms);
    }

    private static Dictionary<string, CollectionSpec> IndexSpecsById(ProtocolDesign design)
    {
        var map = new Dictionary<string, CollectionSpec>(design.CollectionSpecs.Count, StringComparer.Ordinal);
        for (var i = 0; i < design.CollectionSpecs.Count; i++)
        {
            var spec = design.CollectionSpecs[i];
            map.TryAdd(spec.Id, spec);
        }

        return map;
    }

    private static Dictionary<string, RuleValue> BuildSimulationOwnValues(
        SimulationCollectionSnapshot collection,
        CollectionSpec? spec)
    {
        var specItemCount = spec?.Items.Count ?? 0;
        var ownValues = new Dictionary<string, RuleValue>(specItemCount + collection.ValuesByItemId.Count, StringComparer.Ordinal);
        if (spec is not null)
        {
            for (var i = 0; i < spec.Items.Count; i++)
            {
                var itemId = spec.Items[i].Id;
                if (ownValues.ContainsKey(itemId))
                {
                    continue;
                }

                var raw = collection.ValuesByItemId.TryGetValue(itemId, out var entry) ? entry.Value : null;
                ownValues.Add(itemId, CollectionValidationService.ToRuleValue(raw));
            }
        }

        foreach (var (itemId, entry) in collection.ValuesByItemId)
        {
            if (!ownValues.ContainsKey(itemId))
            {
                ownValues.Add(itemId, CollectionValidationService.ToRuleValue(entry.Value));
            }
        }

        return ownValues;
    }

    private static IReadOnlyList<RuleRecord> BuildDesignRuleRecords(ProtocolDesign? design)
    {
        if (design is null)
        {
            return [];
        }

        var values = new Dictionary<string, RuleValue>(StringComparer.Ordinal)
        {
            ["title"] = RuleValue.FromText(design.Title),
            ["objective_count"] = RuleValue.FromNumber(design.Objectives.Count),
            ["endpoint_count"] = RuleValue.FromNumber(design.Endpoints.Count),
            ["planned_activity_count"] = RuleValue.FromNumber(design.PlannedActivities.Count),
            ["collection_spec_count"] = RuleValue.FromNumber(design.CollectionSpecs.Count),
        };
        return [new RuleRecord("protocol_design", "design", values)];
    }

    private static (
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> Baseline,
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> Candidate) EvaluateAllStages(
        IRuleEvaluator evaluator,
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded,
        DateOnly asOfDate,
        IReadOnlyList<(string Standard, string Version)> declaredStandards,
        IReadOnlyList<RulePack> baselinePacks,
        IReadOnlyList<RulePack> candidatePacks,
        bool sameRulePacks,
        SimulationCollectionIndex baselineCollectionIndex,
        SimulationCollectionIndex candidateCollectionIndex,
        bool sameCollectionDesign,
        IReadOnlyList<RuleRecord> baselineProjectedRecords,
        IReadOnlyList<RuleRecord> candidateProjectedRecords,
        IReadOnlyList<RuleRecord> baselineDesignRecords,
        IReadOnlyList<RuleRecord> candidateDesignRecords,
        bool sameDesignRecords,
        ref int unchangedCount,
        ref bool maskedBlindedFinding)
    {
        var baseline = new Dictionary<DeltaFindingKey, SimulatedValidationFinding>();
        var candidate = new Dictionary<DeltaFindingKey, SimulatedValidationFinding>();

        if (sameCollectionDesign && sameRulePacks)
        {
            unchangedCount += EvaluateUnchangedStage(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Collection,
                asOfDate,
                declaredStandards,
                baselinePacks,
                baselineCollectionIndex.CreateCursor(),
                ref maskedBlindedFinding);
        }
        else
        {
            EvaluateStagePair(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Collection,
                asOfDate,
                declaredStandards,
                baselinePacks,
                baselineCollectionIndex.CreateCursor(),
                candidatePacks,
                candidateCollectionIndex.CreateCursor(),
                baseline,
                candidate,
                ref maskedBlindedFinding);
        }

        if (ReferenceEquals(baselineProjectedRecords, candidateProjectedRecords) && sameRulePacks)
        {
            unchangedCount += EvaluateUnchangedStage(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Standardization,
                asOfDate,
                declaredStandards,
                baselinePacks,
                new RuleRecordListCursor(baselineProjectedRecords),
                ref maskedBlindedFinding);
        }
        else
        {
            EvaluateStagePair(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Standardization,
                asOfDate,
                declaredStandards,
                baselinePacks,
                new RuleRecordListCursor(baselineProjectedRecords),
                candidatePacks,
                new RuleRecordListCursor(candidateProjectedRecords),
                baseline,
                candidate,
                ref maskedBlindedFinding);
        }

        if (sameDesignRecords && sameRulePacks)
        {
            unchangedCount += EvaluateUnchangedStage(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Design,
                asOfDate,
                declaredStandards,
                baselinePacks,
                new RuleRecordListCursor(baselineDesignRecords),
                ref maskedBlindedFinding);
        }
        else
        {
            EvaluateStagePair(
                evaluator,
                blinding,
                blindingIndex,
                designs,
                resolveConcept,
                isRecordUnblinded,
                ValidationStage.Design,
                asOfDate,
                declaredStandards,
                baselinePacks,
                new RuleRecordListCursor(baselineDesignRecords),
                candidatePacks,
                new RuleRecordListCursor(candidateDesignRecords),
                baseline,
                candidate,
                ref maskedBlindedFinding);
        }

        return (baseline, candidate);
    }

    private static int EvaluateUnchangedStage(
        IRuleEvaluator evaluator,
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded,
        ValidationStage stage,
        DateOnly asOfDate,
        IReadOnlyList<(string Standard, string Version)> declaredStandards,
        IReadOnlyList<RulePack> packs,
        IResettableRuleRecordCursor cursor,
        ref bool maskedBlindedFinding)
    {
        var violations = AmendmentRegressionEngine.EvaluateStage(
            evaluator,
            stage,
            asOfDate,
            packs,
            cursor,
            declaredStandards);
        if (violations.Count == 0)
        {
            return 0;
        }

        var uniqueVisible = new HashSet<DeltaFindingKey>(violations.Count);
        for (var i = 0; i < violations.Count; i++)
        {
            var violation = violations[i];
            if (ShouldMaskViolation(violation, blinding, blindingIndex, designs, resolveConcept, isRecordUnblinded))
            {
                maskedBlindedFinding = true;
                continue;
            }

            uniqueVisible.Add(new DeltaFindingKey(violation.RuleId, stage, violation.Dataset, violation.RecordKey));
        }

        return uniqueVisible.Count;
    }

    private static void EvaluateStagePair(
        IRuleEvaluator evaluator,
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded,
        ValidationStage stage,
        DateOnly asOfDate,
        IReadOnlyList<(string Standard, string Version)> declaredStandards,
        IReadOnlyList<RulePack> baselinePacks,
        IResettableRuleRecordCursor baselineCursor,
        IReadOnlyList<RulePack> candidatePacks,
        IResettableRuleRecordCursor candidateCursor,
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> baselineSink,
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> candidateSink,
        ref bool maskedBlindedFinding)
    {
        var (baselineViolations, targetViolations) = AmendmentRegressionEngine.EvaluateStageDelta(
            evaluator,
            stage,
            asOfDate,
            baselinePacks,
            baselineCursor,
            candidatePacks,
            candidateCursor,
            declaredStandards);
        CollectStageViolations(blinding, blindingIndex, designs, resolveConcept, isRecordUnblinded, stage, baselineViolations, baselineSink, ref maskedBlindedFinding);
        CollectStageViolations(blinding, blindingIndex, designs, resolveConcept, isRecordUnblinded, stage, targetViolations, candidateSink, ref maskedBlindedFinding);
    }

    private static void CollectStageViolations(
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded,
        ValidationStage stage,
        IReadOnlyList<RuleViolation> violations,
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> sink,
        ref bool maskedBlindedFinding)
    {
        for (var i = 0; i < violations.Count; i++)
        {
            var violation = violations[i];
            if (ShouldMaskViolation(violation, blinding, blindingIndex, designs, resolveConcept, isRecordUnblinded))
            {
                maskedBlindedFinding = true;
                continue;
            }

            var key = new DeltaFindingKey(violation.RuleId, stage, violation.Dataset, violation.RecordKey);
            sink.TryAdd(
                key,
                new SimulatedValidationFinding(
                    violation.RuleId,
                    violation.RuleVersion,
                    violation.Severity,
                    stage,
                    violation.Dataset,
                    violation.RecordKey,
                    violation.Explanation));
        }
    }

    private static bool ShouldMaskViolation(
        RuleViolation violation,
        IBlindedTreatmentClassifier blinding,
        ProtocolBlindingIndex? blindingIndex,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded)
    {
        if (isRecordUnblinded(violation.RecordKey))
        {
            return false;
        }

        if (blindingIndex is not null)
        {
            return blindingIndex.IsBlindedViolation(violation);
        }

        var target = new FindingTarget(
            FindingTargetKind.DatasetRecord,
            violation.Dataset,
            violation.RecordKey,
            violation.Observed.FirstOrDefault()?.Variable);
        var observedVars = violation.Observed
            .Select(o => new ObservedVariable(o.Variable, o.Value.ToString()))
            .ToList();
        return blinding.IsBlindedFinding(target, observedVars, designs, resolveConcept);
    }

    private static bool CanShareProjections(
        IReadOnlyList<MappingSpecification> baselineSpecs,
        IReadOnlyList<MappingSpecification> candidateSpecs,
        ProtocolDesign? baselineDesign,
        ProtocolDesign? candidateDesign) =>
        MappingSpecsMatch(baselineSpecs, candidateSpecs)
        && DesignConceptBindingsMatch(baselineDesign, candidateDesign);

    private static bool MappingSpecsMatch(
        IReadOnlyList<MappingSpecification> left,
        IReadOnlyList<MappingSpecification> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!MappingSpecificationEqual(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MappingSpecificationEqual(MappingSpecification left, MappingSpecification right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (!string.Equals(left.Title, right.Title, StringComparison.Ordinal)
            || !string.Equals(left.Overrides, right.Overrides, StringComparison.Ordinal)
            || !left.ControlledTerminology.SequenceEqual(right.ControlledTerminology)
            || !left.Units.SequenceEqual(right.Units)
            || left.Mappings.Count != right.Mappings.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Mappings.Count; i++)
        {
            var lm = left.Mappings[i];
            var rm = right.Mappings[i];
            if (!string.Equals(lm.Id, rm.Id, StringComparison.Ordinal)
                || lm.Concept != rm.Concept
                || !string.Equals(lm.Dataset, rm.Dataset, StringComparison.Ordinal)
                || lm.Result != rm.Result
                || !string.Equals(lm.StandardUnit, rm.StandardUnit, StringComparison.Ordinal)
                || lm.Decimals != rm.Decimals
                || !string.Equals(lm.Codelist, rm.Codelist, StringComparison.Ordinal)
                || !lm.Conversions.SequenceEqual(rm.Conversions)
                || !StringDictionaryEqual(lm.Assign, rm.Assign))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StringDictionaryEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        ReferenceEquals(left, right)
        || (left.Count == right.Count
            && left.All(kv => right.TryGetValue(kv.Key, out var rv) && string.Equals(kv.Value, rv, StringComparison.Ordinal)));

    private static bool DesignConceptBindingsMatch(ProtocolDesign? left, ProtocolDesign? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.CollectionSpecs.Count != right.CollectionSpecs.Count)
        {
            return false;
        }

        var leftBindings = IndexDesignConceptBindings(left);
        var rightBindings = IndexDesignConceptBindings(right);
        if (leftBindings.Count != rightBindings.Count)
        {
            return false;
        }

        foreach (var (specId, leftItems) in leftBindings)
        {
            if (!rightBindings.TryGetValue(specId, out var rightItems)
                || leftItems.Count != rightItems.Count)
            {
                return false;
            }

            foreach (var (itemId, leftConcept) in leftItems)
            {
                if (!rightItems.TryGetValue(itemId, out var rightConcept) || leftConcept != rightConcept)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool CollectionDesignsMatch(ProtocolDesign? left, ProtocolDesign? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.CollectionSpecs.Count != right.CollectionSpecs.Count)
        {
            return false;
        }

        for (var s = 0; s < left.CollectionSpecs.Count; s++)
        {
            var ls = left.CollectionSpecs[s];
            var rs = right.CollectionSpecs[s];
            if (!string.Equals(ls.Id, rs.Id, StringComparison.Ordinal)
                || ls.Items.Count != rs.Items.Count)
            {
                return false;
            }

            for (var i = 0; i < ls.Items.Count; i++)
            {
                if (!string.Equals(ls.Items[i].Id, rs.Items[i].Id, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool DesignRecordsMatch(ProtocolDesign? left, ProtocolDesign? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.Title, right.Title, StringComparison.Ordinal)
            && left.Objectives.Count == right.Objectives.Count
            && left.Endpoints.Count == right.Endpoints.Count
            && left.PlannedActivities.Count == right.PlannedActivities.Count
            && left.CollectionSpecs.Count == right.CollectionSpecs.Count;
    }

    private static bool RulePacksMatch(IReadOnlyList<RulePack> left, IReadOnlyList<RulePack> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var p = 0; p < left.Count; p++)
        {
            var lp = left[p];
            var rp = right[p];
            if (ReferenceEquals(lp, rp))
            {
                continue;
            }

            if (lp.Authority != rp.Authority
                || !string.Equals(lp.Title, rp.Title, StringComparison.Ordinal)
                || lp.Rules.Count != rp.Rules.Count)
            {
                return false;
            }

            for (var r = 0; r < lp.Rules.Count; r++)
            {
                if (!RulesMatch(lp.Rules[r], rp.Rules[r]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool RulesMatch(Rule left, Rule right) =>
        ReferenceEquals(left, right)
        || (string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && left.Version == right.Version
            && left.Authority == right.Authority
            && left.Citation == right.Citation
            && left.Applicability == right.Applicability
            && left.Severity == right.Severity
            && string.Equals(left.Condition.ToString(), right.Condition.ToString(), StringComparison.Ordinal)
            && string.Equals(left.Explanation, right.Explanation, StringComparison.Ordinal)
            && string.Equals(left.RemediationGuidance, right.RemediationGuidance, StringComparison.Ordinal)
            && left.Effective == right.Effective);

    private static RuleRecord ToRuleRecord(ProjectedObservation projected)
    {
        var values = new Dictionary<string, RuleValue>(projected.Variables.Count, StringComparer.Ordinal);
        foreach (var (variable, raw) in projected.Variables)
        {
            values[variable] = ConvertProjectedVariableValue(raw);
        }

        return new RuleRecord(projected.Dataset, projected.RecordKey, values);
    }

    private static RuleRecord ToRuleRecordFromMapped(
        string dataset,
        string recordKey,
        IReadOnlyList<MappedValue> mappedValues)
    {
        var values = new Dictionary<string, RuleValue>(mappedValues.Count, StringComparer.Ordinal);
        for (var i = 0; i < mappedValues.Count; i++)
        {
            var mv = mappedValues[i];
            values[mv.Variable] = ConvertProjectedVariableValue(mv.Value);
        }

        return new RuleRecord(dataset, recordKey, values);
    }

    private static RuleValue ConvertProjectedVariableValue(string? raw)
    {
        if (raw is null)
        {
            return RuleValue.Null;
        }

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var numeric)
            ? RuleValue.FromNumber(numeric)
            : RuleValue.FromText(raw);
    }

    private static bool VariablesEqual(
        IReadOnlyDictionary<string, string?> left,
        IReadOnlyDictionary<string, string?> right) =>
        left.Count == right.Count
        && left.All(kv => right.TryGetValue(kv.Key, out var rv) && string.Equals(kv.Value, rv, StringComparison.Ordinal));

    private static DatasetAccumulator GetDataset(SortedDictionary<string, DatasetAccumulator> map, string dataset)
    {
        if (!map.TryGetValue(dataset, out var acc))
        {
            acc = new DatasetAccumulator();
            map[dataset] = acc;
        }

        return acc;
    }

    private readonly record struct DeltaFindingKey(
        string RuleId,
        ValidationStage Stage,
        string Dataset,
        string Subject);

    private sealed record ProjectedObservation(
        string RecordKey,
        EnrollmentId EnrollmentId,
        string Dataset,
        IReadOnlyDictionary<string, string?> Variables);

    private sealed class SinglePassProjectionState(
        IMappingEngine engine,
        Dictionary<ConceptId, MappingSpecification> winningSpecByConcept,
        Dictionary<string, Dictionary<string, ConceptRef>>? designBindings,
        ICodelistDecoder decoder,
        List<RuleRecord> records)
    {
        public IMappingEngine Engine { get; } = engine;

        public Dictionary<ConceptId, MappingSpecification> WinningSpecByConcept { get; } = winningSpecByConcept;

        public Dictionary<string, Dictionary<string, ConceptRef>>? DesignBindings { get; } = designBindings;

        public ICodelistDecoder Decoder { get; } = decoder;

        public List<RuleRecord> Records { get; } = records;

        public bool HasPrevious { get; set; }

        public ObservationId PreviousObservationId { get; set; }
    }

    private sealed class IndexedSimulationForm(
        SimulationCollectionSnapshot form,
        Dictionary<string, RuleValue> ownValues,
        CollectionValidationService.EnrollmentSiblingIndex siblings)
    {
        private string? _key;

        public SimulationCollectionSnapshot Form { get; } = form;

        public string Key => _key ??= Form.Id.Value.ToString("D", CultureInfo.InvariantCulture);

        public Dictionary<string, RuleValue> OwnValues { get; } = ownValues;

        public CollectionValidationService.EnrollmentSiblingIndex Siblings { get; } = siblings;
    }

    private sealed class SimulationCollectionIndex(IReadOnlyList<IndexedSimulationForm> forms)
    {
        public static readonly SimulationCollectionIndex Empty = new([]);

        public IReadOnlyList<IndexedSimulationForm> Forms { get; } = forms;

        public SimulationCollectionFormCursor CreateCursor() => new(Forms);
    }

    private sealed class SimulationCollectionFormCursor(IReadOnlyList<IndexedSimulationForm> forms) : IResettableRuleRecordCursor
    {
        private int _index = -1;
        private IndexedSimulationForm? _current;

        public bool IsEmpty => forms.Count == 0;

        public string Dataset => _current!.Form.CollectionSpecId;

        public string Key => _current!.Key;

        public RuleValue this[string name] =>
            _current!.Siblings.ResolveVariable(_current.Form.CollectionSpecId, _current.OwnValues, name);

        public void Reset()
        {
            _index = -1;
            _current = null;
        }

        public bool MoveNext()
        {
            var next = _index + 1;
            if (next < forms.Count)
            {
                _index = next;
                _current = forms[next];
                return true;
            }

            _index = forms.Count;
            _current = null;
            return false;
        }
    }

    private sealed class LazyRecordEnrollmentLookup(
        SimulationCollectionIndex baselineCollections,
        SimulationCollectionIndex candidateCollections,
        IReadOnlyList<SimulationObservationSnapshot> visibleObservations)
    {
        private const int ShardCount = 64;
        private const int ShardMask = ShardCount - 1;

        private Dictionary<Guid, EnrollmentId>[]? _collectionShards;
        private Dictionary<Guid, EnrollmentId>[]? _observationShards;

        public bool TryResolveEnrollment(string recordKey, out EnrollmentId enrollmentId)
        {
            if (!Guid.TryParseExact(recordKey, "D", out var id))
            {
                enrollmentId = default;
                return false;
            }

            var shard = id.GetHashCode() & ShardMask;
            var collections = EnsureCollectionShards();
            if (collections[shard] is { } collectionShard && collectionShard.TryGetValue(id, out enrollmentId))
            {
                return true;
            }

            var observations = EnsureObservationShards();
            if (observations[shard] is { } observationShard && observationShard.TryGetValue(id, out enrollmentId))
            {
                return true;
            }

            enrollmentId = default;
            return false;
        }

        public void TrackEnrollment(string subjectKey, HashSet<EnrollmentId> sink)
        {
            if (TryResolveEnrollment(subjectKey, out var enrollmentId))
            {
                sink.Add(enrollmentId);
            }
        }

        private Dictionary<Guid, EnrollmentId>?[] EnsureCollectionShards()
        {
            if (_collectionShards is not null)
            {
                return _collectionShards;
            }

            var shards = new Dictionary<Guid, EnrollmentId>[ShardCount];
            AddCollectionForms(shards, baselineCollections.Forms);
            if (!ReferenceEquals(baselineCollections, candidateCollections))
            {
                AddCollectionForms(shards, candidateCollections.Forms);
            }

            _collectionShards = shards;
            return shards;
        }

        private Dictionary<Guid, EnrollmentId>?[] EnsureObservationShards()
        {
            if (_observationShards is not null)
            {
                return _observationShards;
            }

            var shards = new Dictionary<Guid, EnrollmentId>[ShardCount];
            for (var i = 0; i < visibleObservations.Count; i++)
            {
                var obs = visibleObservations[i];
                var id = obs.Id.Value;
                var idx = id.GetHashCode() & ShardMask;
                var map = shards[idx];
                if (map is null)
                {
                    map = [];
                    shards[idx] = map;
                }

                map[id] = obs.EnrollmentId;
            }

            _observationShards = shards;
            return shards;
        }

        private static void AddCollectionForms(
            Dictionary<Guid, EnrollmentId>?[] shards,
            IReadOnlyList<IndexedSimulationForm> forms)
        {
            for (var i = 0; i < forms.Count; i++)
            {
                var form = forms[i].Form;
                var id = form.Id.Value;
                var idx = id.GetHashCode() & ShardMask;
                var map = shards[idx];
                if (map is null)
                {
                    map = [];
                    shards[idx] = map;
                }

                map[id] = form.EnrollmentId;
            }
        }
    }

    private sealed class SnapshotCodelistDecoder(IReadOnlyDictionary<(string Codelist, string Code), string> decodes) : ICodelistDecoder
    {
        public string? SubmissionValue(string codelist, string code) =>
            decodes.TryGetValue((codelist, code), out var submissionValue) ? submissionValue : null;
    }

    private sealed class DatasetAccumulator
    {
        private readonly List<ProjectionRecordChangeSample> _samples = [];
        private int _added;
        private int _removed;
        private int _changed;

        public bool HasChanges => _added > 0 || _removed > 0 || _changed > 0;

        public void RecordAdded(string recordKey, IReadOnlyDictionary<string, string?> after)
        {
            _added++;
            AddSample(new ProjectionRecordChangeSample(recordKey, ElementChangeKind.Added, null, after));
        }

        public void RecordRemoved(string recordKey, IReadOnlyDictionary<string, string?> before)
        {
            _removed++;
            AddSample(new ProjectionRecordChangeSample(recordKey, ElementChangeKind.Removed, before, null));
        }

        public void RecordChanged(
            string recordKey,
            IReadOnlyDictionary<string, string?> before,
            IReadOnlyDictionary<string, string?> after)
        {
            _changed++;
            AddSample(new ProjectionRecordChangeSample(recordKey, ElementChangeKind.Changed, before, after));
        }

        public ProjectionDatasetChange ToSummary(string dataset) =>
            new(dataset, _added, _removed, _changed, _samples);

        private void AddSample(ProjectionRecordChangeSample sample)
        {
            if (_samples.Count < MaxSamplesPerDataset)
            {
                _samples.Add(sample);
            }
        }
    }
}

/// <summary>
/// Deterministic structural diff over <see cref="MappingSpecification"/> and <see cref="RulePack"/> candidates
/// (ADR-2480 §4.1, K-SIM-3).
/// </summary>
internal static class SimulationSpecificationDiff
{
    public const string RootId = "$root";
    public static readonly EngineVersion MappingDiffEngine = new("standards.mapping_diff", "1");
    public static readonly EngineVersion RulePackDiffEngine = new("rules.pack_diff", "1");

    public static IReadOnlyList<ElementChange> CompareMappingSpecifications(
        MappingSpecification? baseSpec,
        MappingSpecification candidateSpec)
    {
        ArgumentNullException.ThrowIfNull(candidateSpec);
        var candidateJson = JsonNode.Parse(MappingSpecificationJson.ToJson(candidateSpec).GetRawText())!.AsObject();
        var baseJson = baseSpec is null
            ? null
            : JsonNode.Parse(MappingSpecificationJson.ToJson(baseSpec).GetRawText())!.AsObject();

        return CompareChildCollection(baseJson, candidateJson, "mappings", "MappingSpecification", "VariableMapping");
    }

    public static IReadOnlyList<ElementChange> CompareRulePacks(
        RulePack? basePack,
        RulePack candidatePack)
    {
        ArgumentNullException.ThrowIfNull(candidatePack);
        var candidateJson = JsonNode.Parse(RulePackJson.ToJson(candidatePack).GetRawText())!.AsObject();
        var baseJson = basePack is null
            ? null
            : JsonNode.Parse(RulePackJson.ToJson(basePack).GetRawText())!.AsObject();

        return CompareChildCollection(baseJson, candidateJson, "rules", "RulePack", "Rule");
    }

    private static IReadOnlyList<ElementChange> CompareChildCollection(
        JsonObject? baseRoot,
        JsonObject candidateRoot,
        string arrayPropertyName,
        string rootInstanceType,
        string childInstanceType)
    {
        var changes = new List<ElementChange>();
        var candidateChildren = IndexById(candidateRoot[arrayPropertyName] as JsonArray);

        if (baseRoot is null)
        {
            changes.Add(new ElementChange(RootId, rootInstanceType, ElementChangeKind.Added, []));
            foreach (var id in candidateChildren.Keys.Order(StringComparer.Ordinal))
            {
                changes.Add(new ElementChange(id, childInstanceType, ElementChangeKind.Added, []));
            }

            return [.. changes.OrderBy(c => c.ElementId, StringComparer.Ordinal)];
        }

        var baseChildren = IndexById(baseRoot[arrayPropertyName] as JsonArray);
        var rootPointers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var prop in baseRoot.Select(kv => kv.Key).Union(candidateRoot.Select(kv => kv.Key), StringComparer.Ordinal))
        {
            if (string.Equals(prop, arrayPropertyName, StringComparison.Ordinal))
            {
                var baseIds = baseChildren.Keys.ToList();
                var candIds = candidateChildren.Keys.ToList();
                if (!baseIds.SequenceEqual(candIds, StringComparer.Ordinal))
                {
                    rootPointers.Add($"/{EscapePointer(prop)}");
                }

                continue;
            }

            CollectDifferingPointers(baseRoot[prop], candidateRoot[prop], $"/{EscapePointer(prop)}", rootPointers);
        }

        if (rootPointers.Count > 0)
        {
            changes.Add(new ElementChange(RootId, rootInstanceType, ElementChangeKind.Changed, [.. rootPointers]));
        }

        foreach (var id in baseChildren.Keys.Union(candidateChildren.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var before = baseChildren.GetValueOrDefault(id);
            var after = candidateChildren.GetValueOrDefault(id);
            if (before is null && after is not null)
            {
                changes.Add(new ElementChange(id, childInstanceType, ElementChangeKind.Added, []));
            }
            else if (before is not null && after is null)
            {
                changes.Add(new ElementChange(id, childInstanceType, ElementChangeKind.Removed, []));
            }
            else if (before is not null && after is not null)
            {
                var pointers = new SortedSet<string>(StringComparer.Ordinal);
                CollectDifferingPointers(before, after, string.Empty, pointers);
                if (pointers.Count > 0)
                {
                    changes.Add(new ElementChange(id, childInstanceType, ElementChangeKind.Changed, [.. pointers]));
                }
            }
        }

        return [.. changes.OrderBy(c => c.ElementId, StringComparer.Ordinal)];
    }

    private static Dictionary<string, JsonObject> IndexById(JsonArray? array)
    {
        var map = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (array is null)
        {
            return map;
        }

        foreach (var node in array)
        {
            if (node is JsonObject obj && obj["id"]?.GetValue<string>() is { Length: > 0 } id)
            {
                map[id] = obj;
            }
        }

        return map;
    }

    private static void CollectDifferingPointers(
        JsonNode? left,
        JsonNode? right,
        string path,
        SortedSet<string> pointers)
    {
        if (JsonNode.DeepEquals(left, right))
        {
            return;
        }

        if (left is JsonObject leftObj && right is JsonObject rightObj)
        {
            foreach (var key in leftObj.Select(kv => kv.Key).Union(rightObj.Select(kv => kv.Key), StringComparer.Ordinal))
            {
                var childPath = $"{path}/{EscapePointer(key)}";
                CollectDifferingPointers(leftObj[key], rightObj[key], childPath, pointers);
            }

            return;
        }

        pointers.Add(path.Length == 0 ? "/" : path);
    }

    private static string EscapePointer(string token) =>
        token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
