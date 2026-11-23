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
    private readonly IReadOnlyList<NodeRef> _scopes;
    private readonly Dictionary<AssertionId, Assertion> _retractedBy;
    private readonly List<Assertion> _stated;

    private ScopedAssertions(IReadOnlyList<NodeRef> scopes, List<Assertion> all)
    {
        _scopes = scopes;
        _retractedBy = all.Where(a => a.RetractsId is not null).ToDictionary(a => a.RetractsId!.Value);
        _stated = [.. all.Where(a => a.RetractsId is null).OrderBy(a => a.AssertedAt).ThenBy(a => a.Id)];
    }

    /// <summary>Reads the assertions at <paramref name="scopes"/>, widest first (<c>LockScopes.Covering</c>).</summary>
    public static async Task<ScopedAssertions> ReadAsync(KernelDbContext db, IReadOnlyList<NodeRef> scopes, CancellationToken cancellationToken)
    {
        var scopeIds = scopes.Select(s => s.Id).ToList();
        var direct = await db.Assertions.AsNoTracking()
            .Where(a => scopeIds.Contains(a.TargetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var queryIds = direct.Where(a => a.Kind == AssertionKind.Queried && a.RetractsId == null).Select(a => a.Id.Value).ToList();
        var answers = queryIds.Count == 0
            ? []
            : await db.Assertions.AsNoTracking()
                .Where(a => a.Kind == AssertionKind.QueryAnswered && queryIds.Contains(a.TargetId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        return new ScopedAssertions(
            scopes,
            [.. direct.Concat(answers).Where(a => scopes.Any(s => s.Kind == a.TargetKind && s.Id == a.TargetId) || a.TargetKind == NodeKinds.Assertion)]);
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

    private int Width(Assertion assertion)
    {
        for (var i = 0; i < _scopes.Count; i++)
        {
            if (_scopes[i].Kind == assertion.TargetKind && _scopes[i].Id == assertion.TargetId)
            {
                return i;
            }
        }

        return _scopes.Count;
    }

    private static AssertionView View(Assertion a, AssertionId? retractedBy, DateTimeOffset? retractedAt) =>
        new(a.Id, a.Kind, a.Target, a.TargetRevision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, retractedBy, retractedAt);
}
