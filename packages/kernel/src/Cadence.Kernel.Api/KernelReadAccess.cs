using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly Capability EnrollmentUnblind = new("enrollment.unblind", VerbClass.Read);

    /// <inheritdoc />
    public async ValueTask<bool> CanSeeAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
        await MaskForAsync(tenant, node, cancellationToken).ConfigureAwait(false) is null;

    /// <inheritdoc />
    public async ValueTask<MaskReason?> MaskForAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken)
    {
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [node], cancellationToken).ConfigureAwait(false);
        return masks.For(node);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken) =>
        KernelReadMasking.MaskAsync(visibility, actor, tenant, nodes, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<NodeRef>> FilterVisibleAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count == 0)
        {
            return new HashSet<NodeRef>();
        }

        var distinct = nodes.Distinct().ToList();
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, distinct, cancellationToken).ConfigureAwait(false);
        return distinct.Where(node => masks.For(node) is null).ToHashSet();
    }

    /// <inheritdoc />
    public async ValueTask<bool> CanExerciseAsync(
        TenantId tenant,
        Capability capability,
        NodeRef resource,
        CancellationToken cancellationToken)
    {
        if (probe is null)
        {
            return !Capability.IsUnblinding(capability.Token);
        }

        var decision = await probe.CanIAsync(tenant, capability, resource, cancellationToken).ConfigureAwait(false);
        return decision.Permitted;
    }

    /// <inheritdoc />
    public ValueTask<bool> CanUnblindAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken) =>
        CanExerciseAsync(tenant, EnrollmentUnblind, NodeRef.For(enrollment), cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<EnrollmentId>> CanUnblindManyAsync(
        TenantId tenant,
        IReadOnlyCollection<EnrollmentId> enrollments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enrollments);
        if (enrollments.Count == 0 || probe is null)
        {
            return new HashSet<EnrollmentId>();
        }

        var distinct = enrollments.Distinct().ToList();
        var resources = distinct.Select(static e => NodeRef.For(e)).ToList();
        var decisions = await probe.CanIManyAsync(tenant, EnrollmentUnblind, resources, cancellationToken).ConfigureAwait(false);
        var permitted = new HashSet<EnrollmentId>();
        foreach (var enrollment in distinct)
        {
            if (decisions.TryGetValue(NodeRef.For(enrollment), out var decision) && decision.Permitted)
            {
                permitted.Add(enrollment);
            }
        }

        return permitted;
    }
}
