using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Observations;

/// <summary>
/// Service abstraction governing clinical observation ingestion, unit conversions, and SDV verification.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-042
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public interface IObservationApplicationService
{
    Task<Result<ObservationResponse>> CreateAsync(ObservationCreateRequest request, CancellationToken cancellationToken);

    Task<Result<ObservationResponse>> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<Result<ObservationResponse>> GetByIdAsync(string id, bool includeInactive, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ObservationResponse>>> ListBySubjectAsync(string studyId, string subjectId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ObservationResponse>>> ListBySubjectAsync(string studyId, string subjectId, bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an observation source-verified by <paramref name="verifier"/>, who must hold a source data
    /// verification role (<see cref="ObservationApplicationService.CanVerifySourceData"/>).
    /// </summary>
    Task<Result<ObservationResponse>> VerifySdvAsync(string id, AuthenticatedCaller? verifier, CancellationToken cancellationToken);

    /// <summary>Flags an SDV discrepancy; <paramref name="verifier"/> must hold a source data verification role.</summary>
    Task<Result<ObservationResponse>> FlagSdvAsync(string id, string reason, AuthenticatedCaller? verifier, CancellationToken cancellationToken);
}
