using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;

namespace Cadence.Modules.Intelligence.Tools.Kernel;

/// <summary>
/// <c>list_children</c>: a scope object's children with their own counts. A hidden parent is refused, because even a
/// masked list would say how many children it has (#5033); a hidden child is masked in place.
/// </summary>
internal sealed class ListChildrenTool(IKernelNavigator navigator, IAgentVisibility visibility)
    : AgentTool<NodeArguments, ChildrenView>(IntelligenceToolsJsonContext.Default.NodeArguments, IntelligenceToolsJsonContext.Default.ChildrenView, Schema)
{
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "node": { "type": "string", "description": "The NodeRef of a scope object: a tenant, study, country, study site or participant." }
          },
          "required": ["node"],
          "additionalProperties": false
        }
        """;

    public override string Name => KernelToolNames.ListChildren;

    public override string Description =>
        "Lists the children of a scope object (a study's sites, a site's participants) with their child counts. " +
        "Children you may not see are shown as masked with their kind only.";

    protected override string? Validate(NodeArguments input) => ToolNodes.Problem(input.Node);

    protected override async ValueTask<ToolResult<ChildrenView>> ExecuteAsync(AgentToolContext context, NodeArguments input, CancellationToken cancellationToken)
    {
        var node = NodeRef.Parse(input.Node);
        var tenant = context.Principal.Tenant;
        if (await navigator.ResolveAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } parent)
        {
            return ToolResult<ChildrenView>.NotFoundOrNotVisible();
        }

        var parentMask = await visibility.MaskAsync(context.Principal, [parent.Ref], cancellationToken).ConfigureAwait(false);
        if (parentMask.Count > 0
            || await navigator.GetChildrenAsync(tenant, parent.Ref, cancellationToken).ConfigureAwait(false) is not { } children)
        {
            return ToolResult<ChildrenView>.NotFoundOrNotVisible();
        }

        context.ReadSet.Record(parent.Ref);
        var masks = await visibility.MaskAsync(context.Principal, [.. children.Select(c => c.Node.Ref)], cancellationToken).ConfigureAwait(false);
        var views = children.Select(c =>
        {
            if (masks.TryGetValue(c.Node.Ref, out var reason))
            {
                return new ChildView(c.Node.Ref.Kind, null, null, null, null, null, c.Group, ToolNodes.MaskName(reason));
            }

            context.ReadSet.Record(c.Node.Ref);
            return new ChildView(c.Node.Ref.Kind, c.Node.Ref.ToString(), c.Node.Title, c.Node.Code, c.Node.Status, c.ChildCount, c.Group, null);
        }).ToList();
        return new ChildrenView(parent.Ref.ToString(), views);
    }
}
