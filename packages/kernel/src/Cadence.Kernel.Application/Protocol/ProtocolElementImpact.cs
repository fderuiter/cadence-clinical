using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;

namespace Cadence.Kernel.Application.Protocol;

/// <summary>
/// Which design elements a protocol change impacts (ADR-2474): amendment impact and simulation walk from the base
/// version into these elements only, so both count the same dependents (Canvas D2).
/// </summary>
public static class ProtocolElementImpact
{
    /// <summary>
    /// The elements <paramref name="changes"/> changes or removes, as unversioned references. Added elements have no
    /// node under the base version, and the design root (<see cref="ProtocolStructuralDiff.RootId"/>) is not an element.
    /// </summary>
    public static IReadOnlySet<NodeRef> Impacted(StudyId study, IEnumerable<ElementChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return changes
            .Where(c => c.Change != ElementChangeKind.Added && c.ElementId != ProtocolStructuralDiff.RootId)
            .Select(c => NodeRef.For(ProtocolElementId.For(study, c.ElementId)))
            .ToHashSet();
    }

    /// <summary>
    /// The rule for edges that leave the base version: follow every edge except one into a design element that is not in
    /// <paramref name="impacted"/>.
    /// </summary>
    public static Func<NodeRef, bool> FollowFromBase(IReadOnlySet<NodeRef> impacted)
    {
        ArgumentNullException.ThrowIfNull(impacted);
        return node => !string.Equals(node.Kind, NodeKinds.ProtocolElement, StringComparison.Ordinal) || impacted.Contains(node.Unversioned());
    }
}
