using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Cec;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Persistence port for CEC dossier aggregates and Chair adjudication determination records.
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
/// @req:PRD-SYS-053
/// </summary>
public interface ICecRepository
{
    Task<CecDossier?> GetDossierByIdAsync(string dossierId, CancellationToken cancellationToken);
    Task<CecDossier?> GetDossierForUpdateAsync(string dossierId, CancellationToken cancellationToken);
    Task<List<CecDossier>> GetDossiersAsync(string? studyId, string? workflowStatus, CancellationToken cancellationToken);
    Task AddDossierAsync(CecDossier dossier, CancellationToken cancellationToken);
    Task AddChairAdjudicationAsync(CecChairAdjudication adjudication, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
