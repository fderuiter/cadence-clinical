using System;
using System.Collections.Generic;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Persistence.Evidence;

internal sealed class BatchAssertionIndex
{
    private readonly Dictionary<Guid, List<(AssertionView View, int Ordinal)>> _byObservationId = [];
    private readonly Dictionary<Guid, List<(AssertionView View, int Ordinal)>> _byQueryId = [];
    private readonly HashSet<(Guid ObservationId, int Revision)> _verifiedRevisions = [];

    public BatchAssertionIndex(IReadOnlyList<AssertionView> allAssertions)
    {
        ArgumentNullException.ThrowIfNull(allAssertions);
        for (var i = 0; i < allAssertions.Count; i++)
        {
            var assertion = allAssertions[i];
            if (string.Equals(assertion.Target.Kind, NodeKinds.Observation, StringComparison.Ordinal))
            {
                if (!_byObservationId.TryGetValue(assertion.Target.Id, out var obsList))
                {
                    obsList = [];
                    _byObservationId.Add(assertion.Target.Id, obsList);
                }

                obsList.Add((assertion, i));

                if (assertion.Kind == AssertionKind.Verified
                    && assertion.RetractedAt is null
                    && assertion.Revision is { } verifiedRevision)
                {
                    _verifiedRevisions.Add((assertion.Target.Id, verifiedRevision));
                }
            }
            else if (string.Equals(assertion.Target.Kind, NodeKinds.Assertion, StringComparison.Ordinal))
            {
                if (!_byQueryId.TryGetValue(assertion.Target.Id, out var queryList))
                {
                    queryList = [];
                    _byQueryId.Add(assertion.Target.Id, queryList);
                }

                queryList.Add((assertion, i));
            }
        }
    }

    public bool IsVerified(Guid observationGuid, int revision) =>
        _verifiedRevisions.Contains((observationGuid, revision));

    public IReadOnlyList<AssertionView> ForForm(IReadOnlySet<Guid> memberObservationGuids)
    {
        if (_byObservationId.Count == 0 || memberObservationGuids.Count == 0)
        {
            return [];
        }

        List<(AssertionView View, int Ordinal)>? matched = null;
        HashSet<Guid>? addedQueryIds = null;
        foreach (var obsGuid in memberObservationGuids)
        {
            if (!_byObservationId.TryGetValue(obsGuid, out var obsAssertions))
            {
                continue;
            }

            matched ??= new List<(AssertionView View, int Ordinal)>(obsAssertions.Count);
            for (var i = 0; i < obsAssertions.Count; i++)
            {
                var entry = obsAssertions[i];
                matched.Add(entry);
                if (entry.View.Kind == AssertionKind.Queried
                    && _byQueryId.TryGetValue(entry.View.AssertionId.Value, out var queryTargets))
                {
                    addedQueryIds ??= [];
                    if (addedQueryIds.Add(entry.View.AssertionId.Value))
                    {
                        matched.AddRange(queryTargets);
                    }
                }
            }
        }

        if (matched is null || matched.Count == 0)
        {
            return [];
        }

        if (matched.Count == 1)
        {
            return [matched[0].View];
        }

        matched.Sort(static (x, y) => x.Ordinal.CompareTo(y.Ordinal));
        var ordered = new AssertionView[matched.Count];
        for (var i = 0; i < matched.Count; i++)
        {
            ordered[i] = matched[i].View;
        }

        return ordered;
    }
}

internal sealed class BatchLockIndex
{
    private readonly Dictionary<ScopeKey, List<(LockView Lock, int Ordinal)>> _byScope = new(ScopeKeyComparer.Instance);

    public BatchLockIndex(IReadOnlyList<LockView> allLocks)
    {
        ArgumentNullException.ThrowIfNull(allLocks);
        for (var i = 0; i < allLocks.Count; i++)
        {
            var lockView = allLocks[i];
            var key = new ScopeKey(lockView.Scope.Kind, lockView.Scope.Id);
            if (!_byScope.TryGetValue(key, out var list))
            {
                list = [];
                _byScope.Add(key, list);
            }

            list.Add((lockView, i));
        }
    }

