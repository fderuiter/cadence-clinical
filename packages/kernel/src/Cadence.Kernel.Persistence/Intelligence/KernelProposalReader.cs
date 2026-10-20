using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Intelligence;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Intelligence;

/// <summary>
/// Reads proposals with their plan's artifact through a context bound to the caller's tenant (ADR-2250). Read-only: no
/// tracking. Expiry is derived with <see cref="Proposal.StatusAt"/>, so the status filter matches what a command sees.
/// </summary>
internal sealed class KernelProposalReader(KernelTenantDbContextFactory contexts) : IProposalReader
{
    /// <summary>The most proposals one list returns.</summary>
    public const int MaxLimit = 200;

    public async Task<IReadOnlyList<ProposalSummary>> ListForStudyAsync(
        TenantId tenant, StudyId study, ProposalStatus? status, DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var query = db.Proposals.AsNoTracking().Where(p => p.TenantId == tenant && p.StudyId == study);

        // Expired is derived: an open proposal whose expiry has passed. Every other status is stored as it reads, except
        // that an open proposal reads as open only before its expiry.
        query = status switch
        {
            null => query,
            ProposalStatus.Expired => query.Where(p => p.Status == ProposalStatus.Open && p.ExpiresAt <= now),
            ProposalStatus.Open => query.Where(p => p.Status == ProposalStatus.Open && p.ExpiresAt > now),
            var stored => query.Where(p => p.Status == stored),
        };

        var proposals = await query
            .OrderByDescending(p => p.ProposedAt).ThenBy(p => p.Id)
            .Take(Math.Clamp(limit, 1, MaxLimit))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var plans = await PlansAsync(db, tenant, proposals, cancellationToken).ConfigureAwait(false);
        return [.. proposals.Select(p => Summary(p, plans[p.PlanArtifactVersionId], now))];
    }

    public async Task<ProposalSummary?> GetAsync(TenantId tenant, ProposalId proposal, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var found = await db.Proposals.AsNoTracking()
            .SingleOrDefaultAsync(p => p.TenantId == tenant && p.Id == proposal, cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        var plans = await PlansAsync(db, tenant, [found], cancellationToken).ConfigureAwait(false);
        return Summary(found, plans[found.PlanArtifactVersionId], now);
    }

    /// <summary>The plans of <paramref name="proposals"/>; the handler checked each exists when it was proposed.</summary>
    private static async Task<Dictionary<ArtifactVersionId, ProposalPlan>> PlansAsync(
        KernelDbContext db, TenantId tenant, IReadOnlyList<Proposal> proposals, CancellationToken cancellationToken)
    {
        var ids = proposals.Select(p => p.PlanArtifactVersionId).Distinct().ToList();
        var plans = await (
                from version in db.ArtifactVersions.AsNoTracking()
                join artifact in db.Artifacts.AsNoTracking() on version.ArtifactId equals artifact.Id
                where version.TenantId == tenant && ids.Contains(version.Id)
                select new ProposalPlan(version.Id, artifact.Id, artifact.Title, version.Number, version.MediaType, version.ContentHash))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return plans.ToDictionary(p => p.VersionId);
    }

    private static ProposalSummary Summary(Proposal proposal, ProposalPlan plan, DateTimeOffset now) =>
        new(
            proposal.Id,
            proposal.StudyId,
            proposal.Intent,
            plan,
            [.. proposal.Affected],
            proposal.SimulatedImpact,
            [.. proposal.RequestedCapabilities],
            proposal.ProposedAt,
            proposal.ExpiresAt,
            proposal.ProposedById,
            proposal.ProposedByKind,
            proposal.RequestedById,
            proposal.ModelId,
            proposal.ConfidenceScore,
            proposal.ContentHash,
            proposal.StatusAt(now),
            proposal.DecidedAt,
            proposal.DecidedById,
            proposal.ApprovalAttestationId,
            proposal.DecisionReason,
            proposal.IsEscalated,
            proposal.EscalatedTo,
            proposal.EscalationReason);
}
