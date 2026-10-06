using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Domain.Toxicity;

/// <summary>
/// Repository interface for persisting and retrieving <see cref="StagedAdverseEventReview"/> records.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public interface IStagedAdverseEventReviewRepository
{
    Task<StagedAdverseEventReview?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StagedAdverseEventReview>> ListQueueAsync(
        string? studyId = null,
        string? siteId = null,
        string? reviewStatus = "STAGED_FOR_INVESTIGATOR_REVIEW",
        CancellationToken cancellationToken = default);

    Task AddAsync(StagedAdverseEventReview review, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
