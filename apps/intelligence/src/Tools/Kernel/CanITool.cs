using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.References;

namespace Cadence.Modules.Intelligence.Tools.Kernel;

/// <summary>
/// <c>can_i</c>: a dry run of one capability on one object for the agent's requester (ADR-2483), explained. It
/// executes nothing. The host answers through <see cref="IAgentDryRun"/> (ADR-2632).
/// </summary>
internal sealed class CanITool(IAgentDryRun dryRun, IAgentVisibility visibility)
    : AgentTool<CanIArguments, CanIView>(IntelligenceToolsJsonContext.Default.CanIArguments, IntelligenceToolsJsonContext.Default.CanIView, Schema)
{
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "capability": { "type": "string", "description": "A kernel capability token, for example observation.correct or query.raise." },
            "node": { "type": "string", "description": "The NodeRef it would be exercised on." }
          },
          "required": ["capability", "node"],
          "additionalProperties": false
        }
        """;

    public override string Name => KernelToolNames.CanI;

    public override string Description =>
        "Checks, without doing anything, whether the person you work for could exercise a capability on an object now, and " +
        "why. Use it to name the capabilities a proposal would need. You can never execute or approve yourself.";

    protected override string? Validate(CanIArguments input) =>
        !Capability.IsWellFormed(input.Capability) ? $"'{input.Capability}' is not a capability token." : ToolNodes.Problem(input.Node);

    protected override async ValueTask<ToolResult<CanIView>> ExecuteAsync(AgentToolContext context, CanIArguments input, CancellationToken cancellationToken)
    {
        var node = NodeRef.Parse(input.Node);
        var masks = await visibility.MaskAsync(context.Principal, [node], cancellationToken).ConfigureAwait(false);
        if (masks.Count > 0)
        {
            return ToolResult<CanIView>.NotFoundOrNotVisible();
        }

        if (await dryRun.CanIAsync(context.Principal, input.Capability, node, cancellationToken).ConfigureAwait(false) is not { } answer)
        {
            return ToolResult<CanIView>.Failure(ToolErrors.NotAvailable, "Dry-run authorization is not available to agents yet.");
        }

        if (!answer.KnownCapability)
        {
            return ToolResult<CanIView>.Failure(ToolErrors.InvalidArguments, $"No kernel command declares the capability '{input.Capability}'.");
        }

        var explanation = answer.Explanation is null
            ? null
            : await ToolNodes.ViewAsync(answer.Explanation, context, visibility, cancellationToken).ConfigureAwait(false);
        return new CanIView(input.Capability, node.ToString(), answer.Permitted, answer.Reason, explanation);
    }
}
