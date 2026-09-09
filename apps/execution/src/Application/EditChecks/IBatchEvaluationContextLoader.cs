using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.EditChecks;

/// <summary>
/// Service abstraction for bulk loading subject observations and form submissions to evaluate edit checks without N+1 queries.
/// @req:PRD-SYS-004
/// @req:SRS-EDC-005
/// </summary>
public interface IBatchEvaluationContextLoader
{
    Task<BatchEvaluationContext> LoadAsync(StudyCode studyId, SubjectCode subjectId, CancellationToken cancellationToken);
}
