using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Modules;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Intelligence.Infrastructure.Persistence;

/// <summary>Reads the ledger and controls through a tenant-scoped context, so Row-Level Security limits every query.</summary>
internal sealed class EfAgentLedger(IModuleTenantContextFactory<IntelligenceDbContext> contexts) : IAgentLedger
{
    public async Task<IReadOnlyList<AgentControl>> ControlsAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.AgentControls.AsNoTracking().OrderBy(c => c.Scope).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> TokensUsedAsync(TenantId tenant, string? taskKey, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var runs = db.AgentRuns.AsNoTracking().Where(r => r.StartedAt >= since);
        if (taskKey is not null)
        {
            runs = runs.Where(r => r.TaskKey == taskKey);
        }

        return await runs.SumAsync(r => r.InputTokens + r.OutputTokens, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentRun>> RunsAsync(TenantId tenant, string requestedBy, int limit, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.AgentRuns.AsNoTracking()
            .Where(r => r.RequestedBy == requestedBy)
            .OrderByDescending(r => r.StartedAt)
            .ThenByDescending(r => r.Id)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentRun?> FindRunAsync(TenantId tenant, Guid runId, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.AgentRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false);
    }
}
