using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Cec.Sdtm;
using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Cec;

/// <summary>
/// Application service port governing CEC blinded dual-reader assessments, FSM transitions,
/// Chair tie-breaker adjudications, and SDTM CE dataset compilation.
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
/// @req:PRD-CEC-003
/// @req:PRD-SYS-053
/// @req:PRD-SYS-124
/// </summary>
public interface ICecApplicationService
{
    Task<Result<ChairAdjudicationResponse>> ChairAdjudicateAsync(
        string dossierId,
        ChairAdjudicateRequest request,
        AuthenticatedCaller? signer,
        CancellationToken cancellationToken);

    Task<Result<ReaderAssessmentResponse>> SubmitReaderAssessmentAsync(
        string dossierId,
        SubmitReaderAssessmentRequest request,
        AuthenticatedCaller? caller,
        CancellationToken cancellationToken);

    Task<Result<CecDossierBlindedView>> GetBlindedDossierAsync(
        string dossierId,
        AuthenticatedCaller? caller,
        CancellationToken cancellationToken);

    Task<Result<List<CecDossierBlindedView>>> GetDossiersListAsync(
        string? studyId,
        string? workflowStatus,
        AuthenticatedCaller? caller,
        CancellationToken cancellationToken);

    Task<Result<CecDossierBlindedView>> CompileDossierAsync(
        CompileDossierRequest request,
        AuthenticatedCaller? caller,
        CancellationToken cancellationToken);

    Task<Result<SdtmCeDataset>> ExportSdtmCeAsync(
        string? studyId,
        CancellationToken cancellationToken);
}
