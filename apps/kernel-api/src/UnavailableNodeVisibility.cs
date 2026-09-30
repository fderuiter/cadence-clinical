using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Host;

/// <summary>
/// Fails closed until visibility is decided from the authority model (#4781) and the IdentityVault (#4782): every
/// object is masked as not permitted, so kernel reads keep their shape but identify nothing (ADR-2471, ADR-2540).
/// </summary>
internal sealed class UnavailableNodeVisibility : INodeVisibility
{
    public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(nodes.Distinct().ToDictionary(n => n, _ => MaskReason.NotPermitted));
}
