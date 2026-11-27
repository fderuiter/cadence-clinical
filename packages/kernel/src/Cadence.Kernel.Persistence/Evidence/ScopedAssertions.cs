using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

/// <summary>
/// The assertions at a set of lock scopes, the answers to the queries among them and every retraction of them, read
/// for an observation or a form (spec §8.2, §10.5). Assertions are few per scope, so one read and an in-memory join.
/// </summary>
internal sealed class ScopedAssertions
{
    private readonly Dictionary<ScopeKey, int> _scopeIndex;
    private readonly Dictionary<AssertionId, Assertion> _retractedBy;
    private readonly List<Assertion> _stated;

    internal ScopedAssertions(IReadOnlyList<NodeRef> scopes, List<Assertion> all)
        : this(IndexScopes(scopes), all)
    {
    }

    private ScopedAssertions(Dictionary<ScopeKey, int> scopeIndex, List<Assertion> all)
    {
        _scopeIndex = scopeIndex;
        _retractedBy = all.Where(a => a.RetractsId is not null).ToDictionary(a => a.RetractsId!.Value);
        _stated = [.. all.Where(a => a.RetractsId is null).OrderBy(a => a.AssertedAt).ThenBy(a => a.Id)];
    }

    /// <summary>Reads the assertions at <paramref name="scopes"/>, widest first (<c>LockScopes.Covering</c>).</summary>
    public static async Task<ScopedAssertions> ReadAsync(KernelDbContext db, IReadOnlyList<NodeRef> scopes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var scopeIndex = IndexScopes(scopes);
        if (scopeIndex.Count == 0)
        {
            return new ScopedAssertions(scopeIndex, []);
        }

        var scopeIds = DistinctScopeIds(scopes);
        var direct = await db.Assertions.AsNoTracking()
            .Where(a => scopeIds.Contains(a.TargetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var queryIds = direct
            .Where(a => a.Kind == AssertionKind.Queried && a.RetractsId == null && scopeIndex.ContainsKey(new ScopeKey(a.TargetKind, a.TargetId)))
            .Select(a => a.Id.Value)
            .ToList();
        var answers = queryIds.Count == 0
            ? []
            : await db.Assertions.AsNoTracking()
                .Where(a => a.Kind == AssertionKind.QueryAnswered && queryIds.Contains(a.TargetId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var filtered = new List<Assertion>(direct.Count + answers.Count);
        for (var i = 0; i < direct.Count; i++)
        {
            var candidate = direct[i];
            if (scopeIndex.ContainsKey(new ScopeKey(candidate.TargetKind, candidate.TargetId))
                || string.Equals(candidate.TargetKind, NodeKinds.Assertion, StringComparison.Ordinal))
            {
                filtered.Add(candidate);
            }
        }

        for (var i = 0; i < answers.Count; i++)
        {
            var answer = answers[i];
            if (string.Equals(answer.TargetKind, NodeKinds.Assertion, StringComparison.Ordinal)
                || scopeIndex.ContainsKey(new ScopeKey(answer.TargetKind, answer.TargetId)))
            {
                filtered.Add(answer);
            }
        }

        return new ScopedAssertions(scopeIndex, filtered);
    }

    /// <summary>Reads only the active locks and freezes at <paramref name="scopes"/>, widest first.</summary>
    public static async Task<IReadOnlyList<LockView>> ReadLocksAsync(
        KernelDbContext db,
        IReadOnlyList<NodeRef> scopes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var scopeIndex = IndexScopes(scopes);
        if (scopeIndex.Count == 0)
        {
            return [];
        }

        var scopeIds = DistinctScopeIds(scopes);
        var locks = await db.Assertions.AsNoTracking()
            .Where(a => scopeIds.Contains(a.TargetId)
                && (a.Kind == AssertionKind.Locked || a.Kind == AssertionKind.Frozen))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (locks.Count == 0)
        {
            return [];
        }

        var matchedLocks = new List<Assertion>(locks.Count);
        for (var i = 0; i < locks.Count; i++)
        {
            var candidate = locks[i];
            if (scopeIndex.ContainsKey(new ScopeKey(candidate.TargetKind, candidate.TargetId)))
            {
                matchedLocks.Add(candidate);
            }
        }

        return matchedLocks.Count == 0
            ? []
            : new ScopedAssertions(scopeIndex, matchedLocks).Locks();
    }

    /// <summary>The locks in force, widest scope first.</summary>
    public IReadOnlyList<LockView> Locks() =>
        [.. _stated
            .Where(a => a.IsLock && !_retractedBy.ContainsKey(a.Id))
            .OrderBy(Width)
            .Select(a => new LockView(a.Id, (LockKind)a.Kind, a.Target, a.AttestationId!.Value, a.AssertedAt, a.AssertedBy, a.ReasonForChange))];

    /// <summary>
    /// The assertions other than locks about the observations <paramref name="about"/> accepts, and the answers to their
    /// queries, retracted ones included, oldest first.
    /// </summary>
    public IReadOnlyList<AssertionView> About(Func<Assertion, bool> about) =>
        [.. _stated
            .Where(a => !a.IsLock && (about(a) || a.TargetKind == NodeKinds.Assertion))
            .Select(a => _retractedBy.TryGetValue(a.Id, out var retraction) ? View(a, retraction.Id, retraction.AssertedAt) : View(a, null, null))];

    /// <summary>
    /// Every covering assertion id for the audit trail (spec §8.2): locks on covering scopes, assertions accepted by
    /// <paramref name="about"/>, answers to their queries, and every retraction or release of them.
    /// </summary>
    public IReadOnlyList<AssertionId> CoveringAuditIds(Func<Assertion, bool> about)
    {
        var queryIds = _stated
            .Where(a => !a.IsLock && a.Kind == AssertionKind.Queried && about(a))
            .Select(a => a.Id.Value)
            .ToHashSet();
        var coveredStated = _stated
            .Where(a => a.IsLock || about(a) || (a.TargetKind == NodeKinds.Assertion && queryIds.Contains(a.TargetId)))
            .Select(a => a.Id)
            .ToList();
        var retractions = coveredStated
            .Where(_retractedBy.ContainsKey)
            .Select(id => _retractedBy[id].Id);
        return [.. coveredStated.Concat(retractions)];
    }

    private (int HierarchyOrder, int ScopeOrder) Width(Assertion assertion)
    {
        var hierarchyOrder = LockScopes.OrderOf(assertion.TargetKind);
        var scopeOrder = _scopeIndex.TryGetValue(new ScopeKey(assertion.TargetKind, assertion.TargetId), out var index)
            ? index
            : int.MaxValue;
        return (hierarchyOrder, scopeOrder);
    }

    private static Dictionary<ScopeKey, int> IndexScopes(IReadOnlyList<NodeRef> scopes)
    {
        var index = new Dictionary<ScopeKey, int>(scopes.Count, ScopeKeyComparer.Instance);
        for (var i = 0; i < scopes.Count; i++)
        {
            var scope = scopes[i];
            index.TryAdd(new ScopeKey(scope.Kind, scope.Id), i);
        }

        return index;
    }

    private static List<Guid> DistinctScopeIds(IReadOnlyList<NodeRef> scopes)
    {
        var seen = new HashSet<Guid>(scopes.Count);
        var ids = new List<Guid>(scopes.Count);
        for (var i = 0; i < scopes.Count; i++)
        {
            var id = scopes[i].Id;
            if (seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static AssertionView View(Assertion a, AssertionId? retractedBy, DateTimeOffset? retractedAt) =>
        new(a.Id, a.Kind, a.Target, a.TargetRevision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, retractedBy, retractedAt);

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
