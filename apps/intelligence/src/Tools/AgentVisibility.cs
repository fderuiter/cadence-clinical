using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.References;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>
/// Decides which objects an agent may see for its requester (ADR-2633 §4.2, K-AI-3). An object is visible only when
/// both the requester and the agent may see it, so an agent never reads wider than the person it works for.
/// </summary>
public interface IAgentVisibility
{
    /// <summary>The objects in <paramref name="nodes"/> the principal may not see, with the reason; the rest are visible.</summary>
    ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        AgentPrincipal principal, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IAgentVisibility"/> over the host's <see cref="INodeVisibility"/>: it asks about the requester and about
/// the agent acting for the requester (<see cref="AgentPrincipal.Acting"/>, whose grants the kernel already intersects
/// with the person's, ADR-2632) and masks a node when either may not see it. Asking about the person too keeps the
/// bound when a visibility rule (blinding, the identity vault) does not look at the agent's requester.
/// </summary>
internal sealed class RequesterBoundVisibility(INodeVisibility visibility) : IAgentVisibility
{
    public async ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        AgentPrincipal principal, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
    {
        if (nodes.Count == 0)
        {
            return new Dictionary<NodeRef, MaskReason>();
        }

        var forRequester = await visibility.MaskAsync(principal.Tenant, principal.Requester, nodes, cancellationToken).ConfigureAwait(false);
        var forAgent = await visibility.MaskAsync(principal.Tenant, principal.Acting, nodes, cancellationToken).ConfigureAwait(false);

        // The requester's reason wins: it is the one a person would get, and it never says more than the agent's.
        var masks = new Dictionary<NodeRef, MaskReason>(forAgent);
        foreach (var (node, reason) in forRequester)
        {
            masks[node] = reason;
        }

        return masks;
    }
}
