using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Simulation;

namespace Cadence.Kernel.Protocol;

/// <summary>
/// The impact of a protocol amendment (K-PROT-2): the elements it changes and what depends on them.
/// </summary>
/// <param name="Amended">The forked version that carries the amendment.</param>
/// <param name="Parent">The approved version it amends.</param>
/// <param name="ImpactedElements">The protocol elements the amendment declares it changes.</param>
/// <param name="Dependents">
/// Everything downstream of the elements the amendment changes or removes, and of the parent version itself other than
/// through its elements (ADR-2474), except the amended fork and what depends only on it. Objects the reader may not see
/// are counted and masked, never dropped (D1).
/// </param>
/// <param name="ChangedElements">
/// The elements the amended content adds, changes or removes relative to the parent, by the structural diff (ADR-2480
/// §4.6), sorted by element id. Compare with <paramref name="ImpactedElements"/> to see what was changed but not declared.
/// </param>
/// <param name="Schedule">
/// The typed Schedule of Activities directives (ADR-2475): visits added, removed, moved or re-windowed, activities added,
/// removed or collected more or less often, and eligibility criteria changed, with the elements and concepts each one
/// touches. Null when the amended content is not a typed protocol design.
/// </param>
public sealed record AmendmentImpact(
    ProtocolVersionId Amended,
    ProtocolVersionId Parent,
    IReadOnlyList<string> ImpactedElements,
    DependentsReport Dependents,
    IReadOnlyList<ElementChange> ChangedElements,
    SoaAmendmentDiff? Schedule);

/// <summary>
/// Computes amendment impact (spec §6.2, K-PROT-2) as a caller of <see cref="IKernelLineage.DependentsAsync"/>, so the
/// Impact verb and the amendment report count the same way (Canvas D2). An approved version's design elements are
/// lineage nodes (ADR-2474): the walk from the parent version follows only the elements the amendment changes or
/// removes, and everything that depends on the parent version as a whole.
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
