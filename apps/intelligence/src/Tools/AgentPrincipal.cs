using System;

using Cadence.Kernel;
using Cadence.Kernel.Commands;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>
/// Who a tool call reads as (ADR-2633 §4.1, K-AI-3): an automation agent acting for one person, in one tenant, for one
/// run. Every tool takes its tenant and readers from here, never from the model's arguments.
/// </summary>
public sealed record AgentPrincipal
{
    /// <summary>Creates the principal for one run.</summary>
    /// <param name="tenant">The requester's tenant.</param>
    /// <param name="agent">The automation agent.</param>
    /// <param name="requester">The person the agent acts for.</param>
    /// <param name="runId">The run.</param>
    /// <exception cref="ArgumentException">The tenant or run is empty, the agent is not an automation agent, or the requester is not a person.</exception>
    public AgentPrincipal(TenantId tenant, Actor agent, Actor requester, Guid runId)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(requester);
        if (tenant.IsEmpty)
        {
            throw new ArgumentException("An agent run belongs to one tenant.", nameof(tenant));
        }

        if (agent.Kind != ActorKind.AutomationAgent)
        {
            throw new ArgumentException("The acting principal must be an automation agent.", nameof(agent));
        }

        if (requester.Kind != ActorKind.Person)
        {
            throw new ArgumentException("An agent acts for a person, never for a service account or another agent.", nameof(requester));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("An agent run needs an id.", nameof(runId));
        }

        Tenant = tenant;
        Agent = agent.AsIdentity();
        Requester = requester;
        RunId = runId;
    }

    /// <summary>The tenant every read is scoped to, taken from the requester's sign-in (#5077).</summary>
    public TenantId Tenant { get; }

    /// <summary>The agent as an identity (<c>agent:&lt;name&gt;</c>), as grants and proposals name it.</summary>
    public Actor Agent { get; }

    /// <summary>
    /// The agent acting for <see cref="Requester"/> (K-AI-6, ADR-2632): the actor the kernel masks reads for and
    /// authorizes commands as, holding only what both the agent and the person hold. An agent acting for nobody is
    /// refused everything.
    /// </summary>
    public Actor Acting => Agent.ActingFor(Requester);

    /// <summary>The person the agent works for; the agent never reads more than this person may.</summary>
    public Actor Requester { get; }

    /// <summary>The run this call belongs to.</summary>
    public Guid RunId { get; }
}
