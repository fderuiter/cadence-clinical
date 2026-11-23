using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;

namespace Cadence.Modules.Intelligence.Tools.Kernel;

/// <summary>
/// <c>simulate</c>: evaluates a candidate protocol version against its base without writing anything (ADR-2480,
/// K-SIM-1). The host runs it for the agent's requester through <see cref="IAgentSimulation"/> (ADR-2632).
/// </summary>
internal sealed class SimulateTool(IAgentSimulation simulation, IAgentVisibility visibility)
    : AgentTool<SimulateArguments, SimulationView>(IntelligenceToolsJsonContext.Default.SimulateArguments, IntelligenceToolsJsonContext.Default.SimulationView, Schema)
{
    /// <summary>The most entries of each list a result shows; the totals are always given.</summary>
    public const int MaxListed = 100;

    /// <summary>The largest unsaved candidate an agent may submit, in UTF-8 bytes.</summary>
    public const int MaxContentBytes = 512 * 1024;

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "candidate": { "type": "string", "description": "A stored candidate: protocol_version:<uuid> or protocol_version:<uuid>@<version>." },
            "baseVersion": { "type": "string", "description": "For unsaved content: the frozen protocol_version (optional @<version> pin) it changes." },
            "content": { "type": "string", "description": "For unsaved content: the candidate protocol design as USDM JSON." }
          },
          "additionalProperties": false
        }
        """;

    public override string Name => KernelToolNames.Simulate;

    public override string Description =>
        "Shows what a candidate protocol version would change compared with its base (elements, schedule, dependents, " +
        "matrix gaps) without changing anything. Give either candidate, or baseVersion with content.";

    protected override string? Validate(SimulateArguments input)
    {
        var stored = input.Candidate is not null;
        var unsaved = input.BaseVersion is not null || input.Content is not null;
        if (stored == unsaved)
        {
            return "Give either candidate, or baseVersion with content.";
        }

        if (stored)
        {
            return ProtocolVersionProblem(input.Candidate);
        }

        if (string.IsNullOrWhiteSpace(input.Content) || Encoding.UTF8.GetByteCount(input.Content) > MaxContentBytes)
        {
            return $"content must be 1 to {MaxContentBytes} bytes.";
        }

        return ProtocolVersionProblem(input.BaseVersion);
    }

    protected override async ValueTask<ToolResult<SimulationView>> ExecuteAsync(AgentToolContext context, SimulateArguments input, CancellationToken cancellationToken)
    {
        var versionNode = NodeRef.Parse(input.Candidate ?? input.BaseVersion!);
        var masks = await visibility.MaskAsync(
            context.Principal,
            [.. new[] { versionNode, versionNode.Unversioned() }.Distinct()],
            cancellationToken).ConfigureAwait(false);
        if (masks.Count > 0)
        {
            return ToolResult<SimulationView>.NotFoundOrNotVisible();
        }

        var version = new ProtocolVersionId(versionNode.Id);
        var candidate = input.Candidate is not null
            ? SimulationCandidate.StoredProtocolVersion(version, versionNode.Version)
            : SimulationCandidate.UnsavedProtocolVersion(version, input.Content!, versionNode.Version);
        if (await simulation.SimulateAsync(context.Principal, new SimulationRequest(candidate), cancellationToken).ConfigureAwait(false) is not { } outcome)
        {
            return ToolResult<SimulationView>.Failure(ToolErrors.NotAvailable, "Simulation is not available to agents yet.");
        }

        if (!outcome.IsSuccess)
        {
            return ToolResult<SimulationView>.Failure(ToolErrors.Refused, $"{outcome.Error!.Code}: {outcome.Error.Message}");
        }

        var result = outcome.Value!;
        foreach (var read in new[] { result.Scope, result.Candidate.Version, result.Candidate.BaseVersion }.OfType<NodeRef>())
        {
            context.ReadSet.Record(read);
        }

        return new SimulationView(
            result.Scope.ToString(),
            result.Candidate.Version?.ToString(),
            result.Candidate.BaseVersion?.ToString(),
            result.InputHash,
            result.ResultHash,
            result.StructuralDiff.Count,
            [.. result.StructuralDiff.Take(MaxListed).Select(c => new ElementChangeView(c.ElementId, c.InstanceType, c.Change.ToString(), c.Properties))],
            result.ScheduleChanges.Count,
            [.. result.ScheduleChanges.Take(MaxListed).Select(c => new ScheduleChangeView(c.ActivityId, c.Change.ToString()))],
            [.. result.Masked.Select(m => $"{m.Part} ({ToolNodes.MaskName(m.Reason)})")],
            [.. (result.Dependents?.Groups ?? []).Select(g => new DependentCountView(g.Kind, g.Count))],
            result.MatrixGaps?.Count ?? 0);
    }

    private static string? ProtocolVersionProblem(string? text) =>
        ToolNodes.Problem(text) ?? (NodeRef.Parse(text!).Kind == NodeKinds.ProtocolVersion ? null : "The version must be a protocol_version NodeRef.");
}
