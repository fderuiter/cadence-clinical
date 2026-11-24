using System;
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
        SimulationAsOf? asOf = null)
    {
        var nodes = new List<NodeRef>();
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
            : await visibility.MaskAsync(tenant, actor, [.. nodes.Distinct()], cancellationToken).ConfigureAwait(false);

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
        var canUnblindStudy = await CanUnblindResourceAsync(NodeRef.For(studyId)).ConfigureAwait(false);

        var unblindByEnrollment = new Dictionary<EnrollmentId, bool>();
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

            if (obsMask is null)
            {
                BlindedConceptMetadata? conceptMeta = raw.Concepts.TryGetValue(obs.Concept.ConceptId, out var foundConcept)
                    ? foundConcept
                    : null;
                if (blinding.IsBlindedObservation(obs.Concept.ConceptId, obs.Value, conceptMeta, obs.CollectionSpecId, obs.ItemId, allDesigns))
                {
                    var canUnblind = await CanUnblindEnrollmentAsync(obs.EnrollmentId).ConfigureAwait(false);
                    if (!canUnblind)
                    {
                        obsMask = MaskReason.Blinded;
                    }
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
            if (collectionMask is null && blinding.IsBlindedCollectionSpec(collection.CollectionSpecId, designs: allDesigns))
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

            var matchingSpec = allDesigns
                .SelectMany(d => d.CollectionSpecs)
                .FirstOrDefault(s => string.Equals(s.Id, collection.CollectionSpecId, StringComparison.Ordinal));

            var filteredItems = new Dictionary<string, SimulationCollectedItemSnapshot>(StringComparer.Ordinal);
            foreach (var (itemId, itemValue) in collection.ValuesByItemId.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (maskedObservationIds.Contains(itemValue.ObservationId))
                {
                    continue;
                }

                var specItem = matchingSpec?.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
                BlindedConceptMetadata? itemConceptMeta = raw.Concepts.TryGetValue(itemValue.Concept.ConceptId, out var foundMeta)
                    ? foundMeta
                    : null;
                if (blinding.IsBlindedCollectionItem(collection.CollectionSpecId, matchingSpec?.Name, specItem, itemValue.Value, itemConceptMeta, allDesigns))
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
        var digest = ComputeVisibleDigest(visibleSites, visibleEffectivities, visibleEnrollments, visibleObservations, visibleCollections);

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

    private static string ComputeVisibleDigest(
        IReadOnlyList<SimulationSiteSnapshot> sites,
        IReadOnlyList<SimulationEffectivitySnapshot> effectivities,
        IReadOnlyList<SimulationEnrollmentSnapshot> enrollments,
        IReadOnlyList<SimulationObservationSnapshot> observations,
        IReadOnlyList<SimulationCollectionSnapshot> collections)
    {
        var doc = new JsonObject
        {
            ["sites"] = new JsonArray([.. sites
                .OrderBy(s => s.Id.Value)
                .Select(s => (JsonNode?)new JsonObject
                {
                    ["id"] = s.Id.Value.ToString("D", CultureInfo.InvariantCulture),
                    ["country"] = s.Country,
                    ["status"] = s.Status,
                    ["active"] = s.IsActive,
                })]),
            ["effectivities"] = new JsonArray([.. effectivities
                .OrderBy(e => e.Id.Value)
                .Select(e => (JsonNode?)new JsonObject
                {
                    ["id"] = e.Id.Value.ToString("D", CultureInfo.InvariantCulture),
                    ["protocolVersion"] = e.ProtocolVersionId.Value.ToString("D", CultureInfo.InvariantCulture),
                    ["status"] = e.StatusAsOf?.ToString(),
                })]),
            ["enrollments"] = new JsonArray([.. enrollments
                .OrderBy(e => e.Id.Value)
                .Select(e => (JsonNode?)new JsonObject
                {
                    ["id"] = e.Id.Value.ToString("D", CultureInfo.InvariantCulture),
                    ["subjectCode"] = e.SubjectCode,
                    ["status"] = e.StatusAsOf.ToString(),
                    ["referenceStart"] = e.ReferenceStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                })]),
            ["observations"] = new JsonArray([.. observations
                .OrderBy(o => o.Id.Value)
                .Select(o => (JsonNode?)new JsonObject
                {
                    ["id"] = o.Id.Value.ToString("D", CultureInfo.InvariantCulture),
                    ["concept"] = o.Concept.ToString(),
                    ["value"] = o.Value.ToDisplayString(),
                })]),
            ["collections"] = new JsonArray([.. collections
                .OrderBy(c => c.Id.Value)
                .Select(c => (JsonNode?)new JsonObject
                {
                    ["id"] = c.Id.Value.ToString("D", CultureInfo.InvariantCulture),
                    ["spec"] = c.CollectionSpecId,
                    ["activity"] = c.PlannedActivityId,
                    ["items"] = new JsonArray([.. c.ValuesByItemId
                        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kv => (JsonNode?)new JsonObject
                        {
                            ["item"] = kv.Key,
                            ["value"] = kv.Value.Value.ToDisplayString(),
                        })]),
                })]),
        };
        return ContentHash.Compute(doc.ToJsonString());
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
        var (baselineCollectionRecords, enrollmentByCollectionKey) = BuildCollectionRuleRecords(snapshot, baselineDesign);
        var (candidateCollectionRecords, candidateEnrollmentByCollectionKey) = BuildCollectionRuleRecords(snapshot, candidateDesign);
        foreach (var kv in candidateEnrollmentByCollectionKey)
        {
            enrollmentByCollectionKey[kv.Key] = kv.Value;
        }

        var enrollmentByObservationKey = snapshot.VisibleObservations
            .ToDictionary(o => o.Id.Value.ToString("D", CultureInfo.InvariantCulture), o => o.EnrollmentId, StringComparer.Ordinal);

        var allDesigns = snapshot.ProtocolDesigns.Values
            .Concat(new[] { baselineDesign, candidateDesign }.OfType<ProtocolDesign>())
            .ToList();

        BlindedConceptMetadata? ResolveConcept(ConceptId cid) =>
            snapshot.Concepts.TryGetValue(cid, out var meta) ? meta : null;

        bool IsRecordUnblinded(string recordKey)
        {
            if (enrollmentByCollectionKey.TryGetValue(recordKey, out var fromCollection))
            {
                return snapshot.UnblindedEnrollments.Contains(fromCollection);
            }

            if (enrollmentByObservationKey.TryGetValue(recordKey, out var fromObs))
            {
                return snapshot.UnblindedEnrollments.Contains(fromObs);
            }

            return snapshot.CanUnblindStudy;
        }

        var baselineDesignRecords = BuildDesignRuleRecords(baselineDesign);
        var candidateDesignRecords = BuildDesignRuleRecords(candidateDesign);

        var maskedBlindedFinding = false;
        var (baselineFindings, candidateFindings) = EvaluateAllStages(
            evaluator,
            blinding,
            allDesigns,
            ResolveConcept,
            IsRecordUnblinded,
            asOfDate,
            snapshot.DeclaredStandards,
            baselinePacks,
            baselineCollectionRecords,
            baselineProjectedRecords,
            baselineDesignRecords,
            candidatePacks,
            candidateCollectionRecords,
            candidateProjectedRecords,
            candidateDesignRecords,
            ref maskedBlindedFinding);

        var opening = new List<SimulatedValidationFinding>();
        var resolving = new List<SimulatedValidationFinding>();
        var unchanged = 0;
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
                TrackEnrollment(finding.Subject, enrollmentByCollectionKey, enrollmentByObservationKey, impactedEnrollments);
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
                TrackEnrollment(finding.Subject, enrollmentByCollectionKey, enrollmentByObservationKey, impactedEnrollments);
            }
        }

        return (new ValidationDeltaSummary(opening, resolving, unchanged), impactedEnrollments, maskedBlindedFinding);
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

        var designItemsByKey = designOverlay?.CollectionSpecs
            .SelectMany(spec => spec.Items.Select(item => (Key: $"{spec.Id}::{item.Id}", Item: item)))
            .ToDictionary(x => x.Key, x => x.Item, StringComparer.Ordinal);
        var designSpecIds = designOverlay?.CollectionSpecs
            .Select(s => s.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var obs in observations.OrderBy(o => o.Id.Value))
        {
            var concept = obs.Concept;
            if (designOverlay is not null && obs.CollectionSpecId is not null && obs.ItemId is not null)
            {
                if (!designSpecIds!.Contains(obs.CollectionSpecId))
                {
                    continue;
                }

                if (!designItemsByKey!.TryGetValue($"{obs.CollectionSpecId}::{obs.ItemId}", out var designItem))
                {
                    continue;
                }

                concept = designItem.Concept;
            }

            var input = MappingInput.From(concept, obs.Value);
            MappingSpecification? winningSpec = null;
            foreach (var spec in specifications)
            {
                if (spec.For(concept.ConceptId) is not null)
                {
                    winningSpec = spec;
                }
            }

            if (winningSpec is null)
            {
                continue;
            }

            var mapped = engine.Map(winningSpec, input, decoder);
            if (!mapped.Succeeded || string.IsNullOrWhiteSpace(mapped.Dataset) || mapped.Values.Count == 0)
            {
                continue;
            }

            var obsKey = obs.Id.Value.ToString("D", CultureInfo.InvariantCulture);
            var vars = new SortedDictionary<string, string?>(StringComparer.Ordinal);
            foreach (var mv in mapped.Values)
            {
                vars[mv.Variable] = mv.Value;
            }

            projected[obsKey] = new ProjectedObservation(obsKey, obs.EnrollmentId, mapped.Dataset, vars);
        }

        return projected;
    }

    private static (IReadOnlyList<RuleRecord> Records, Dictionary<string, EnrollmentId> EnrollmentByRecordKey) BuildCollectionRuleRecords(
        MaskedSimulationSnapshot snapshot,
        ProtocolDesign? designOverlay)
    {
        var records = new List<RuleRecord>(snapshot.VisibleCollections.Count);
        var enrollmentMap = new Dictionary<string, EnrollmentId>(StringComparer.Ordinal);
        if (snapshot.VisibleCollections.Count == 0)
        {
            return (records, enrollmentMap);
        }

        var byEnrollment = snapshot.VisibleCollections
            .GroupBy(c => c.EnrollmentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var collection in snapshot.VisibleCollections.OrderBy(c => c.Id.Value))
        {
            var effectiveDesign = designOverlay;
            if (effectiveDesign is null && snapshot.ProtocolDesigns.TryGetValue(collection.ProtocolVersionId, out var storedDesign))
            {
                effectiveDesign = storedDesign;
            }

            CollectionSpec? spec = null;
            if (effectiveDesign is not null)
            {
                spec = effectiveDesign.CollectionSpecs.FirstOrDefault(s => string.Equals(s.Id, collection.CollectionSpecId, StringComparison.Ordinal));
                if (designOverlay is not null && spec is null)
                {
                    continue;
                }
            }

            var values = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
            var itemIds = (spec?.Items.Select(i => i.Id) ?? [])
                .Concat(collection.ValuesByItemId.Keys)
                .Distinct(StringComparer.Ordinal);

            foreach (var itemId in itemIds)
            {
                var raw = collection.ValuesByItemId.TryGetValue(itemId, out var entry) ? entry.Value : null;
                var converted = CollectionValidationService.ToRuleValue(raw);
                values[itemId] = converted;
                var qualified = $"{collection.CollectionSpecId}.{itemId}";
                if (qualified.Length <= FindingTarget.MaxNameLength)
                {
                    values[qualified] = converted;
                }
            }

            var siblings = byEnrollment[collection.EnrollmentId]
                .Where(s => s.Id != collection.Id && !string.Equals(s.CollectionSpecId, collection.CollectionSpecId, StringComparison.Ordinal))
                .GroupBy(s => s.CollectionSpecId, StringComparer.Ordinal)
                .Select(g => g.OrderBy(s => s.OpenedAt).ThenBy(s => s.Id.Value).Last());

            foreach (var sibling in siblings)
            {
                foreach (var (sibItem, sibVal) in sibling.ValuesByItemId)
                {
                    var converted = CollectionValidationService.ToRuleValue(sibVal.Value);
                    var qualified = $"{sibling.CollectionSpecId}.{sibItem}";
                    if (qualified.Length <= FindingTarget.MaxNameLength)
                    {
                        values[qualified] = converted;
                    }

                    if (!values.ContainsKey(sibItem) && sibItem.Length <= FindingTarget.MaxNameLength)
                    {
                        values[sibItem] = converted;
                    }
                }
            }

            var recordKey = collection.Id.Value.ToString("D", CultureInfo.InvariantCulture);
            enrollmentMap[recordKey] = collection.EnrollmentId;
            records.Add(new RuleRecord(collection.CollectionSpecId, recordKey, values));
        }

        return (records, enrollmentMap);
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
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded,
        DateOnly asOfDate,
        IReadOnlyList<(string Standard, string Version)> declaredStandards,
        IReadOnlyList<RulePack> baselinePacks,
        IReadOnlyList<RuleRecord> baselineCollectionRecords,
        IReadOnlyList<RuleRecord> baselineProjectedRecords,
        IReadOnlyList<RuleRecord> baselineDesignRecords,
        IReadOnlyList<RulePack> candidatePacks,
        IReadOnlyList<RuleRecord> candidateCollectionRecords,
        IReadOnlyList<RuleRecord> candidateProjectedRecords,
        IReadOnlyList<RuleRecord> candidateDesignRecords,
        ref bool maskedBlindedFinding)
    {
        var baseline = new Dictionary<DeltaFindingKey, SimulatedValidationFinding>();
        var candidate = new Dictionary<DeltaFindingKey, SimulatedValidationFinding>();
        if (baselinePacks.Count == 0 && candidatePacks.Count == 0)
        {
            return (baseline, candidate);
        }

        EvaluateStagePair(
            evaluator,
            blinding,
            designs,
            resolveConcept,
            isRecordUnblinded,
            ValidationStage.Collection,
            asOfDate,
            declaredStandards,
            baselinePacks,
            baselineCollectionRecords,
            candidatePacks,
            candidateCollectionRecords,
            baseline,
            candidate,
            ref maskedBlindedFinding);
        EvaluateStagePair(
            evaluator,
            blinding,
            designs,
            resolveConcept,
            isRecordUnblinded,
            ValidationStage.Standardization,
            asOfDate,
            declaredStandards,
            baselinePacks,
            baselineProjectedRecords,
            candidatePacks,
            candidateProjectedRecords,
            baseline,
            candidate,
            ref maskedBlindedFinding);
        EvaluateStagePair(
            evaluator,
            blinding,
            designs,
            resolveConcept,
            isRecordUnblinded,
            ValidationStage.Design,
            asOfDate,
            declaredStandards,
            baselinePacks,
            baselineDesignRecords,
            candidatePacks,
            candidateDesignRecords,
            baseline,
            candidate,
            ref maskedBlindedFinding);
        return (baseline, candidate);
    }

    private static void EvaluateStagePair(
        IRuleEvaluator evaluator,
        IBlindedTreatmentClassifier blinding,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded,
        ValidationStage stage,
        DateOnly asOfDate,
        IReadOnlyList<(string Standard, string Version)> declaredStandards,
        IReadOnlyList<RulePack> baselinePacks,
        IReadOnlyList<RuleRecord> baselineRecords,
        IReadOnlyList<RulePack> candidatePacks,
        IReadOnlyList<RuleRecord> candidateRecords,
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> baselineSink,
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> candidateSink,
        ref bool maskedBlindedFinding)
    {
        var (baselineViolations, targetViolations) = AmendmentRegressionEngine.EvaluateStageDelta(
            evaluator,
            stage,
            asOfDate,
            baselinePacks,
            baselineRecords,
            candidatePacks,
            candidateRecords,
            declaredStandards);
        CollectStageViolations(blinding, designs, resolveConcept, isRecordUnblinded, stage, baselineViolations, baselineSink, ref maskedBlindedFinding);
        CollectStageViolations(blinding, designs, resolveConcept, isRecordUnblinded, stage, targetViolations, candidateSink, ref maskedBlindedFinding);
    }

    private static void CollectStageViolations(
        IBlindedTreatmentClassifier blinding,
        IReadOnlyList<ProtocolDesign> designs,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        Func<string, bool> isRecordUnblinded,
        ValidationStage stage,
        IReadOnlyList<RuleViolation> violations,
        Dictionary<DeltaFindingKey, SimulatedValidationFinding> sink,
        ref bool maskedBlindedFinding)
    {
        foreach (var violation in violations)
        {
            if (!isRecordUnblinded(violation.RecordKey))
            {
                var target = new FindingTarget(
                    FindingTargetKind.DatasetRecord,
                    violation.Dataset,
                    violation.RecordKey,
                    violation.Observed.FirstOrDefault()?.Variable);
                var observedVars = violation.Observed
                    .Select(o => new ObservedVariable(o.Variable, o.Value.ToString()))
                    .ToList();
                if (blinding.IsBlindedFinding(target, observedVars, designs, resolveConcept))
                {
                    maskedBlindedFinding = true;
                    continue;
                }
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

    private static void TrackEnrollment(
        string subjectKey,
        IReadOnlyDictionary<string, EnrollmentId> byCollection,
        IReadOnlyDictionary<string, EnrollmentId> byObservation,
        HashSet<EnrollmentId> sink)
    {
        if (byCollection.TryGetValue(subjectKey, out var fromCollection))
        {
            sink.Add(fromCollection);
        }
        else if (byObservation.TryGetValue(subjectKey, out var fromObservation))
        {
            sink.Add(fromObservation);
        }
    }

    private static RuleRecord ToRuleRecord(ProjectedObservation projected)
    {
        var values = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        foreach (var (variable, raw) in projected.Variables)
        {
            if (raw is null)
            {
                values[variable] = RuleValue.Null;
            }
            else if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var numeric))
            {
                values[variable] = RuleValue.FromNumber(numeric);
            }
            else
            {
                values[variable] = RuleValue.FromText(raw);
            }
        }

        return new RuleRecord(projected.Dataset, projected.RecordKey, values);
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
