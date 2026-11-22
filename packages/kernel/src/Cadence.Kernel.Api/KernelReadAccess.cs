using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api;

/// <summary>
/// <see cref="IKernelReadAccess"/> over the same masking as the kernel's reads (<see cref="KernelReadMasking"/>), for a
/// host that registers module read endpoints.
/// </summary>
/// <param name="visibility">The host's visibility.</param>
/// <param name="actor">The request's verified actor.</param>
/// <param name="probe">Optional dry-run authorization probe for capability checks.</param>
public sealed class KernelReadAccess(
    INodeVisibility visibility,
    ICurrentActor actor,
    IAuthorizationProbe? probe = null) : IKernelReadAccess
{
    /// <inheritdoc />
    public async ValueTask<bool> CanSeeAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken)
    {
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [node], cancellationToken).ConfigureAwait(false);
        return masks.For(node) is null;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken) =>
        KernelReadMasking.MaskAsync(visibility, actor, tenant, nodes, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<bool> CanExerciseAsync(
        TenantId tenant,
        Capability capability,
        NodeRef resource,
        CancellationToken cancellationToken)
    {
        if (probe is null)
        {
            return true;
        }

        var decision = await probe.CanIAsync(tenant, capability, resource, cancellationToken).ConfigureAwait(false);
        return decision.Permitted;
    }
}
