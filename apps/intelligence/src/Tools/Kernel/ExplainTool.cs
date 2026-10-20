using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.References;

namespace Cadence.Modules.Intelligence.Tools.Kernel;

/// <summary>
/// <c>explain</c>: why an object is what it is, as its owning component explains it (ADR-2482, K-EXP-1). Evidence the
/// principal may not see is counted, not shown.
/// </summary>
internal sealed class ExplainTool(IEnumerable<IExplanationSource> sources, IAgentVisibility visibility)
    : AgentTool<NodeArguments, ExplanationView>(IntelligenceToolsJsonContext.Default.NodeArguments, IntelligenceToolsJsonContext.Default.ExplanationView, Schema)
{
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "node": { "type": "string", "description": "The NodeRef to explain, for example validation_finding:<uuid>." }
          },
          "required": ["node"],
          "additionalProperties": false
        }
        """;

    // A host's source for a kind replaces the module's own, whatever the registration order.
    private readonly Dictionary<string, IExplanationSource> byKind = sources
        .GroupBy(s => s.Kind, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.LastOrDefault(s => s is not ValidationFindingExplanations) ?? g.Last(), StringComparer.Ordinal);

    public override string Name => KernelToolNames.Explain;

    public override string Description =>
        "Explains why an object holds, as the component that owns it reasons: the claim, each step with its evidence, the " +
        "conditions checked and what would change the outcome. Today it explains validation findings.";

    protected override string? Validate(NodeArguments input) => ToolNodes.Problem(input.Node);

    protected override async ValueTask<ToolResult<ExplanationView>> ExecuteAsync(AgentToolContext context, NodeArguments input, CancellationToken cancellationToken)
    {
        var node = NodeRef.Parse(input.Node);
        if (!byKind.TryGetValue(node.Kind, out var source))
        {
            return ToolResult<ExplanationView>.Failure(
                ToolErrors.NotExplainable, $"No explanation is available for {node.Kind}. Explainable kinds: {string.Join(", ", byKind.Keys.Order(StringComparer.Ordinal))}.");
        }

        var masks = await visibility.MaskAsync(context.Principal, [node], cancellationToken).ConfigureAwait(false);
        if (masks.Count > 0 || await source.ExplainAsync(context.Principal, node, cancellationToken).ConfigureAwait(false) is not { } explanation)
        {
            return ToolResult<ExplanationView>.NotFoundOrNotVisible();
        }

        context.ReadSet.Record(node);
        return await ToolNodes.ViewAsync(explanation, context, visibility, cancellationToken).ConfigureAwait(false);
    }
}