    public IReadOnlyList<LockView> ForForm(
        Guid studyGuid,
        Guid studySiteGuid,
        Guid enrollmentGuid,
        Guid instanceGuid,
        IReadOnlySet<Guid> memberObservationGuids)
    {
        if (_byScope.Count == 0)
        {
            return [];
        }

        List<(LockView Lock, int Ordinal)>? matched = null;
        AppendScopeLocks(new ScopeKey(NodeKinds.Study, studyGuid), ref matched);
        AppendScopeLocks(new ScopeKey(NodeKinds.StudySite, studySiteGuid), ref matched);
        AppendScopeLocks(new ScopeKey(NodeKinds.Enrollment, enrollmentGuid), ref matched);
        AppendScopeLocks(new ScopeKey(NodeKinds.CollectionInstance, instanceGuid), ref matched);
        foreach (var obsGuid in memberObservationGuids)
        {
            AppendScopeLocks(new ScopeKey(NodeKinds.Observation, obsGuid), ref matched);
        }

        if (matched is null || matched.Count == 0)
        {
            return [];
        }

        if (matched.Count == 1)
        {
            return [matched[0].Lock];
        }

        matched.Sort(static (x, y) => x.Ordinal.CompareTo(y.Ordinal));
        var ordered = new LockView[matched.Count];
        for (var i = 0; i < matched.Count; i++)
        {
            ordered[i] = matched[i].Lock;
        }

        return ordered;
    }

    public (bool Locked, bool Frozen, int Count) SummarizeForForm(
        Guid studyGuid,
        Guid studySiteGuid,
        Guid enrollmentGuid,
        Guid instanceGuid,
        IReadOnlySet<Guid> memberObservationGuids)
    {
        if (_byScope.Count == 0)
        {
            return (false, false, 0);
        }

        var locked = false;
        var frozen = false;
        var count = 0;
        AccumulateScopeSummary(new ScopeKey(NodeKinds.Study, studyGuid), ref locked, ref frozen, ref count);
        AccumulateScopeSummary(new ScopeKey(NodeKinds.StudySite, studySiteGuid), ref locked, ref frozen, ref count);
        AccumulateScopeSummary(new ScopeKey(NodeKinds.Enrollment, enrollmentGuid), ref locked, ref frozen, ref count);
        AccumulateScopeSummary(new ScopeKey(NodeKinds.CollectionInstance, instanceGuid), ref locked, ref frozen, ref count);
        foreach (var obsGuid in memberObservationGuids)
        {
            AccumulateScopeSummary(new ScopeKey(NodeKinds.Observation, obsGuid), ref locked, ref frozen, ref count);
        }

        return (locked, frozen, count);
    }

    private void AppendScopeLocks(ScopeKey key, ref List<(LockView Lock, int Ordinal)>? matched)
    {
        if (_byScope.TryGetValue(key, out var entries))
        {
            matched ??= new List<(LockView Lock, int Ordinal)>(entries.Count);
            matched.AddRange(entries);
        }
    }

    private void AccumulateScopeSummary(ScopeKey key, ref bool locked, ref bool frozen, ref int count)
    {
        if (!_byScope.TryGetValue(key, out var entries))
        {
            return;
        }

        count += entries.Count;
        for (var i = 0; i < entries.Count; i++)
        {
            var kind = entries[i].Lock.Kind;
            if (kind == LockKind.Locked)
            {
                locked = true;
            }
            else if (kind == LockKind.Frozen)
            {
                frozen = true;
            }
        }
    }

    private readonly record struct ScopeKey(string Kind, Guid Id);

    private sealed class ScopeKeyComparer : IEqualityComparer<ScopeKey>
    {
        public static readonly ScopeKeyComparer Instance = new();

        public bool Equals(ScopeKey x, ScopeKey y) =>
            x.Id == y.Id && string.Equals(x.Kind, y.Kind, StringComparison.Ordinal);

        public int GetHashCode(ScopeKey obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Kind), obj.Id);
    }
}

internal sealed class BatchErrorTargetIndex
{
    private readonly Dictionary<Guid, int> _byDatasetRecordGuid = [];
    private readonly Dictionary<Guid, int> _byObservationGuid = [];

    public BatchErrorTargetIndex(IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)> activeErrorTargets)
    {
        ArgumentNullException.ThrowIfNull(activeErrorTargets);
        for (var i = 0; i < activeErrorTargets.Count; i++)
        {
            var target = activeErrorTargets[i].Target;
            if (target.Kind == FindingTargetKind.DatasetRecord
                && Guid.TryParse(target.Key, out var recordGuid))
            {
                _byDatasetRecordGuid[recordGuid] = _byDatasetRecordGuid.GetValueOrDefault(recordGuid) + 1;
            }
            else if (target.Kind == FindingTargetKind.Observation
                && target.Node is { Kind: NodeKinds.Observation } obsNode)
            {
                _byObservationGuid[obsNode.Id] = _byObservationGuid.GetValueOrDefault(obsNode.Id) + 1;
            }
        }
    }

    public int CountForForm(Guid instanceGuid, IReadOnlySet<Guid> memberObservationGuids)
    {
        var count = _byDatasetRecordGuid.GetValueOrDefault(instanceGuid);
        if (_byObservationGuid.Count == 0)
        {
            return count;
        }

        foreach (var obsGuid in memberObservationGuids)
        {
            count += _byObservationGuid.GetValueOrDefault(obsGuid);
        }

        return count;
    }
}
