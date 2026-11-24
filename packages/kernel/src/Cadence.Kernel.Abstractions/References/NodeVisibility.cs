using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.References;

/// <summary>
/// Why a kernel read returned an object as <c>Masked(kind, reason)</c> instead of its reference (ADR-2471, Canvas
/// PX-20). Every kernel read that can reach blinded or identity-layer content uses this shape: the object stays in
/// the result, so a path, count or trace never looks shorter than it is, but nothing identifies it.
/// </summary>
public enum MaskReason
{
    /// <summary>The reader may not see the object at all (no grant or delegation covers it).</summary>
    NotPermitted,

    /// <summary>The object would reveal treatment assignment to a blinded reader (K-AUTH-4).</summary>
    Blinded,

    /// <summary>The object is in the identity layer (IdentityVault, K-IDN-1) and the reader holds no identity grant.</summary>
    IdentityVault,
}

/// <summary>
/// Decides which objects a reader may see, for kernel reads that return other objects (lineage, navigation). The
/// host implements it from the authority model (#4781) and the IdentityVault (#4782); the kernel registers none, so
/// a host that reads lineage without deciding visibility fails at startup instead of showing everything.
/// </summary>
public interface INodeVisibility
{
    /// <summary>
    /// The objects in <paramref name="nodes"/> that <paramref name="reader"/> may not see, with the reason. Objects
    /// not in the result are visible. One call per read, so an implementation can decide in bulk.
    /// </summary>
    ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken);

    /// <summary>
    /// True when <paramref name="reader"/> holds an active grant whose scope directly covers <paramref name="scope"/>'s
    /// downward scope path (for example, study-, organization-, or tenant-wide authority on a study node, rather than
    /// upward visibility from a single study site).
    /// </summary>
    async ValueTask<bool> CoversScopeAsync(
        TenantId tenant, Actor reader, NodeRef scope, CancellationToken cancellationToken) =>
        (await MaskAsync(tenant, reader, [scope], cancellationToken).ConfigureAwait(false)).Count == 0;
}
