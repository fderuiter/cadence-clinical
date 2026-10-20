using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Persistence port for randomisation schemes, their lists and allocations, and the site kits randomisation assigns
/// (#5607). Writes stage on the shared Execution context and commit on <see cref="SaveChangesAsync"/>.
/// @req:PRD-SUB-001
/// </summary>
public interface IRandomizationRepository
{
    /// <summary>
    /// Takes the study's randomisation lock for the rest of the current transaction, so two randomisations or a
    /// randomisation and an approval in the same study run one after the other. Must run inside
    /// <see cref="ExecuteInTransactionAsync"/>.
    /// </summary>
    Task LockStudyAsync(StudyCode studyCode, CancellationToken cancellationToken);

    /// <summary>Every version of the study's scheme, newest first.</summary>
    Task<IReadOnlyList<RandomizationScheme>> ListSchemesAsync(StudyCode studyCode, CancellationToken cancellationToken);

    /// <summary>The scheme version, tracked.</summary>
    Task<RandomizationScheme?> GetSchemeAsync(Guid schemeId, CancellationToken cancellationToken);

    /// <summary>The study's approved scheme, tracked, or null when none is live.</summary>
    Task<RandomizationScheme?> GetApprovedSchemeAsync(StudyCode studyCode, CancellationToken cancellationToken);

    /// <summary>The highest version number drafted for the study, or 0.</summary>
    Task<int> GetLatestVersionAsync(StudyCode studyCode, CancellationToken cancellationToken);

    Task AddSchemeAsync(RandomizationScheme scheme, CancellationToken cancellationToken);

    Task AddListSlotsAsync(IReadOnlyCollection<RandomizationListSlot> slots, CancellationToken cancellationToken);

    /// <summary>The lowest-sequence unconsumed slot of the scheme's stratum, tracked, or null when the list is exhausted.</summary>
    Task<RandomizationListSlot?> GetNextFreeSlotAsync(Guid schemeId, string stratumKey, CancellationToken cancellationToken);

    /// <summary>The participant's allocation, or null.</summary>
    Task<RandomizationAllocation?> GetAllocationForSubjectAsync(SubjectId subjectId, CancellationToken cancellationToken);

    /// <summary>Every allocation in the study, in allocation order (minimisation balances over them).</summary>
    Task<IReadOnlyList<RandomizationAllocation>> ListAllocationsAsync(StudyCode studyCode, CancellationToken cancellationToken);

    Task AddAllocationAsync(RandomizationAllocation allocation, CancellationToken cancellationToken);

    /// <summary>The site's unassigned, labelled kits for the study, tracked, earliest expiry first.</summary>
    Task<IReadOnlyList<IpKitInventoryItem>> ListAssignableKitsAsync(StudyCode studyCode, SiteCode siteCode, DateTimeOffset at,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> unitOfWork, CancellationToken cancellationToken);
}
