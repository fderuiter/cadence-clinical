using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// Classifies whether an observation records blinded treatment allocation, investigational intervention, or masked
/// drug dispensation according to protocol design elements and concept metadata (K-AUTH-4).
/// </summary>
internal static class BlindedObservationClassifier
{
    public static bool IsBlindedTreatment(
        ConceptId conceptId,
        ObservationValue valueAtCut,
        Concept? concept,
        string? collectionSpecId,
        string? itemId,
        IReadOnlyList<ProtocolDesign> designs) =>
        BlindedTreatmentClassifier.Instance.IsBlindedObservation(
            conceptId,
            valueAtCut,
            ToMetadata(concept),
            collectionSpecId,
            itemId,
            designs);

    public static BlindedConceptMetadata? ToMetadata(Concept? concept) =>
        concept is null
            ? null
            : new BlindedConceptMetadata(concept.ExternalCode, concept.PreferredLabel, concept.Definition);

    public static BlindedObservationLookup CreateLookup(
        IReadOnlyDictionary<ConceptId, Concept> conceptsById,
        IReadOnlyList<ProtocolDesign> designs,
        IBlindedTreatmentClassifier? classifier = null) =>
        new(conceptsById, designs, classifier);

    internal static async Task<HashSet<ObservationId>> ResolveBlindedObservationsAsync(
        KernelDbContext db,
        StudyId studyId,
        IEnumerable<Observation> observations,
        CancellationToken cancellationToken,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null)
    {
        var obsList = observations.ToList();
        if (obsList.Count == 0)
        {
            return [];
        }

        var obsIds = obsList.Select(o => o.Id).ToList();
        var obsIdSet = obsIds.ToHashSet();
        var instances = await db.CollectionInstances.AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.StudyId == studyId
                && (validAt == null || c.CollectedAt <= validAt.Value)
                && (knownAt == null || c.OpenedAt <= knownAt.Value)
                && c.Members.Any(m => obsIds.Contains(m.ObservationId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ProtocolDesign> designs;
        if (validAt is { } vAt && knownAt is { } kAt)
        {
            var referencedVersionIds = instances
                .Where(c => c.Members.Any(m => m.JoinedAt <= kAt && obsIdSet.Contains(m.ObservationId)))
                .Select(c => c.ProtocolVersionId)
                .ToHashSet();
            var protocols = await KernelExportReader.ReadProtocolsAsync(db, studyId, vAt, kAt, referencedVersionIds, cancellationToken)
                .ConfigureAwait(false);
            designs = protocols.Where(p => p.Design is not null).Select(p => p.Design!).ToList();
        }
        else
        {
            var versions = await db.ProtocolVersions.AsNoTracking()
                .Where(v => v.StudyId == studyId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            designs = versions
                .Select(v => ProtocolDesignDocument.Read(v.Content).Design)
                .Where(d => d is not null)
                .Select(d => d!)
                .ToList();
        }

        var conceptIds = obsList.Select(o => o.ConceptId).Distinct().ToList();
        var concepts = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        var itemByObs = new Dictionary<ObservationId, (string SpecId, string ItemId)>();
        foreach (var instance in instances)
        {
            foreach (var member in instance.Members.Where(m => knownAt is null || m.JoinedAt <= knownAt.Value))
            {
                itemByObs[member.ObservationId] = (instance.CollectionSpecId, member.ItemId);
            }
        }

        var blindingLookup = CreateLookup(concepts, designs);
        var blinded = new HashSet<ObservationId>();
        foreach (var obs in obsList)
        {
            ObservationValue valueAtCut = obs.Value;
            if (knownAt is { } kCut && obs.Revisions.Count > 0)
            {
                var latestAtCut = obs.Revisions
                    .Where(r => r.RecordedAt <= kCut)
                    .OrderBy(r => r.Number)
                    .LastOrDefault();
                if (latestAtCut is null)
                {
                    continue;
                }

                valueAtCut = latestAtCut.Value;
            }

            itemByObs.TryGetValue(obs.Id, out var membership);
            if (blindingLookup.IsBlindedTreatment(
                    obs.ConceptId,
                    valueAtCut,
                    membership.SpecId,
                    membership.ItemId))
            {
                blinded.Add(obs.Id);
            }
        }

        return blinded;
    }
}

internal sealed class BlindedObservationLookup(
    IReadOnlyDictionary<ConceptId, Concept> conceptsById,
    IReadOnlyList<ProtocolDesign> designs,
    IBlindedTreatmentClassifier? classifier = null)
{
    private readonly IBlindedTreatmentClassifier _classifier = classifier ?? BlindedTreatmentClassifier.Instance;
    private readonly Dictionary<BlindingKey, bool> _cache = new(BlindingKeyComparer.Instance);
    private readonly Dictionary<ConceptId, BlindedConceptMetadata?> _metadataByConcept = [];

    public bool IsBlindedTreatment(
        ConceptId conceptId,
        ObservationValue valueAtCut,
        string? collectionSpecId,
        string? itemId)
    {
        // Check per-observation NullFlavor.Masked before consulting or populating the memo cache so a masked
        // value never marks unmasked siblings on the same (ConceptId, CollectionSpecId, ItemId) as blinded.
        if (valueAtCut is MissingValue { Reason: NullFlavor.Masked })
        {
            return true;
        }

        var key = new BlindingKey(conceptId, collectionSpecId, itemId);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!_metadataByConcept.TryGetValue(conceptId, out var metadata))
        {
            conceptsById.TryGetValue(conceptId, out var concept);
            metadata = BlindedObservationClassifier.ToMetadata(concept);
            _metadataByConcept[conceptId] = metadata;
        }

        var isBlinded = _classifier.IsBlindedObservation(
            conceptId,
            value: null,
            metadata,
            collectionSpecId,
            itemId,
            designs);
        _cache[key] = isBlinded;
        return isBlinded;
    }

    private readonly record struct BlindingKey(ConceptId ConceptId, string? CollectionSpecId, string? ItemId);

    private sealed class BlindingKeyComparer : IEqualityComparer<BlindingKey>
    {
        public static readonly BlindingKeyComparer Instance = new();

        public bool Equals(BlindingKey x, BlindingKey y) =>
            x.ConceptId == y.ConceptId
            && string.Equals(x.CollectionSpecId, y.CollectionSpecId, StringComparison.Ordinal)
            && string.Equals(x.ItemId, y.ItemId, StringComparison.Ordinal);

        public int GetHashCode(BlindingKey obj)
        {
            var hash = new HashCode();
            hash.Add(obj.ConceptId);
            hash.Add(obj.CollectionSpecId, StringComparer.Ordinal);
            hash.Add(obj.ItemId, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
