using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Lineage;

/// <summary>
/// A lineage read (ADR-2471): <c>Lineage(node, direction, depth, edge_kinds, valid_at, known_at)</c>.
/// </summary>
/// <param name="Node">
/// Where the walk starts. With a version, that version only; without one, every stored version of the object.
/// </param>
/// <param name="Direction">Backward toward inputs, or forward toward dependents.</param>
/// <param name="Depth">How many edges away from <paramref name="Node"/> to walk, 1 to <see cref="MaxDepth"/>.</param>
/// <param name="EdgeKinds">The edge kinds to follow; null or empty follows every kind.</param>
/// <param name="ValidAt">Valid time: edges that take effect after it are not followed. Defaults to <paramref name="KnownAt"/>.</param>
/// <param name="KnownAt">Transaction time: edges recorded after it are not followed. Defaults to now.</param>
public sealed record LineageQuery(
    NodeRef Node,
    LineageDirection Direction,
    int Depth = LineageQuery.DefaultDepth,
    IReadOnlySet<LineageEdgeKind>? EdgeKinds = null,
    DateTimeOffset? ValidAt = null,
    DateTimeOffset? KnownAt = null)
{
    /// <summary>Depth used when the caller names none.</summary>
    public const int DefaultDepth = 10;

    /// <summary>The deepest walk one read may ask for.</summary>
    public const int MaxDepth = 50;
}

/// <summary>
/// One object in a lineage result. A visible object carries its <see cref="Ref"/>; an object the reader may not see
/// comes back masked: <see cref="Ref"/> is null, <see cref="Mask"/> says why, and <see cref="Handle"/> is an opaque
/// placeholder (<c>masked:1</c>) that edges use instead of its reference. Masked objects are never dropped, so a trace
/// through a blinded object keeps its length.
/// </summary>
/// <param name="Handle">The node's reference string when visible, or <c>masked:n</c>, unique within one result.</param>
/// <param name="Kind">The object's kind (<see cref="NodeKinds"/>), shown even when masked.</param>
/// <param name="Ref">The object's pinned reference, or null when masked.</param>
/// <param name="Mask">Why the object is masked, or null when visible.</param>
/// <param name="Distance">Fewest edges between the starting object and this one (0 for the start).</param>
public sealed record LineageNode(string Handle, string Kind, NodeRef? Ref, MaskReason? Mask, int Distance)
{
    /// <summary>True when the reader may not see the object.</summary>
    public bool IsMasked => Mask is not null;
}

/// <summary>
/// One edge in a lineage result, between two <see cref="LineageNode.Handle"/>s. <see cref="ActivityId"/> (the
/// ProvenanceActivity that wrote the edge) is withheld when either end is masked, because the activity names the
/// object it wrote.
/// </summary>
public sealed record LineageEdge(
    string From,
    string To,
    LineageEdgeKind Kind,
    Guid? ActivityId,
    DateTimeOffset RecordedAt,
    DateTimeOffset? ValidFrom);

/// <summary>
/// The result of a lineage read: every object reached, masked or not, and every edge followed.
/// <see cref="Truncated"/> is true when the graph continues past <see cref="Depth"/> or past the read's edge limit,
/// so a reader never mistakes a cut-off trace for a complete one.
/// </summary>
public sealed record LineageGraph(
    string Start,
    LineageDirection Direction,
    int Depth,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    IReadOnlyList<LineageNode> Nodes,
    IReadOnlyList<LineageEdge> Edges,
    bool Truncated);

/// <summary>
/// An evidence dossier (spec §9.6 "inspection readiness"): everything upstream and downstream of one object, with
/// the objects grouped by kind.
/// </summary>
public sealed record LineageDossier(
    LineageGraph Backward,
    LineageGraph Forward,
    IReadOnlyDictionary<string, IReadOnlyList<LineageNode>> ByKind);

