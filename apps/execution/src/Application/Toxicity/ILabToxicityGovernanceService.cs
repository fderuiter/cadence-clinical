using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Toxicity;

public interface ILabToxicityGovernanceService
{
    Task<Result<IReadOnlyList<StagedAdverseEventReviewResponse>>> ListQueueAsync(
        string? studyId = null,
        string? siteId = null,
        string? reviewStatus = "STAGED_FOR_INVESTIGATOR_REVIEW",
        CancellationToken cancellationToken = default);

    Task<Result<StagedAdverseEventActionResponse>> ExecuteActionAsync(
        string reviewId,
        StagedAdverseEventActionRequest request,
        AuthenticatedCaller? caller,
        CancellationToken cancellationToken = default);

    Task<Result<LabToxicityEvaluateResponse>> EvaluateAsync(
        LabToxicityEvaluateRequest request,
        CancellationToken cancellationToken = default);

    Task<StagedAdverseEventReviewResponse?> GetReviewByIdAsync(
        string reviewId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StagedAdverseEventReviewResponse?>(null);
}
