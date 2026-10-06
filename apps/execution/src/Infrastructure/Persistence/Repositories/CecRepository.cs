using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Ports;

using Microsoft.EntityFrameworkCore;

using Polly;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core 10 repository adapter for <see cref="CecDossier"/> and <see cref="CecChairAdjudication"/> aggregates.
/// All read-only queries enforce <c>.AsNoTracking()</c>.
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
/// @req:PRD-SYS-053
/// </summary>
public sealed class CecRepository(ExecutionDbContext context, ResiliencePipeline saveChangesResilience)
    : ExecutionRepositoryBase(context, saveChangesResilience), ICecRepository
{
    public async Task<CecDossier?> GetDossierByIdAsync(string dossierId, CancellationToken cancellationToken) =>
        await Context.CecDossiers
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == dossierId, cancellationToken)
            .ConfigureAwait(false);

    public async Task<CecDossier?> GetDossierForUpdateAsync(string dossierId, CancellationToken cancellationToken) =>
        await Context.CecDossiers
            .FirstOrDefaultAsync(d => d.Id == dossierId, cancellationToken)
            .ConfigureAwait(false);

    public async Task<List<CecDossier>> GetDossiersAsync(string? studyId, string? workflowStatus, CancellationToken cancellationToken)
    {
        var query = Context.CecDossiers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(d => d.StudyId == studyId);
        }

        if (!string.IsNullOrWhiteSpace(workflowStatus) && !string.Equals(workflowStatus, "ALL", System.StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(d => d.WorkflowStatus == workflowStatus);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddDossierAsync(CecDossier dossier, CancellationToken cancellationToken)
    {
        await Context.CecDossiers.AddAsync(dossier, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddChairAdjudicationAsync(CecChairAdjudication adjudication, CancellationToken cancellationToken)
    {
        await Context.CecChairAdjudications.AddAsync(adjudication, cancellationToken).ConfigureAwait(false);
    }
}
