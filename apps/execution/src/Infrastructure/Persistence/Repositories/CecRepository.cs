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

    public async Task AddDossierAsync(CecDossier dossier, CancellationToken cancellationToken)
    {
        await Context.CecDossiers.AddAsync(dossier, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddChairAdjudicationAsync(CecChairAdjudication adjudication, CancellationToken cancellationToken)
    {
        await Context.CecChairAdjudications.AddAsync(adjudication, cancellationToken).ConfigureAwait(false);
    }
}
