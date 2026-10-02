using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.EditChecks;

/// <summary>
/// EF Core 10 loader executing consolidated queries for subject observations, form submissions,
/// consent status, and subject lifecycle state.
/// Eliminates N+1 query cascades during longitudinal edit check evaluation and enforces subject consent gating.
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
        var submissions = await context.FormSubmissions
            .AsNoTracking()
            .Where(f => f.StudyId == studyId && f.SubjectId == subjectId && f.IsActive)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var observations = await context.ClinicalObservations
            .AsNoTracking()
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId && o.IsActive)
            .OrderByDescending(o => o.ObservationDate)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var consents = await context.SubjectConsents
            .AsNoTracking()
            .Where(c => c.ProtocolVersion.StudyCode == studyId && c.SubjectCode == subjectId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var subject = await context.ClinicalSubjects
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.StudyCode == studyId && s.SubjectCode == subjectId, cancellationToken).ConfigureAwait(false);

        return new BatchEvaluationContext(
            studyId.Value,
            subjectId.Value,
            observations,
            submissions,
            subject,
            consents);
    }
}
