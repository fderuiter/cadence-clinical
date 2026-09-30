using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api;

/// <summary>
/// One object in a lineage or dependents result (ADR-2471). A masked object keeps its place and kind; its
/// <see cref="Ref"/> is null and edges name it by its opaque <see cref="Handle"/>.
/// </summary>
/// <param name="Handle">The object's NodeRef string when visible, or <c>masked:n</c>, unique within one result.</param>
/// <param name="Kind">The object's kind, shown even when masked.</param>
/// <param name="Ref">The object's pinned NodeRef, or null when masked.</param>
/// <param name="Mask">Why the object is masked (<c>NotPermitted</c>, <c>Blinded</c>, <c>IdentityVault</c>), or null.</param>
/// <param name="Distance">Fewest edges from the starting object (0 for the start).</param>
public sealed record KernelLineageNodeResponse(string Handle, string Kind, string? Ref, string? Mask, int Distance)
{
    internal static KernelLineageNodeResponse From(LineageNode node) =>
        new(node.Handle, node.Kind, node.Ref?.ToString(), node.Mask?.ToString(), node.Distance);
}

/// <summary>One lineage edge between two handles.</summary>
/// <param name="From">The handle the edge leaves.</param>
/// <param name="To">The handle the edge reaches.</param>
/// <param name="Kind">The edge kind, for example <c>DerivedFrom</c>.</param>
/// <param name="Activity">The provenance activity that wrote the edge, withheld when either end is masked.</param>
/// <param name="RecordedAt">When the edge was recorded (transaction time).</param>
/// <param name="ValidFrom">When the edge takes effect (valid time), when it differs from the recording.</param>
public sealed record KernelLineageEdgeResponse(
    string From,
    string To,
    string Kind,
    Guid? Activity,
    DateTimeOffset RecordedAt,
    DateTimeOffset? ValidFrom)
{
    internal static KernelLineageEdgeResponse Of(LineageEdge edge) =>
        new(edge.From, edge.To, edge.Kind.ToString(), edge.ActivityId, edge.RecordedAt, edge.ValidFrom);
}

/// <summary>
/// A lineage walk (Trace): every object reached, masked or not, and every edge followed. <see cref="Truncated"/> is
/// true when the graph continues past the depth or the read's edge limit.
/// </summary>
/// <param name="Start">The handle of the starting object.</param>
/// <param name="Direction"><c>Backward</c> toward inputs or <c>Forward</c> toward dependents.</param>
/// <param name="Depth">How many edges were walked.</param>
/// <param name="ValidAt">The valid time the walk was read at.</param>
/// <param name="KnownAt">The transaction time the walk was read at.</param>
/// <param name="Nodes">Every object reached.</param>
/// <param name="Edges">Every edge followed.</param>
/// <param name="Truncated">Whether the graph continues past what was returned.</param>
public sealed record KernelLineageResponse(
    string Start,
    string Direction,
    int Depth,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    IReadOnlyList<KernelLineageNodeResponse> Nodes,
    IReadOnlyList<KernelLineageEdgeResponse> Edges,
    bool Truncated)
{
    internal static KernelLineageResponse From(LineageGraph graph) =>
        new(
            graph.Start,
            graph.Direction.ToString(),
            graph.Depth,
            graph.ValidAt,
            graph.KnownAt,
            [.. graph.Nodes.Select(KernelLineageNodeResponse.From)],
            [.. graph.Edges.Select(KernelLineageEdgeResponse.Of)],
            graph.Truncated);
}

/// <summary>The dependents of one kind. Masked objects are counted like any other, so a count never depends on the reader.</summary>
/// <param name="Kind">The objects' kind.</param>
/// <param name="Count">How many distinct objects of this kind depend on the start.</param>
/// <param name="Objects">Each object once, nearest first.</param>
public sealed record KernelDependentGroupResponse(string Kind, int Count, IReadOnlyList<KernelLineageNodeResponse> Objects);

/// <summary>
/// What depends on an object (Impact, Canvas D2), grouped by kind. When <see cref="Truncated"/> is true the counts
/// are a lower bound.
/// </summary>
/// <param name="Start">The handle of the object whose dependents were read.</param>
/// <param name="Depth">How many edges downstream were counted.</param>
/// <param name="ValidAt">The valid time the read was made at.</param>
/// <param name="KnownAt">The transaction time the read was made at.</param>
/// <param name="Groups">The dependents by kind, in ordinal order of kind.</param>
/// <param name="Total">How many objects depend on the start.</param>
/// <param name="Truncated">Whether the graph continues past what was counted.</param>
public sealed record KernelDependentsResponse(
    string Start,
    int Depth,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    IReadOnlyList<KernelDependentGroupResponse> Groups,
    int Total,
    bool Truncated)
{
    internal static KernelDependentsResponse From(DependentsReport report) =>
        new(
            report.Start,
            report.Depth,
            report.ValidAt,
            report.KnownAt,
            [.. report.Groups.Select(g => new KernelDependentGroupResponse(g.Kind, g.Count, [.. g.Objects.Select(KernelLineageNodeResponse.From)]))],
            report.Total,
            report.Truncated);
}

/// <summary>The impact of a protocol amendment (K-PROT-2): the elements it changes and what depends on them.</summary>
/// <param name="Amended">The NodeRef of the version that carries the amendment.</param>
/// <param name="Parent">The NodeRef of the approved version it amends.</param>
/// <param name="ImpactedElements">The protocol elements the amendment declares it changes.</param>
/// <param name="Dependents">
/// Everything downstream of the elements the amendment changes or removes, and of the parent as a whole (ADR-2474),
/// counted the same way as Impact.
/// </param>
/// <param name="ChangedElements">The elements the amended content adds, changes or removes, by the structural diff.</param>
public sealed record KernelAmendmentImpactResponse(
    string Amended,
    string Parent,
    IReadOnlyList<string> ImpactedElements,
    KernelDependentsResponse Dependents,
    IReadOnlyList<KernelElementChangeResponse> ChangedElements)
{
    internal static KernelAmendmentImpactResponse From(AmendmentImpact impact) =>
        new(
            NodeRef.For(impact.Amended).ToString(),
            NodeRef.For(impact.Parent).ToString(),
            impact.ImpactedElements,
            KernelDependentsResponse.From(impact.Dependents),
            [.. impact.ChangedElements.Select(c => new KernelElementChangeResponse(c.ElementId, c.InstanceType, c.Change.ToString(), c.Properties))]);
}

/// <summary>One element an amendment adds, changes or removes.</summary>
/// <param name="ElementId">The element's stable id in the design.</param>
/// <param name="InstanceType">The element's kind, when the document names one.</param>
/// <param name="Change">Added, Changed or Removed.</param>
/// <param name="Properties">For a changed element, the JSON Pointers of its changed values.</param>
public sealed record KernelElementChangeResponse(string ElementId, string? InstanceType, string Change, IReadOnlyList<string> Properties);
