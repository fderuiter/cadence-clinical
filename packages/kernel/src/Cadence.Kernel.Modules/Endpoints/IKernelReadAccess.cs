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
    /// True when the current caller holds a grant permitting <paramref name="capability"/> on <paramref name="resource"/>
    /// in <paramref name="tenant"/> (ADR-2482).
    /// </summary>
    ValueTask<bool> CanExerciseAsync(
        TenantId tenant,
        Capability capability,
        NodeRef resource,
        CancellationToken cancellationToken) => ValueTask.FromResult(true);
}
