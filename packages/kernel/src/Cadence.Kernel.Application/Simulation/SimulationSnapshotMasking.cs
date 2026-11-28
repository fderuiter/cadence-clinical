using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;

namespace Cadence.Kernel.Application.Simulation;

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

    internal static string ComputeVisibleDigest(
        IReadOnlyList<SimulationSiteSnapshot> sites,
        IReadOnlyList<SimulationEffectivitySnapshot> effectivities,
        IReadOnlyList<SimulationEnrollmentSnapshot> enrollments,
        IReadOnlyList<SimulationObservationSnapshot> observations,
        IReadOnlyList<SimulationCollectionSnapshot> collections) =>
        SimulationVisibleDigestWriter.ComputeVisibleDigest(sites, effectivities, enrollments, observations, collections);

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
}
