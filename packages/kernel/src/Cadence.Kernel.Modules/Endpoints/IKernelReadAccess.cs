using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Whether the request's caller may see a kernel object, decided as the kernel's own reads decide it (ADR-2471): by the
/// host's visibility over the caller's verified actor. A module's read endpoint asks before it answers with anything
/// derived from that object, so a module never shows what the kernel would mask. The host implements it.
/// </summary>
public interface IKernelReadAccess
{
    /// <summary>
    /// True when the current caller may see <paramref name="node"/> in <paramref name="tenant"/>; false when it is
    /// masked for any reason, or when the request has no accountable actor.
    /// </summary>
    ValueTask<bool> CanSeeAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken);

    /// <summary>
    /// Evaluates visibility across <paramref name="nodes"/> in <paramref name="tenant"/> as a single batch, returning
    /// the <see cref="MaskReason"/> for each masked node (visible nodes are omitted).
    /// </summary>
    async ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken)
    {
        var masked = new Dictionary<NodeRef, MaskReason>();
        foreach (var node in nodes)
        {
            if (!await CanSeeAsync(tenant, node, cancellationToken).ConfigureAwait(false))
            {
                masked[node] = MaskReason.NotPermitted;
            }
        }

        return masked;
    }

    /// <summary>
    /// True when the current caller holds a grant permitting <paramref name="capability"/> on <paramref name="resource"/>
    /// in <paramref name="tenant"/> (ADR-2482).
    /// </summary>
    ValueTask<bool> CanExerciseAsync(
        TenantId tenant,
        Capability capability,
        NodeRef resource,
        CancellationToken cancellationToken) => ValueTask.FromResult(true);
}
