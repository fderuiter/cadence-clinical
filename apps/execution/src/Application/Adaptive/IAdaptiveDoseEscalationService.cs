using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Adaptive;

public interface IAdaptiveDoseEscalationService
{
    Task<AdaptiveDoseEscalationStateResponse> GetStateAsync(string studyId, CancellationToken ct = default);

    Task<Result<AdaptiveCohortTransitionResponse>> SubmitCohortTransitionAsync(
        AdaptiveCohortTransitionRequest request,
        AuthenticatedCaller caller,
        string? sigToken,
        string? changeReasonHeader,
        CancellationToken ct = default);
}
