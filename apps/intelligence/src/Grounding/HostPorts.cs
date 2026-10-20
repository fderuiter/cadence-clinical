using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Modules.Intelligence.Tools;

namespace Cadence.Modules.Intelligence.Grounding;

/// <summary>
/// Stores a plan and records it as a kernel artifact version (ADR-2633 §4.9). Registering an artifact is an Execute
/// verb, which the kernel refuses to agents, so the host decides who records it for the run (ADR-2632). The stored
/// bytes must be exactly <see cref="PlanContent.Utf8"/>, so the approver's attestation covers what the agent wrote.
/// </summary>
public interface IPlanArtifactStore
{
    /// <summary>Stores <paramref name="plan"/> under <paramref name="study"/> for the run and returns its artifact version.</summary>
    /// <param name="principal">The agent and requester of the run.</param>
    /// <param name="study">The study the plan belongs to.</param>
    /// <param name="plan">The plan's bytes, hash and media type.</param>
    /// <param name="cancellationToken">Cancels the store.</param>
    Task<CommandResult<ArtifactVersionId>> StoreAsync(AgentPrincipal principal, StudyId study, PlanContent plan, CancellationToken cancellationToken);
}

/// <summary>A plan's bytes, their <c>sha256:</c> hash and media type.</summary>
public sealed record PlanContent(byte[] Utf8, string ContentHash, string MediaType);

/// <summary>
/// Sends a <see cref="Propose"/> as the agent for its requester (ADR-2633 §4.9, ADR-2632). The kernel authorizes it
/// as a Propose verb, refuses objects the proposer may not see (<c>K.Proposal.NotVisible</c>) and requires the model
/// id (K-PROV-3).
/// </summary>
public interface IAgentProposalGateway
{
    /// <summary>Sends <paramref name="command"/> as the agent for the requester and returns the new proposal.</summary>
    /// <param name="principal">The agent and requester of the run.</param>
    /// <param name="command">The proposal to record.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task<CommandResult<ProposalState>> ProposeAsync(AgentPrincipal principal, Propose command, CancellationToken cancellationToken);
}
