using System;
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
    /// The mask reason preventing the current caller from seeing <paramref name="node"/> in <paramref name="tenant"/>,
    /// or null when <paramref name="node"/> is visible.
    /// </summary>
    async ValueTask<MaskReason?> MaskForAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken)
    {
        var masked = await MaskAsync(tenant, [node], cancellationToken).ConfigureAwait(false);
        return masked.TryGetValue(node, out var reason) ? reason : null;
    }

    /// <summary>
    /// Evaluates visibility across <paramref name="nodes"/> in <paramref name="tenant"/> as a single batch, returning
    /// the <see cref="MaskReason"/> for each masked node (visible nodes are omitted).
    /// </summary>
    async ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var masked = new Dictionary<NodeRef, MaskReason>();
        foreach (var node in nodes)
        {
            if (!masked.ContainsKey(node) && !await CanSeeAsync(tenant, node, cancellationToken).ConfigureAwait(false))
            {
                masked[node] = MaskReason.NotPermitted;
            }
        }

        return masked;
    }

    /// <summary>
    /// Returns the subset of <paramref name="nodes"/> that the current caller may see in <paramref name="tenant"/>,
    /// evaluated in one visibility pass when supported by the host.
    /// </summary>
    async ValueTask<IReadOnlySet<NodeRef>> FilterVisibleAsync(
        TenantId tenant,
        IReadOnlyCollection<NodeRef> nodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var visible = new HashSet<NodeRef>();
        foreach (var node in nodes)
        {
            if (!visible.Contains(node) && await CanSeeAsync(tenant, node, cancellationToken).ConfigureAwait(false))
            {
                visible.Add(node);
            }
        }

        return visible;
    }

    /// <summary>
    /// True when the current caller holds an active grant whose scope directly covers <paramref name="scope"/>'s
    /// downward scope path (for example, study-wide, organization-wide, or tenant-wide visibility on a study node,
    /// excluding site-only ancestor visibility).
    /// </summary>
    ValueTask<bool> CoversScopeAsync(TenantId tenant, NodeRef scope, CancellationToken cancellationToken) =>
        CanSeeAsync(tenant, scope, cancellationToken);

    /// <summary>
    /// True when the current caller holds a grant permitting <paramref name="capability"/> on <paramref name="resource"/>
    /// in <paramref name="tenant"/> (ADR-2482). Unblinding capabilities fail closed when no capability probe is configured.
    /// </summary>
    ValueTask<bool> CanExerciseAsync(
        TenantId tenant,
        Capability capability,
        NodeRef resource,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(!Capability.IsUnblinding(capability.Token));

    /// <summary>
    /// True when the current caller holds <c>enrollment.unblind</c> covering <paramref name="enrollment"/> in
    /// <paramref name="tenant"/> (K-AUTH-4). Defaults to false when no capability probe is configured so blinded
    /// treatment values fail closed unless explicitly unblinded.
    /// </summary>
    ValueTask<bool> CanUnblindAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken) =>
        CanExerciseAsync(tenant, new Capability("enrollment.unblind", VerbClass.Read), NodeRef.For(enrollment), cancellationToken);

    /// <summary>
    /// Returns the subset of <paramref name="enrollments"/> for which the current caller holds <c>enrollment.unblind</c>
    /// in <paramref name="tenant"/> (K-AUTH-4), evaluated in one batch when supported by the host.
    /// </summary>
    async ValueTask<IReadOnlySet<EnrollmentId>> CanUnblindManyAsync(
        TenantId tenant,
        IReadOnlyCollection<EnrollmentId> enrollments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enrollments);
        var permitted = new HashSet<EnrollmentId>();
        var checkedEnrollments = new HashSet<EnrollmentId>();
        foreach (var enrollment in enrollments)
        {
            if (checkedEnrollments.Add(enrollment)
                && await CanUnblindAsync(tenant, enrollment, cancellationToken).ConfigureAwait(false))
            {
                permitted.Add(enrollment);
            }
        }

        return permitted;
    }
}
