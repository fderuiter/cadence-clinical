using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Lineage;
using Cadence.Kernel.References;

namespace Cadence.Modules.Intelligence.Tools.Kernel;

/// <summary>
/// <c>trace_lineage</c>: walks the lineage graph backward to an object's inputs or forward to its dependents
/// (spec §9.6, ADR-2540). The kernel masks for the requester; this tool then masks what the agent may not see, in
/// place, so a trace never looks shorter than it is (ADR-2471). A hidden starting object reads as not found.
/// </summary>
internal sealed class TraceLineageTool(IKernelLineage lineage, IAgentVisibility visibility)
    : AgentTool<TraceArguments, LineageView>(IntelligenceToolsJsonContext.Default.TraceArguments, IntelligenceToolsJsonContext.Default.LineageView, Schema)
{
    /// <summary>The deepest walk an agent may ask for; deeper graphs come back truncated.</summary>
    public const int MaxDepth = 10;

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "node": { "type": "string", "description": "The NodeRef to start from." },
            "direction": { "type": "string", "enum": ["backward", "forward"], "default": "backward", "description": "backward walks to inputs and sources; forward walks to dependents." },
            "depth": { "type": "integer", "minimum": 1, "maximum": 10, "default": 3 },
            "edgeKinds": { "type": "array", "items": { "type": "string", "enum": ["RequiredBy", "DefinedBy", "CollectedBy", "DerivedFrom", "MappedBy", "TransformedBy", "ValidatedBy", "AttestedBy", "GovernedBy", "Supersedes"] } }
          },
          "required": ["node"],
          "additionalProperties": false
        }
        """;

    public override string Name => KernelToolNames.TraceLineage;

    public override string Description =>
        "Traces where an object came from (backward: the specification it was collected under, the values it was derived " +
        "from, the mapping that produced it) or what depends on it (forward). Objects you may not see stay in the trace as masked.";

    protected override string? Validate(TraceArguments input)
    {
        if (ToolNodes.Problem(input.Node) is { } problem)
        {
            return problem;
        }

        if (!TryDirection(input.Direction, out _))
        {
            return "direction must be backward or forward.";
        }

        if (input.Depth is < 1 or > MaxDepth)
        {
            return $"depth must be between 1 and {MaxDepth}.";
        }

        return input.EdgeKinds?.FirstOrDefault(k => !Enum.TryParse<LineageEdgeKind>(k, ignoreCase: false, out _)) is { } unknown
            ? $"'{unknown}' is not an edge kind."
            : null;
    }

    protected override async ValueTask<ToolResult<LineageView>> ExecuteAsync(AgentToolContext context, TraceArguments input, CancellationToken cancellationToken)
    {
        var start = NodeRef.Parse(input.Node);
        TryDirection(input.Direction, out var direction);
        var startMask = await visibility.MaskAsync(context.Principal, [start], cancellationToken).ConfigureAwait(false);
        if (startMask.Count > 0)
        {
            return ToolResult<LineageView>.NotFoundOrNotVisible();
        }

        IReadOnlySet<LineageEdgeKind>? kinds = input.EdgeKinds is { Count: > 0 } names
            ? names.Select(n => Enum.Parse<LineageEdgeKind>(n)).ToHashSet()
            : null;
        var graph = await lineage.TraceAsync(
            context.Principal.Tenant, context.Principal.Requester, new LineageQuery(start, direction, input.Depth, kinds), cancellationToken)
            .ConfigureAwait(false);

        // The requester may see more than the agent: mask, in place, what only the agent may not see.
        var visible = graph.Nodes.Where(n => n.Ref is not null).Select(n => n.Ref!).ToList();
        var agentMasks = await visibility.MaskAsync(context.Principal, visible, cancellationToken).ConfigureAwait(false);
        if (graph.Nodes.FirstOrDefault(n => n.Distance == 0) is { } first && (first.IsMasked || (first.Ref is not null && agentMasks.ContainsKey(first.Ref))))
        {
            return ToolResult<LineageView>.NotFoundOrNotVisible();
        }

        var handles = new Dictionary<string, string>(StringComparer.Ordinal);
        var next = graph.Nodes.Count(n => n.IsMasked);
        var nodes = new List<LineageNodeView>(graph.Nodes.Count);
        foreach (var node in graph.Nodes)
        {
            if (node.Ref is { } shown && agentMasks.TryGetValue(shown, out var reason))
            {
                var handle = string.Create(CultureInfo.InvariantCulture, $"masked:agent:{++next}");
                handles[node.Handle] = handle;
                nodes.Add(new LineageNodeView(handle, node.Kind, null, ToolNodes.MaskName(reason), node.Distance));
                continue;
            }

            if (node.Ref is { } read)
            {
                context.ReadSet.Record(read);
            }

            nodes.Add(new LineageNodeView(node.Handle, node.Kind, node.Ref?.ToString(), node.Mask is { } mask ? ToolNodes.MaskName(mask) : null, node.Distance));
        }

        var edges = graph.Edges
            .Select(e => new LineageEdgeView(handles.GetValueOrDefault(e.From, e.From), handles.GetValueOrDefault(e.To, e.To), e.Kind.ToString(), e.RecordedAt))
            .ToList();
        return new LineageView(handles.GetValueOrDefault(graph.Start, graph.Start), input.Direction, graph.Depth, nodes, edges, graph.Truncated);
    }

    private static bool TryDirection(string? text, out LineageDirection direction)
    {
        switch (text)
        {
            case "backward":
                direction = LineageDirection.Backward;
                return true;
            case "forward":
                direction = LineageDirection.Forward;
                return true;
            default:
                direction = LineageDirection.Backward;
                return false;
        }
    }
}
