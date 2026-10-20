using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Modules.Intelligence.Grounding;

namespace Cadence.Modules.Intelligence.Tools.Kernel;

/// <summary>
/// <c>get_node</c>: one object's summary and zoom path; an observation also carries its current value, which the run
/// records so a claim can quote it (K-AI-4). A hidden object reads as not found.
/// </summary>
internal sealed class GetNodeTool(IKernelNavigator navigator, IObservationRecords observations, IAgentVisibility visibility)
    : AgentTool<NodeArguments, NodeView>(IntelligenceToolsJsonContext.Default.NodeArguments, IntelligenceToolsJsonContext.Default.NodeView, Schema)
{
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "node": { "type": "string", "description": "A NodeRef such as observation:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e or study:<uuid>@3." }
          },
          "required": ["node"],
          "additionalProperties": false
        }
        """;

    public override string Name => KernelToolNames.GetNode;

    public override string Description =>
        "Reads one Cadence object by NodeRef: its title, code, status and where it sits (tenant, study, site, participant). " +
        "For an observation it returns the current value; quote that value exactly when you cite it.";

    protected override string? Validate(NodeArguments input) => ToolNodes.Problem(input.Node);

    protected override async ValueTask<ToolResult<NodeView>> ExecuteAsync(AgentToolContext context, NodeArguments input, CancellationToken cancellationToken)
    {
        var node = NodeRef.Parse(input.Node);
        var tenant = context.Principal.Tenant;

        ObservationRecord? observation = null;
        NodeSummary? summary;
        if (node.Kind == NodeKinds.Observation)
        {
            observation = await observations.FindAsync(tenant, new ObservationId(node.Id), cancellationToken).ConfigureAwait(false);
            if (observation is null || (node.Version is { } wanted && wanted != observation.Version))
            {
                return ToolResult<NodeView>.NotFoundOrNotVisible();
            }

            summary = new NodeSummary(new NodeRef(NodeKinds.Observation, node.Id, observation.Version), $"Observation of concept {observation.Concept}");
        }
        else
        {
            summary = await navigator.ResolveAsync(tenant, node, cancellationToken).ConfigureAwait(false);
        }

        if (summary is null)
        {
            return ToolResult<NodeView>.NotFoundOrNotVisible();
        }

        var path = await navigator.GetZoomPathAsync(tenant, summary.Ref, cancellationToken).ConfigureAwait(false) ?? [];
        var refs = path.Where(s => s.Ref is not null).Select(s => s.Ref!).Append(summary.Ref).Distinct().ToList();
        var masks = await visibility.MaskAsync(context.Principal, refs, cancellationToken).ConfigureAwait(false);
        if (masks.ContainsKey(summary.Ref))
        {
            return ToolResult<NodeView>.NotFoundOrNotVisible();
        }

        var steps = new List<PathStepView>(path.Count);
        foreach (var step in path)
        {
            if (step.Ref is { } stepRef && masks.TryGetValue(stepRef, out var reason))
            {
                steps.Add(new PathStepView(step.Level, null, null, ToolNodes.MaskName(reason)));
                continue;
            }

            if (step.Ref is { } shown)
            {
                context.ReadSet.Record(shown);
            }

            steps.Add(new PathStepView(step.Level, step.Label, step.Ref?.ToString(), null));
        }

        ObservationView? value = null;
        if (observation is not null)
        {
            var valueJson = JsonSerializer.Serialize(observation.Value, IntelligenceToolsJsonContext.Default.ObservationValue);
            context.ReadSet.RecordValue(summary.Ref, ValueFingerprint.Of(valueJson)!);
            value = new ObservationView(
                observation.Value, observation.Concept.ToString(), observation.ObservedAt, observation.Version,
                observation.Revisions.Count, observation.Locks.Count);
        }
        else
        {
            context.ReadSet.Record(summary.Ref);
        }

        return new NodeView(summary.Ref.ToString(), summary.Ref.Kind, summary.Title, summary.Code, summary.Status, steps, value);
    }
}
