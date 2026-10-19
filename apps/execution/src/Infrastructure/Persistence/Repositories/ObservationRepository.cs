using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core 10 repository adapter for <see cref="ClinicalObservation"/> aggregates.
/// All query-only reads strictly enforce <c>.AsNoTracking()</c>.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public sealed class ObservationRepository(ExecutionDbContext context)
    : ExecutionRepositoryBase(context), IObservationRepository
{
    public Task<ClinicalObservation?> GetByIdAsync(ObservationId id, CancellationToken cancellationToken = default) =>
        GetByIdAsync(id, includeInactive: false, cancellationToken);

    public async Task<ClinicalObservation?> GetByIdAsync(
        ObservationId id,
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        await Context.ClinicalObservations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && (includeInactive || o.IsActive), cancellationToken)
            .ConfigureAwait(false);

    public async Task<ClinicalObservation?> GetForUpdateAsync(ObservationId id, CancellationToken cancellationToken = default) =>
        await Context.ClinicalObservations
            .FirstOrDefaultAsync(o => o.Id == id && o.IsActive, cancellationToken)
            .ConfigureAwait(false);

    public Task<IReadOnlyList<ClinicalObservation>> ListBySubjectAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        CancellationToken cancellationToken = default) =>
        ListBySubjectAsync(studyId, subjectId, includeInactive: false, cancellationToken);

    public async Task<IReadOnlyList<ClinicalObservation>> ListBySubjectAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        await Context.ClinicalObservations
            .AsNoTracking()
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId && (includeInactive || o.IsActive))
            .OrderByDescending(o => o.ObservationDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<IReadOnlyList<ClinicalObservation>> ListByCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        DomainCode? domain,
        TestCode? testCode,
        CancellationToken cancellationToken = default) =>
        ListByCoordinatesAsync(studyId, subjectId, visitId, domain, testCode, includeInactive: false, cancellationToken);

    public async Task<IReadOnlyList<ClinicalObservation>> ListByCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        DomainCode? domain,
        TestCode? testCode,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = Context.ClinicalObservations
            .AsNoTracking()
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId && (includeInactive || o.IsActive));

        if (visitId != null)
        {
            query = query.Where(o => o.VisitId == visitId);
        }

        if (domain.HasValue)
        {
            query = query.Where(o => o.Domain == domain.Value);
        }

        if (testCode.HasValue)
        {
            query = query.Where(o => o.TestCode == testCode.Value);
        }

        return await query
            .OrderByDescending(o => o.ObservationDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClinicalObservation>> ListByFormSubmissionAsync(
        FormSubmissionId formSubmissionId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        await Context.ClinicalObservations
            .AsNoTracking()
            .Where(o => o.FormSubmissionId == formSubmissionId && (includeInactive || o.IsActive))
            .OrderByDescending(o => o.ObservationDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task DeactivateByFormSubmissionIdAsync(
        FormSubmissionId formSubmissionId,
        CancellationToken cancellationToken = default)
    {
        var activeObservations = await Context.ClinicalObservations
            .Where(o => o.FormSubmissionId == formSubmissionId && o.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var obs in activeObservations)
        {
            obs.Deactivate();
        }
    }

    public async Task AddAsync(ClinicalObservation observation, CancellationToken cancellationToken = default)
    {
        await Context.ClinicalObservations.AddAsync(observation, cancellationToken).ConfigureAwait(false);
    }
}
