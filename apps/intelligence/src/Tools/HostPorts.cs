using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>
/// Dry-run authorization for an agent's requester (ADR-2633 §4.9, ADR-2483): whether the requester could exercise a
/// capability on an object now, explained, with the agent's own limits as conditions. The kernel's
/// <c>IAuthorizationProbe</c> answers only for the ambient actor and lives in Kernel.Application, which a module may
/// not reference (K-XS-2), so the host implements this port (ADR-2632). The capability's verb class comes from the
/// kernel's command catalogue, never from the model.
/// </summary>
public interface IAgentDryRun
{
    /// <summary>The decision, or null when the host does not provide dry runs yet.</summary>
    ValueTask<AgentDryRunAnswer?> CanIAsync(AgentPrincipal principal, string capability, NodeRef resource, CancellationToken cancellationToken);
}

/// <summary>A dry-run decision with the kernel's explanation (ADR-2482).</summary>
/// <param name="KnownCapability">False when no kernel command declares the capability.</param>
/// <param name="Permitted">Whether the requester could exercise it now.</param>
/// <param name="Reason">Why not, when it is denied.</param>
/// <param name="Explanation">The decision explained, when the kernel produced one.</param>
public sealed record AgentDryRunAnswer(bool KnownCapability, bool Permitted, string? Reason, Explanation? Explanation);

/// <summary>
/// Evaluate-only simulation for an agent's requester (ADR-2633 §4.9, ADR-2480). The kernel's
/// <see cref="ISimulationEngine"/> runs as the ambient actor, so the host implements this port to run it as the agent
/// for its requester (ADR-2632). It never writes (K-SIM-1).
/// </summary>
public interface IAgentSimulation
{
    /// <summary>The result, or null when the host does not simulate for agents yet.</summary>
    ValueTask<CommandResult<SimulationResult>?> SimulateAsync(AgentPrincipal principal, SimulationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// A producer of explanations for one kind of object (ADR-2482, K-EXP-1). The <c>explain</c> tool renders what the
/// owning component produced and never composes an explanation itself.
/// </summary>
public interface IExplanationSource
{
    /// <summary>The <see cref="NodeKinds"/> value this source explains.</summary>
    string Kind { get; }

    /// <summary>The explanation of <paramref name="node"/>, or null when it does not exist in the tenant.</summary>
    Task<Explanation?> ExplainAsync(AgentPrincipal principal, NodeRef node, CancellationToken cancellationToken);
}

/// <summary>The default <see cref="IAgentDryRun"/> until the host provides one: not available, never permitted.</summary>
internal sealed class UnavailableAgentDryRun : IAgentDryRun
{
    public ValueTask<AgentDryRunAnswer?> CanIAsync(AgentPrincipal principal, string capability, NodeRef resource, CancellationToken cancellationToken) =>
        ValueTask.FromResult<AgentDryRunAnswer?>(null);
}

/// <summary>The default <see cref="IAgentSimulation"/> until the host provides one: not available.</summary>
internal sealed class UnavailableAgentSimulation : IAgentSimulation
{
    public ValueTask<CommandResult<SimulationResult>?> SimulateAsync(AgentPrincipal principal, SimulationRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<CommandResult<SimulationResult>?>(null);
}
