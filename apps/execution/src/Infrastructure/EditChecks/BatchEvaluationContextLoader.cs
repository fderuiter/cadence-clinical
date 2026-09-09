using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.EditChecks;

/// <summary>
/// EF Core 10 loader executing consolidated queries for subject observations and form submissions.
/// Eliminates N+1 query cascades during longitudinal edit check evaluation.
/// @req:PRD-SYS-004
/// @req:SRS-EDC-005
/// </summary>
public sealed class BatchEvaluationContextLoader(ExecutionDbContext context) : IBatchEvaluationContextLoader
{
    public async Task<BatchEvaluationContext> LoadAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        CancellationToken cancellationToken)
    {
        var submissionsTask = context.FormSubmissions
            .AsNoTracking()
            .Where(f => f.StudyId == studyId && f.SubjectId == subjectId && f.IsActive)
            .ToListAsync(cancellationToken);

        var observationsTask = context.ClinicalObservations
            .AsNoTracking()
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId)
            .OrderByDescending(o => o.ObservationDate)
            .ToListAsync(cancellationToken);

        await Task.WhenAll(submissionsTask, observationsTask).ConfigureAwait(false);

        return new BatchEvaluationContext(submissionsTask.Result, observationsTask.Result);
    }
}
