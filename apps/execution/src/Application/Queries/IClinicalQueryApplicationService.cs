using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Service abstraction governing clinical discrepancy query lifecycle and workflows.
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// @req:SRS-EDC-004
/// </summary>
public interface IClinicalQueryApplicationService
{
    Task<Result<ClinicalQueryResponse>> CreateAsync(QueryCreateRequest request, CancellationToken cancellationToken);

    Task<Result<ClinicalQueryResponse>> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ClinicalQueryResponse>>> ListAsync(
        string? studyId,
        string? subjectId,
        string? visitId,
        string? status,
        CancellationToken cancellationToken);

    Task<Result<ClinicalQueryResponse>> AnswerAsync(
        string id,
        QueryRespondRequest request,
        CancellationToken cancellationToken);

    /// <summary>Closes a query as the authenticated caller, who must be a CRA or data manager.</summary>
    Task<Result<ClinicalQueryResponse>> CloseAsync(
        string id,
        CancellationToken cancellationToken,
        string? userRole = null);

    Task<Result<ClinicalQueryResponse>> ReopenAsync(
        string id,
        QueryReopenRequest request,
        CancellationToken cancellationToken,
        string? userRole = null);

    Task<Result<ClinicalQueryResponse>> CancelAsync(
        string id,
        QueryCancelRequest request,
        CancellationToken cancellationToken,
        string? userRole = null);
}
