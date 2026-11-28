using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

internal static class ExportAttestationAndAuditReader
{
    internal static async Task<IReadOnlyList<ExportAttestationSnapshot>> ReadAttestationsAsync(
        KernelDbContext db,
        HashSet<Guid> targetIds,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        if (targetIds.Count == 0)
        {
            return [];
        }

        var ids = targetIds.ToList();
        var rows = await db.Attestations.AsNoTracking()
            .Where(a => ids.Contains(a.TargetId) && a.RecordedAt <= knownAt)
            .OrderBy(a => a.RecordedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(a => new ExportAttestationSnapshot(
                a.Id,
                a.ActorId,
                a.Meaning,
                a.TargetKind,
                a.TargetId,
                a.TargetContentHash,
                a.AuthenticatedAt,
                a.Method,
                a.RecordedAt,
                a.Reason))
            .ToList();
    }

    internal static HashSet<string> BuildBlindedAuditKeys(
        IReadOnlyList<ExportCollectionSnapshot> collections,
        IReadOnlyList<ExportObservationSnapshot> observations,
        IReadOnlyList<StudyLockEntry> locks,
        IReadOnlyList<ExportAttestationSnapshot> attestations,
        IReadOnlyList<ProtocolDesign> designs)
    {
        var blindedObsIds = new HashSet<ObservationId>();
        var blindedObsGuids = new HashSet<Guid>();
        var blindedCollectionGuids = new HashSet<Guid>();
        foreach (var obs in observations)
        {
            if (!obs.IsBlindedTreatment)
            {
                continue;
            }

            blindedObsIds.Add(obs.ObservationId);
            blindedObsGuids.Add(obs.ObservationId.Value);
            if (obs.CollectionInstanceId is { } cid)
            {
                blindedCollectionGuids.Add(cid.Value);
            }
        }

        var firstSpecById = new Dictionary<string, CollectionSpec>(StringComparer.Ordinal);
        foreach (var design in designs)
        {
            foreach (var spec in design.CollectionSpecs)
            {
                firstSpecById.TryAdd(spec.Id, spec);
            }
        }

        var specBlindingCache = new Dictionary<CollectionSpecBlindingKey, bool>(CollectionSpecBlindingKeyComparer.Instance);
        var emptyTextValue = new TextValue(string.Empty);
        foreach (var coll in collections)
        {
            if (blindedCollectionGuids.Contains(coll.CollectionInstanceId.Value))
            {
                continue;
            }

            var specKey = new CollectionSpecBlindingKey(coll.CollectionSpecId, coll.CollectionSpecName);
            if (!specBlindingCache.TryGetValue(specKey, out var isBlindedSpec))
            {
                isBlindedSpec = BlindedTreatmentClassifier.Instance.IsBlindedCollectionSpec(
                        coll.CollectionSpecId,
                        coll.CollectionSpecName,
                        designs: designs)
                    || (firstSpecById.TryGetValue(coll.CollectionSpecId, out var spec)
                        && spec.Items.Any(i => BlindedObservationClassifier.IsBlindedTreatment(
                            i.Concept.ConceptId,
                            emptyTextValue,
                            concept: null,
                            coll.CollectionSpecId,
                            i.Id,
                            designs)));
                specBlindingCache[specKey] = isBlindedSpec;
            }

            if (isBlindedSpec || (blindedObsIds.Count > 0 && coll.ItemIdsByObservation.Keys.Any(blindedObsIds.Contains)))
            {
                blindedCollectionGuids.Add(coll.CollectionInstanceId.Value);
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obs in observations.Where(o => o.IsBlindedTreatment))
        {
            keys.Add(obs.ObservationId.ToString());
            foreach (var assertion in obs.Assertions)
            {
                keys.Add(assertion.AssertionId.ToString());
                if (assertion.RetractedBy is { } retractedBy)
                {
                    keys.Add(retractedBy.ToString());
                }
            }
        }

        foreach (var collGuid in blindedCollectionGuids)
        {
            keys.Add(CollectionInstanceId.FromGuid(collGuid).ToString());
        }

        var blindedLockTargetGuids = new HashSet<Guid>();
        foreach (var l in locks.Where(l => l.BlindedScope))
        {
            keys.Add(l.LockId.ToString());
            blindedLockTargetGuids.Add(l.LockId.Value);
            keys.Add(l.AttestationId.ToString());
            if (l.ReleaseAssertionId is { } relId)
            {
                keys.Add(relId.ToString());
                blindedLockTargetGuids.Add(relId.Value);
            }

            if (l.ReleaseAttestationId is { } relAttId)
            {
                keys.Add(relAttId.ToString());
            }
        }

        foreach (var att in attestations)
        {
            if (blindedObsGuids.Contains(att.TargetId)
                || blindedCollectionGuids.Contains(att.TargetId)
                || blindedLockTargetGuids.Contains(att.TargetId))
            {
                keys.Add(att.AttestationId.ToString());
            }
        }

        return keys;
    }

    internal static async Task<(
        IReadOnlyList<ObservationAuditView> Entries,
        IReadOnlySet<long> BlindedSequences,
        IReadOnlyDictionary<long, string> AggregateIdsBySequence)> ReadAuditEntriesAsync(
        KernelDbContext db,
        HashSet<string> aggregateKeys,
        HashSet<string> blindedAggregateKeys,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        if (aggregateKeys.Count == 0)
        {
            return ([], new HashSet<long>(), new Dictionary<long, string>());
        }

        var keys = aggregateKeys.ToList();
        var rows = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateId != null && keys.Contains(a.AggregateId) && a.RecordedAt <= knownAt)
            .OrderBy(a => a.Sequence)
            .Select(a => new
            {
                a.AggregateId,
                View = new ObservationAuditView(
                    a.Sequence,
                    a.RecordedAt,
                    a.ActorId,
                    a.Action,
                    a.AggregateType ?? string.Empty,
                    a.AggregateVersion,
                    a.ReasonForChange),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var blindedSequences = rows
            .Where(r => r.AggregateId is not null && blindedAggregateKeys.Contains(r.AggregateId))
            .Select(r => r.View.Sequence)
            .ToHashSet();

        var aggregateIdsBySequence = rows
            .Where(r => r.AggregateId is not null)
            .ToDictionary(r => r.View.Sequence, r => r.AggregateId!);

        return ([.. rows.Select(r => r.View)], blindedSequences, aggregateIdsBySequence);
    }

    private readonly record struct CollectionSpecBlindingKey(string SpecId, string SpecName);

    private sealed class CollectionSpecBlindingKeyComparer : IEqualityComparer<CollectionSpecBlindingKey>
    {
        public static readonly CollectionSpecBlindingKeyComparer Instance = new();

        public bool Equals(CollectionSpecBlindingKey x, CollectionSpecBlindingKey y) =>
            string.Equals(x.SpecId, y.SpecId, StringComparison.Ordinal)
            && string.Equals(x.SpecName, y.SpecName, StringComparison.Ordinal);

        public int GetHashCode(CollectionSpecBlindingKey obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.SpecId),
                StringComparer.Ordinal.GetHashCode(obj.SpecName));
    }
}
