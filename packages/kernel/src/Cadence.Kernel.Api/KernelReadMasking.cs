using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api;

/// <summary>
/// Asks the host's <see cref="INodeVisibility"/> which objects of one read the caller may not see (ADR-2471). A
/// request without an accountable actor sees nothing: every object is masked as not permitted.
/// </summary>
internal static class KernelReadMasking
{
    private static readonly IReadOnlyDictionary<NodeRef, MaskReason> None = new Dictionary<NodeRef, MaskReason>();

    public static async ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        INodeVisibility visibility,
        ICurrentActor currentActor,
        TenantId tenant,
        IEnumerable<NodeRef> nodes,
        CancellationToken cancellationToken)
    {
        var distinct = nodes.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return None;
        }

        if (currentActor.Actor is not { } reader)
        {
            return distinct.ToDictionary(n => n, _ => MaskReason.NotPermitted);
        }

        return await visibility.MaskAsync(tenant, reader, distinct, cancellationToken).ConfigureAwait(false);
    }

    public static ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskExplanationsAsync(
        INodeVisibility visibility,
        ICurrentActor currentActor,
        TenantId tenant,
        IEnumerable<Explanation> explanations,
        CancellationToken cancellationToken) =>
        MaskAsync(visibility, currentActor, tenant, ExplanationMasking.CollectNodes(explanations), cancellationToken);

    public static MaskReason? For(this IReadOnlyDictionary<NodeRef, MaskReason> masks, NodeRef? node) =>
        ExplanationMasking.ReasonFor(node, masks);
}