/// <summary>
/// A dependents read (Canvas D2, PX-6): <c>Dependents(node, depth, edge_kinds, valid_at, known_at)</c>, the objects
/// downstream of <paramref name="Node"/>. Every screen that shows impact counts through this one read, so their
/// numbers agree.
/// </summary>
/// <param name="Node">
/// The object whose dependents are wanted. With a version, what was produced from that version onwards (the walk
/// follows <see cref="LineageEdgeKind.Supersedes"/> like any other kind unless <paramref name="EdgeKinds"/> leaves it
/// out); without one, what depends on any version.
/// </param>
/// <param name="Depth">How many edges downstream to count, 1 to <see cref="LineageQuery.MaxDepth"/>.</param>
/// <param name="EdgeKinds">The edge kinds to follow; null or empty follows every kind.</param>
/// <param name="ValidAt">Valid time; defaults to <paramref name="KnownAt"/>.</param>
/// <param name="KnownAt">Transaction time; defaults to now.</param>
public sealed record DependentsQuery(
    NodeRef Node,
    int Depth = LineageQuery.DefaultDepth,
    IReadOnlySet<LineageEdgeKind>? EdgeKinds = null,
    DateTimeOffset? ValidAt = null,
    DateTimeOffset? KnownAt = null);

/// <summary>
/// The dependents of one kind. <see cref="Count"/> counts objects, not versions, and masked objects are counted like
/// any other (D1), so a count never depends on who is reading.
/// </summary>
/// <param name="Kind">The objects' kind (<see cref="NodeKinds"/>).</param>
/// <param name="Count">How many distinct objects of this kind depend on the start.</param>
/// <param name="Objects">
/// Each object once, at the latest version the walk reached, nearest first. <see cref="LineageNode.Distance"/> is the
/// fewest edges from the start to any version of the object.
/// </param>
public sealed record DependentGroup(string Kind, int Count, IReadOnlyList<LineageNode> Objects);

/// <summary>
/// The result of a dependents read: the downstream objects grouped by kind (ordinal order of kind), and their total.
/// The starting object itself is never its own dependent. <see cref="Truncated"/> is true when the graph continues
/// past <see cref="Depth"/> or the read's edge limit, so the counts are then a lower bound.
/// </summary>
public sealed record DependentsReport(
    string Start,
    int Depth,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    IReadOnlyList<DependentGroup> Groups,
    int Total,
    bool Truncated);

/// <summary>
/// Reads the tenant's lineage graph (spec §9.6, ADR-2255, ADR-2471). Every read names its reader: objects the reader
/// may not see (decided by the host's <see cref="INodeVisibility"/>) are returned masked, never omitted. Edges are
/// written only by the command pipeline, in the transaction of the object they describe (K-LIN-2).
/// </summary>
public interface IKernelLineage
{
    /// <summary>Walks the graph from <see cref="LineageQuery.Node"/>.</summary>
    Task<LineageGraph> TraceAsync(TenantId tenant, Actor reader, LineageQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// The evidence dossier for <paramref name="node"/>: a backward and a forward walk to <paramref name="depth"/>
    /// at the same <c>(valid_at, known_at)</c>, merged and grouped by kind.
    /// </summary>
    Task<LineageDossier> DossierAsync(
        TenantId tenant,
        Actor reader,
        NodeRef node,
        int depth = LineageQuery.DefaultDepth,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>What depends on <see cref="DependentsQuery.Node"/>, grouped by kind with counts (Canvas D2).</summary>
    Task<DependentsReport> DependentsAsync(TenantId tenant, Actor reader, DependentsQuery query, CancellationToken cancellationToken = default);
}

/// <summary>An object version that is derived but has no inbound lineage edge (K-LIN-1).</summary>
public sealed record LineageOrphan(TenantId Tenant, NodeRef Node, Guid ActivityId, DateTimeOffset RecordedAt);

/// <summary>
/// Receives the scheduled K-LIN-1 check's results. The kernel's default logs them; the Quality engine replaces it to
/// raise ValidationFindings (#4812).
/// </summary>
public interface ILineageOrphanSink
{
    Task ReportAsync(TenantId tenant, IReadOnlyList<LineageOrphan> orphans, CancellationToken cancellationToken);
}
