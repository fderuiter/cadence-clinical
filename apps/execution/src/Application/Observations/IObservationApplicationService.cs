using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

    Task<Result<ObservationResponse>> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ObservationResponse>>> ListBySubjectAsync(string studyId, string subjectId, CancellationToken cancellationToken);

    Task<Result<ObservationResponse>> VerifySdvAsync(string id, string verifierUserId, CancellationToken cancellationToken);

    Task<Result<ObservationResponse>> FlagSdvAsync(string id, string reason, CancellationToken cancellationToken);
}
