using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Kernel.Persistence.Lineage;

/// <summary>
/// The scheduled K-LIN-1 check: every derived object version must have at least one inbound lineage edge. The command
/// pipeline already refuses to commit a derived object without inputs, so an orphan here means rows written outside
/// the pipeline or a defect; the check visits each tenant in turn and hands what it finds to
/// <see cref="ILineageOrphanSink"/>.
/// </summary>
internal sealed class KernelLineageOrphanCheck(KernelTenantDbContextFactory contexts, ILineageOrphanSink sink)
{
    /// <summary>Orphans reported per tenant per pass; the rest are found on later passes once these are fixed.</summary>
    internal const int ReportLimit = 1_000;

    /// <summary>Checks every tenant and reports each tenant's result, including an empty one.</summary>
    public async Task CheckAllAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in await contexts.ListTenantsAsync(cancellationToken).ConfigureAwait(false))
        {
            var orphans = await FindOrphansAsync(tenant, ReportLimit, cancellationToken).ConfigureAwait(false);
            await sink.ReportAsync(tenant, orphans, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Derived object versions in <paramref name="tenant"/> with no inbound edge, oldest first.</summary>
    public async Task<IReadOnlyList<LineageOrphan>> FindOrphansAsync(TenantId tenant, int limit, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var rows = await db.LineageNodes.AsNoTracking()
            .Where(n => n.Derived && !db.LineageEdges.Any(e => e.ToId == n.NodeId && e.ToKind == n.Kind && e.ToVersion == n.Version))
            .OrderBy(n => n.RecordedAt).ThenBy(n => n.NodeId)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(n => new LineageOrphan(tenant, new NodeRef(n.Kind, n.NodeId, n.Version), n.ActivityId, n.RecordedAt))];
    }
}

/// <summary>
/// The kernel's default <see cref="ILineageOrphanSink"/>: logs orphans as warnings until the Quality engine raises
/// them as ValidationFindings (#4812). Logs the references only, never object content.
/// </summary>
internal sealed partial class LoggingLineageOrphanSink(ILogger<LoggingLineageOrphanSink> logger) : ILineageOrphanSink
{
    public Task ReportAsync(TenantId tenant, IReadOnlyList<LineageOrphan> orphans, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orphans);
        if (orphans.Count > 0)
        {
            LogOrphans(logger, tenant.Value, orphans.Count, string.Join(", ", orphans.Take(10).Select(o => o.Node.ToString())));
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "K-LIN-1: tenant {Tenant} has {Count} derived object versions without inputs, first: {Nodes}")]
    private static partial void LogOrphans(ILogger logger, Guid tenant, int count, string nodes);
}
