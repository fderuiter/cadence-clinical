using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;

using Polly;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core 10 repository adapter for <see cref="FormSubmission"/> aggregates.
/// All query-only reads strictly enforce <c>.AsNoTracking()</c>.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:SRS-EDC-002
/// </summary>
public sealed class FormSubmissionRepository(ExecutionDbContext context, ResiliencePipeline saveChangesResilience)
    : ExecutionRepositoryBase(context, saveChangesResilience), IFormSubmissionRepository
{
    public async Task<FormSubmission?> GetByIdAsync(FormSubmissionId id, CancellationToken cancellationToken) =>
        await Context.FormSubmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.IsActive, cancellationToken)
            .ConfigureAwait(false);

    public async Task<FormSubmission?> GetForUpdateAsync(FormSubmissionId id, CancellationToken cancellationToken) =>
        await Context.FormSubmissions
            .FirstOrDefaultAsync(f => f.Id == id && f.IsActive, cancellationToken)
            .ConfigureAwait(false);

    public async Task<FormSubmission?> GetByCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        FormId formId,
        CancellationToken cancellationToken) =>
        await Context.FormSubmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.StudyId == studyId &&
                                      f.SubjectId == subjectId &&
                                      f.VisitId == visitId &&
                                      f.FormId == formId &&
                                      f.IsActive, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<FormSubmission>> ListBySubjectAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        CancellationToken cancellationToken) =>
        await Context.FormSubmissions
            .AsNoTracking()
            .Where(f => f.StudyId == studyId && f.SubjectId == subjectId && f.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<FormSubmission>> ListByVisitAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string visitId,
        CancellationToken cancellationToken) =>
        await Context.FormSubmissions
            .AsNoTracking()
            .Where(f => f.StudyId == studyId && f.SubjectId == subjectId && f.VisitId == visitId && f.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<FormSubmission>> ListBySubjectsForUpdateAsync(
        StudyCode studyId,
        IEnumerable<SubjectCode> subjectIds,
        CancellationToken cancellationToken)
    {
        var subjectList = subjectIds.ToList();
        return await Context.FormSubmissions
            .Where(f => f.StudyId == studyId && subjectList.Contains(f.SubjectId) && f.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FormSubmission>> ListByVisitsForUpdateAsync(
        StudyCode studyId,
        IEnumerable<string> visitIds,
        CancellationToken cancellationToken)
    {
        var visitList = visitIds.Where(v => !string.IsNullOrEmpty(v)).ToList();
        return await Context.FormSubmissions
            .Where(f => f.StudyId == studyId && f.VisitId != null && visitList.Contains(f.VisitId) && f.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FormSubmission>> ListByIdsForUpdateAsync(
        IEnumerable<FormSubmissionId> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.ToList();
        return await Context.FormSubmissions
            .Where(f => idList.Contains(f.Id) && f.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(FormSubmission submission, CancellationToken cancellationToken)
    {
        await Context.FormSubmissions.AddAsync(submission, cancellationToken).ConfigureAwait(false);
    }
}
