using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>EF Core <see cref="IRandomizationRepository"/> over the shared Execution context (#5607).</summary>
public sealed class RandomizationRepository(ExecutionDbContext context)
    : ExecutionRepositoryBase(context), IRandomizationRepository
{
    public async Task LockStudyAsync(StudyCode studyCode, CancellationToken cancellationToken)
    {
        if (!Context.Database.IsNpgsql())
        {
            return;
        }

        if (Context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                $"{nameof(LockStudyAsync)} must run inside {nameof(ExecuteInTransactionAsync)}, which holds the randomisation lock until commit.");
        }

        await Context.Database.ExecuteSqlInterpolatedAsync(AdvisoryLockSql.ForRandomization(studyCode), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RandomizationScheme>> ListSchemesAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
        await Context.RandomizationSchemes.AsNoTracking()
            .Where(s => s.StudyCode == studyCode)
            .OrderByDescending(s => s.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<RandomizationScheme?> GetSchemeAsync(Guid schemeId, CancellationToken cancellationToken) =>
        Context.RandomizationSchemes.SingleOrDefaultAsync(s => s.Id == schemeId, cancellationToken);

    public Task<RandomizationScheme?> GetApprovedSchemeAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
        Context.RandomizationSchemes.SingleOrDefaultAsync(
            s => s.StudyCode == studyCode && s.Status == RandomizationSchemeStatus.Approved, cancellationToken);

    public async Task<int> GetLatestVersionAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
        await Context.RandomizationSchemes.AsNoTracking()
            .Where(s => s.StudyCode == studyCode)
            .Select(s => (int?)s.Version)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

    public async Task AddSchemeAsync(RandomizationScheme scheme, CancellationToken cancellationToken) =>
        await Context.RandomizationSchemes.AddAsync(scheme, cancellationToken).ConfigureAwait(false);

    public async Task AddListSlotsAsync(IReadOnlyCollection<RandomizationListSlot> slots, CancellationToken cancellationToken) =>
        await Context.RandomizationListSlots.AddRangeAsync(slots, cancellationToken).ConfigureAwait(false);

    public Task<RandomizationListSlot?> GetNextFreeSlotAsync(Guid schemeId, string stratumKey, CancellationToken cancellationToken) =>
        Context.RandomizationListSlots
            .Where(s => s.SchemeId == schemeId && s.StratumKey == stratumKey && s.AllocationId == null)
            .OrderBy(s => s.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<RandomizationAllocation?> GetAllocationForSubjectAsync(SubjectId subjectId, CancellationToken cancellationToken) =>
        Context.RandomizationAllocations.AsNoTracking().SingleOrDefaultAsync(a => a.SubjectId == subjectId, cancellationToken);

    public async Task<IReadOnlyList<RandomizationAllocation>> ListAllocationsAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
        await Context.RandomizationAllocations.AsNoTracking()
            .Where(a => a.StudyCode == studyCode)
            .OrderBy(a => a.AllocatedAt)
            .ThenBy(a => a.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAllocationAsync(RandomizationAllocation allocation, CancellationToken cancellationToken) =>
        await Context.RandomizationAllocations.AddAsync(allocation, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<IpKitInventoryItem>> ListAssignableKitsAsync(StudyCode studyCode, SiteCode siteCode, DateTimeOffset at,
        CancellationToken cancellationToken) =>
        await Context.IpKitInventoryItems
            .Where(k => k.StudyId == studyCode.Value
                && k.SiteId == siteCode.Value
                && k.Status == IpKitInventoryItem.ActiveAtSiteStatus
                && k.AssignedRandomizationId == null
                && k.TreatmentArmCiphertext != null
                && k.ExpirationDate > at)
            .OrderBy(k => k.ExpirationDate)
            .ThenBy(k => k.KitNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
