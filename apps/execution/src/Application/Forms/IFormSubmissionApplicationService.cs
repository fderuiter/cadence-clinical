using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// Service abstraction governing form submissions, multi-field eCRF page ingestion, and PI signature attestation.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:SRS-EDC-002
/// @req:SRS-EDC-003
/// </summary>
public interface IFormSubmissionApplicationService
{
    Task<Result<FormSubmissionResponse>> CreateAsync(FormSubmissionCreateRequest request, CancellationToken cancellationToken);

    Task<Result<FormSubmissionResponse>> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<FormSubmissionResponse>>> ListBySubjectAsync(string studyId, string subjectId, CancellationToken cancellationToken);

    Task<Result<FormSubmissionResponse>> UpdatePayloadAsync(string id, string newPayload, string? formHash, CancellationToken cancellationToken);

    Task<Result<FormSubmissionResponse>> UpdatePayloadAsync(string id, string newPayload, CancellationToken cancellationToken)
        => UpdatePayloadAsync(id, newPayload, null, cancellationToken);

    Task<Result<FormSubmissionResponse>> CompleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Applies the Principal Investigator signature. The signer is always <paramref name="signer"/>, the
    /// authenticated caller; a manifest naming anyone else is rejected.
    /// </summary>
    Task<Result<FormSubmissionResponse>> ApproveAsync(
        string id,
        FormSubmissionApproveRequest request,
        AuthenticatedCaller? signer,
        CancellationToken cancellationToken);

    Task<Result<FormSubmissionResponse>> UnlockAsync(string id, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Executes a Principal Investigator batch electronic sign-off across multiple form, visit, or subject targets.
    /// </summary>
    Task<Result<BatchSignOffResponse>> BatchSignOffAsync(
        BatchSignOffRequest request,
        AuthenticatedCaller? signer,
        string? sigToken,
        CancellationToken cancellationToken);
}
