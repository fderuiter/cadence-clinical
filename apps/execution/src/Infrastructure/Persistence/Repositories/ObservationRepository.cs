using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel;

using Microsoft.EntityFrameworkCore;

using Polly;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core 10 repository adapter for <see cref="ClinicalObservation"/> aggregates.
/// All query-only reads strictly enforce <c>.AsNoTracking()</c>.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public sealed class ObservationRepository(ExecutionDbContext context, ResiliencePipeline saveChangesResilience)
    : ExecutionRepositoryBase(context, saveChangesResilience), IObservationRepository
{
    public async Task<ClinicalObservation?> GetByIdAsync(ObservationId id, CancellationToken cancellationToken) =>
        await Context.ClinicalObservations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<ClinicalObservation?> GetForUpdateAsync(ObservationId id, CancellationToken cancellationToken) =>
        await Context.ClinicalObservations
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<ClinicalObservation>> ListBySubjectAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        CancellationToken cancellationToken) =>
        await Context.ClinicalObservations
            .AsNoTracking()
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId)
            .OrderByDescending(o => o.ObservationDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<ClinicalObservation>> ListByCoordinatesAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        string? visitId,
        DomainCode? domain,
        TestCode? testCode,
        CancellationToken cancellationToken)
    {
        var query = Context.ClinicalObservations
            .AsNoTracking()
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId);

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

    public async Task AddAsync(ClinicalObservation observation, CancellationToken cancellationToken)
    {
        await Context.ClinicalObservations.AddAsync(observation, cancellationToken).ConfigureAwait(false);
    }
}
