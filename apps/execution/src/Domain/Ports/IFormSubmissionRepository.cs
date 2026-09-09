using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Port for persisting and retrieving <see cref="FormSubmission"/> aggregates.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-002
/// </summary>
public interface IFormSubmissionRepository
{
    Task<FormSubmission?> GetByIdAsync(FormSubmissionId id, CancellationToken cancellationToken);

    Task<FormSubmission?> GetByCoordinatesAsync(StudyCode studyId, SubjectCode subjectId, string? visitId, FormId formId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormSubmission>> ListBySubjectAsync(StudyCode studyId, SubjectCode subjectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormSubmission>> ListByVisitAsync(StudyCode studyId, SubjectCode subjectId, string visitId, CancellationToken cancellationToken);

    Task AddAsync(FormSubmission submission, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
