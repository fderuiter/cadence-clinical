using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Lineage;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Lineage;

/// <summary>
/// Finds the protocol version a protocol element belongs to (ADR-2474). An element's node id is a one-way hash of its
/// study and element id, so the owner is read from the lineage: the approved version that most recently wrote the
/// <see cref="LineageEdgeKind.GovernedBy"/> edge to it. Every version that governs an element is of the same study.
/// </summary>
internal static class ProtocolElementOwner
{
    public static async Task<ProtocolVersionId?> GoverningVersionAsync(KernelDbContext db, TenantId tenant, Guid element, CancellationToken cancellationToken)
    {
        var version = await db.LineageEdges.AsNoTracking()
            .Where(e => e.TenantId == tenant
                && e.ToKind == NodeKinds.ProtocolElement
                && e.ToId == element
                && e.FromKind == NodeKinds.ProtocolVersion
                && e.Kind == LineageEdgeKind.GovernedBy)
            .OrderByDescending(e => e.RecordedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => (Guid?)e.FromId)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return version is { } id ? ProtocolVersionId.FromGuid(id) : null;
    }
}
