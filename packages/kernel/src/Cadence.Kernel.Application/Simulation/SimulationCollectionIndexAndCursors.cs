using System;
using System.Collections.Generic;
using System.Globalization;

using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;

namespace Cadence.Kernel.Application.Simulation;

internal readonly record struct DeltaFindingKey(
    string RuleId,
    ValidationStage Stage,
    string Dataset,
    string Subject);

internal sealed class IndexedSimulationForm(
    SimulationCollectionSnapshot form,
    Dictionary<string, RuleValue> ownValues,
    EnrollmentSiblingIndex siblings)
{
    private string? _key;

    public SimulationCollectionSnapshot Form { get; } = form;

    public string Key => _key ??= Form.Id.Value.ToString("D", CultureInfo.InvariantCulture);

    public Dictionary<string, RuleValue> OwnValues { get; } = ownValues;

    public EnrollmentSiblingIndex Siblings { get; } = siblings;
}

internal sealed class SimulationCollectionIndex(IReadOnlyList<IndexedSimulationForm> forms)
{
    public static readonly SimulationCollectionIndex Empty = new([]);

    public IReadOnlyList<IndexedSimulationForm> Forms { get; } = forms;

    public SimulationCollectionFormCursor CreateCursor() => new(Forms);

    public static SimulationCollectionIndex Build(
        MaskedSimulationSnapshot snapshot,
        ProtocolDesign? designOverlay)
    {
        var collections = snapshot.VisibleCollections;
        if (collections.Count == 0)
        {
            return Empty;
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
        var siblingIndexByEnrollment = new Dictionary<EnrollmentId, EnrollmentSiblingIndex>(byEnrollment.Count);

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

    public static bool CollectionDesignsMatch(ProtocolDesign? left, ProtocolDesign? right)
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
}

internal sealed class SimulationCollectionFormCursor(IReadOnlyList<IndexedSimulationForm> forms) : IResettableRuleRecordCursor
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

internal sealed class LazyRecordEnrollmentLookup(
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
