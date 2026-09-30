using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Protocol;

/// <summary>
/// Reads a protocol version's design (spec §6.2) for services that evaluate it outside a command, such as design-time
/// conformance (ADR-2464). Reads run under the tenant's Row-Level Security. The reader does not authorize the actor.
/// </summary>
public interface IProtocolDesignReader
{
    /// <summary>The version's design as stored now, or null when the tenant has no such version.</summary>
    Task<ProtocolDesignSnapshot?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken);
}

/// <summary>A protocol version's design at one version index.</summary>
/// <param name="Version">The version without its content.</param>
/// <param name="Node">The version's lineage node at the content read: a draft's node changes each time it is revised.</param>
/// <param name="Design">The design.</param>
public sealed record ProtocolDesignSnapshot(ProtocolVersionSummary Version, NodeRef Node, ProtocolDesign Design);
