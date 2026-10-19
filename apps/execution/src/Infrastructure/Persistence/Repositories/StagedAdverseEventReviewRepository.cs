using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Toxicity;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

public sealed class StagedAdverseEventReviewRepository(ExecutionDbContext context)
    : ExecutionRepositoryBase(context), IStagedAdverseEventReviewRepository
{
    public async Task<StagedAdverseEventReview?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await Context.StagedAdverseEventReviews
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StagedAdverseEventReview>> ListQueueAsync(
        string? studyId = null,
        string? siteId = null,
        string? reviewStatus = "STAGED_FOR_INVESTIGATOR_REVIEW",
        CancellationToken cancellationToken = default)
    {
        var query = Context.StagedAdverseEventReviews
            .AsNoTracking()
            .Where(r => !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(r => r.StudyId == new Domain.Subjects.StudyCode(studyId));
        }

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            query = query.Where(r => r.SiteId != null && r.SiteId == new Domain.Subjects.SiteCode(siteId));
        }

        if (!string.IsNullOrWhiteSpace(reviewStatus) && !reviewStatus.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.ReviewStatus == reviewStatus);
        }

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(StagedAdverseEventReview review, CancellationToken cancellationToken = default)
    {
        await Context.StagedAdverseEventReviews.AddAsync(review, cancellationToken).ConfigureAwait(false);
    }
}
