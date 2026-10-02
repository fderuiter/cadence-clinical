using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Core.Domain;
using Cadence.Security.Authentication;

namespace Cadence.Apps.Execution.Application.Cec;

/// <summary>
/// Application service interface for CEC adjudication operations.
/// @req:PRD-CEC-002
/// @req:PRD-SYS-053
/// </summary>
public interface ICecApplicationService
{
    Task<Result<ChairAdjudicationResponse>> ChairAdjudicateAsync(
        string dossierId,
        ChairAdjudicateRequest request,
        AuthenticatedCaller? signer,
        CancellationToken cancellationToken);
}
