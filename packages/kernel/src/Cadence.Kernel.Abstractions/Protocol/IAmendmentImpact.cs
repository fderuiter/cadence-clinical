using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;

namespace Cadence.Kernel.Protocol;

/// <summary>
/// The impact of a protocol amendment (K-PROT-2): what depends on the version the amendment changes.
/// </summary>
/// <param name="Amended">The forked version that carries the amendment.</param>
/// <param name="Parent">The approved version it amends.</param>
/// <param name="ImpactedElements">The protocol elements the amendment declares it changes.</param>
/// <param name="Dependents">
/// Everything downstream of any version of <paramref name="Parent"/>, except the amended fork and what depends only on
/// it. Objects the reader may not see are counted and masked, never dropped (D1).
/// </param>
public sealed record AmendmentImpact(
    ProtocolVersionId Amended,
    ProtocolVersionId Parent,
    IReadOnlyList<string> ImpactedElements,
    DependentsReport Dependents);

/// <summary>
/// Computes amendment impact (spec §6.2, K-PROT-2) as a caller of <see cref="IKernelLineage.DependentsAsync"/>, so the
/// Impact verb and the amendment report count the same way (Canvas D2). Until protocol elements are nodes of their own
/// (#4808) the changed object is the parent version as a whole, and <see cref="AmendmentImpact.ImpactedElements"/>
/// says which of its elements the amendment names.
/// </summary>
public interface IAmendmentImpact
{
    /// <summary>
    /// The impact of the amendment <paramref name="amended"/> carries, or null when no such version exists in the
    /// tenant. Throws <see cref="InvalidOperationException"/> for a first version, which amends nothing.
    /// </summary>
    Task<AmendmentImpact?> ComputeAsync(
        TenantId tenant,
        Actor reader,
        ProtocolVersionId amended,
        int depth = LineageQuery.DefaultDepth,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null,
        CancellationToken cancellationToken = default);
}
