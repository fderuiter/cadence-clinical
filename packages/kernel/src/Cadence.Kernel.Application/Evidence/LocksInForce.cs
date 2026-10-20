using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>
/// Reads the locks in force at a set of scopes (spec §10.5, ADR-2621): <see cref="AssertionKind.Locked"/> and
/// <see cref="AssertionKind.Frozen"/> assertions there that no later assertion retracts.
/// </summary>
internal static class LocksInForce
{
    /// <summary>The locks that refuse recording and correcting observations: both kinds.</summary>
    public static readonly IReadOnlySet<AssertionKind> DenyingWrites = new HashSet<AssertionKind> { AssertionKind.Locked, AssertionKind.Frozen };

    /// <summary>The locks that refuse new assertions about an observation: <see cref="AssertionKind.Locked"/> only.</summary>
    public static readonly IReadOnlySet<AssertionKind> DenyingAssertions = new HashSet<AssertionKind> { AssertionKind.Locked };

    /// <summary>
    /// Takes shared locks on <paramref name="scopes"/> (widest first), then reports whether a lock of one of
    /// <paramref name="kinds"/> is in force at any of them. A lock committed before the shared locks were granted is
    /// seen; one applied later waits for this command to finish.
    /// </summary>
    public static async Task<bool> AnyAsync(
        IKernelUnitOfWork work, IReadOnlyList<NodeRef> scopes, IReadOnlySet<AssertionKind> kinds, CancellationToken cancellationToken)
    {
        await work.ShareScopesAsync(scopes, cancellationToken).ConfigureAwait(false);
        var locks = await ReadAsync(work, scopes, cancellationToken).ConfigureAwait(false);
        return locks.Any(l => kinds.Contains(l.Kind));
    }

    /// <summary>The locks in force at <paramref name="scopes"/>, without taking any advisory lock.</summary>
    public static async Task<IReadOnlyList<Assertion>> ReadAsync(IKernelUnitOfWork work, IReadOnlyList<NodeRef> scopes, CancellationToken cancellationToken)
    {
        var ids = scopes.Select(s => s.Id).ToList();
        var applied = (await work.ReadAllAsync<Assertion>(
                a => (a.Kind == AssertionKind.Locked || a.Kind == AssertionKind.Frozen) && a.RetractsId == null && ids.Contains(a.TargetId),
                cancellationToken).ConfigureAwait(false))
            .Where(a => scopes.Any(s => s.Kind == a.TargetKind && s.Id == a.TargetId))
            .ToList();
        if (applied.Count == 0)
        {
            return [];
        }

        var released = await RetractedAsync(work, applied.Select(a => a.Id).ToList(), cancellationToken).ConfigureAwait(false);
        return [.. applied.Where(a => !released.Contains(a.Id))];
    }

    /// <summary>Which of <paramref name="assertions"/> a later assertion retracts.</summary>
    public static async Task<IReadOnlySet<AssertionId>> RetractedAsync(IKernelUnitOfWork work, IReadOnlyList<AssertionId> assertions, CancellationToken cancellationToken)
    {
        var retracting = await work.ReadAllAsync<Assertion>(
            a => a.RetractsId != null && assertions.Contains(a.RetractsId.Value), cancellationToken).ConfigureAwait(false);
        return retracting.Select(a => a.RetractsId!.Value).ToHashSet();
    }
}
