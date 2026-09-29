using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.UnitTests.Execution.Fakes;

/// <summary>
/// In-memory fake implementation of <see cref="IFormSubmissionRepository"/> for isolated unit tests.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-002
/// </summary>
public sealed class InMemoryFormSubmissionRepository : IFormSubmissionRepository
{
    private readonly ConcurrentDictionary<FormSubmissionId, FormSubmission> _store = new();

    public Task<FormSubmission?> GetForUpdateAsync(FormSubmissionId id, CancellationToken cancellationToken) =>
        GetByIdAsync(id, cancellationToken);

    public Task<FormSubmission?> GetByIdAsync(FormSubmissionId id, CancellationToken cancellationToken)
    {
        _store.TryGetValue(id, out var submission);
        return Task.FromResult(submission != null && submission.IsActive ? submission : null);
    }

    public Task<FormSubmission?> GetByCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        FormId formId,
        CancellationToken cancellationToken)
    {
        FormSubmission? match = _store.Values.FirstOrDefault(f =>
            f.StudyId == studyId &&
            f.SubjectId == subjectId &&
            f.VisitId == visitId &&
            f.FormId == formId &&
            f.IsActive);

        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<FormSubmission>> ListBySubjectAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FormSubmission> list = _store.Values
            .Where(f => f.StudyId == studyId && f.SubjectId == subjectId && f.IsActive)
            .ToList();

        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<FormSubmission>> ListByVisitAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string visitId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FormSubmission> list = _store.Values
            .Where(f => f.StudyId == studyId && f.SubjectId == subjectId && f.VisitId == visitId && f.IsActive)
            .ToList();

        return Task.FromResult(list);
    }

    public Task AddAsync(FormSubmission submission, CancellationToken cancellationToken)
    {
        _store[submission.Id] = submission;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
