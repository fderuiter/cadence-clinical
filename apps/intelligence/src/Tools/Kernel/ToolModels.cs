using System.Collections.Generic;

using Cadence.Kernel.Evidence;

namespace Cadence.Modules.Intelligence.Tools.Kernel;

// Tool arguments. NodeRefs travel as strings because the model writes them; each tool parses and refuses bad ones.

/// <summary>Arguments of <c>get_node</c>, <c>list_children</c> and <c>explain</c>.</summary>
public sealed record NodeArguments(string Node);

/// <summary>Arguments of <c>search_kernel</c>.</summary>
public sealed record SearchArguments(string Text, IReadOnlyList<string>? Kinds = null, int Limit = 10);

/// <summary>Arguments of <c>trace_lineage</c>.</summary>
public sealed record TraceArguments(string Node, string Direction = "backward", int Depth = 3, IReadOnlyList<string>? EdgeKinds = null);

/// <summary>Arguments of <c>can_i</c>.</summary>
public sealed record CanIArguments(string Capability, string Node);

/// <summary>Arguments of <c>simulate</c>: a stored candidate, or unsaved content over a base version.</summary>
public sealed record SimulateArguments(string? Candidate = null, string? BaseVersion = null, string? Content = null);

// Tool results. A masked object carries its kind and the reason, never its reference or title (ADR-2471).

/// <summary>One object as an agent sees it.</summary>
public sealed record NodeView(
    string Ref,
    string Kind,
    string Title,
    string? Code,
    string? Status,
    IReadOnlyList<PathStepView> Path,
    ObservationView? Observation);

/// <summary>One step of an object's zoom path.</summary>
public sealed record PathStepView(string Level, string? Label, string? Ref, string? Masked);

/// <summary>An observation's current value. Quote <see cref="Value"/> exactly when citing it.</summary>
public sealed record ObservationView(ObservationValue Value, string Concept, DateTimeOffset ObservedAt, long Version, int Revisions, int Locks);

/// <summary>A child of a scope object.</summary>
public sealed record ChildView(string Kind, string? Ref, string? Title, string? Code, string? Status, int? ChildCount, string? Group, string? Masked);

/// <summary>The children of a scope object.</summary>
public sealed record ChildrenView(string Parent, IReadOnlyList<ChildView> Children);

/// <summary>One search hit.</summary>
public sealed record SearchHitView(string Ref, string Kind, string Title, string? Code, string? Detail, bool ExactCode);

/// <summary>Search results.</summary>
public sealed record SearchView(IReadOnlyList<SearchHitView> Hits);

/// <summary>One object in a lineage walk; masked objects keep their place.</summary>
public sealed record LineageNodeView(string Handle, string Kind, string? Ref, string? Masked, int Distance);

/// <summary>One edge in a lineage walk.</summary>
public sealed record LineageEdgeView(string From, string To, string Kind, DateTimeOffset RecordedAt);

/// <summary>A lineage walk. <see cref="Truncated"/> means the graph continues past what was returned.</summary>
public sealed record LineageView(string Start, string Direction, int Depth, IReadOnlyList<LineageNodeView> Nodes, IReadOnlyList<LineageEdgeView> Edges, bool Truncated);

/// <summary>A step of an explanation, with the evidence the reader may see and a count of the evidence masked.</summary>
public sealed record ExplanationStepView(string Claim, IReadOnlyList<string> Evidence, int MaskedEvidence);

/// <summary>A condition the producer evaluated.</summary>
public sealed record ConditionView(string Code, string Description, bool Met, IReadOnlyList<string> Evidence, int MaskedEvidence);

/// <summary>What would change the outcome.</summary>
public sealed record NextActionView(string Description, string? Capability, string? Target);

/// <summary>An explanation (ADR-2482) as an agent sees it.</summary>
public sealed record ExplanationView(
    string? Subject,
    string Claim,
    string Producer,
    IReadOnlyList<ExplanationStepView> Steps,
    IReadOnlyList<ConditionView> Conditions,
    IReadOnlyList<NextActionView> NextActions);

/// <summary>A dry-run decision.</summary>
public sealed record CanIView(string Capability, string Resource, bool Permitted, string? Reason, ExplanationView? Explanation);

/// <summary>An element change in a simulation.</summary>
public sealed record ElementChangeView(string ElementId, string? InstanceType, string Change, IReadOnlyList<string> Properties);

/// <summary>A schedule change in a simulation.</summary>
public sealed record ScheduleChangeView(string ActivityId, string Change);

/// <summary>Dependents of one kind, counted including masked ones.</summary>
public sealed record DependentCountView(string Kind, int Count);

/// <summary>A simulation result, summarised for a model: the first 100 of each list and their totals.</summary>
public sealed record SimulationView(
    string Scope,
    string? Candidate,
    string? BaseVersion,
    string InputHash,
    string ResultHash,
    int StructuralChangeCount,
    IReadOnlyList<ElementChangeView> StructuralChanges,
    int ScheduleChangeCount,
    IReadOnlyList<ScheduleChangeView> ScheduleChanges,
    IReadOnlyList<string> MaskedParts,
    IReadOnlyList<DependentCountView> Dependents,
    int MatrixGapCount);
