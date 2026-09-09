using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Port for persisting and retrieving <see cref="ClinicalObservation"/> aggregates.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public interface IObservationRepository
{
    Task<ClinicalObservation?> GetByIdAsync(ObservationId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClinicalObservation>> ListBySubjectAsync(StudyCode studyId, SubjectCode subjectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClinicalObservation>> ListByCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        DomainCode? domain,
        TestCode? testCode,
        CancellationToken cancellationToken);

    Task AddAsync(ClinicalObservation observation, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
