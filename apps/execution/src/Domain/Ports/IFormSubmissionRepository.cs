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

    /// <summary>
    /// Loads the aggregate with change tracking for a command that mutates it. <see cref="GetByIdAsync"/> is the
    /// untracked read path; changes made to an aggregate it returns are not persisted by <c>SaveChangesAsync</c>.
    /// </summary>
    Task<FormSubmission?> GetForUpdateAsync(FormSubmissionId id, CancellationToken cancellationToken);

    Task<FormSubmission?> GetByCoordinatesAsync(StudyCode studyId, SubjectCode subjectId, string? visitId, FormId formId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormSubmission>> ListBySubjectAsync(StudyCode studyId, SubjectCode subjectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormSubmission>> ListByVisitAsync(StudyCode studyId, SubjectCode subjectId, string visitId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormSubmission>> ListBySubjectsForUpdateAsync(StudyCode studyId, IEnumerable<SubjectCode> subjectIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormSubmission>> ListByVisitsForUpdateAsync(StudyCode studyId, IEnumerable<string> visitIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormSubmission>> ListByIdsForUpdateAsync(IEnumerable<FormSubmissionId> ids, CancellationToken cancellationToken);

    Task AddAsync(FormSubmission submission, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
