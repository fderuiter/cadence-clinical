using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Direct typed application-service contract for the whole `execution-subjects` slice (ADR-2425 §3.2 — no
/// MediatR), per <c>02_technical_design.md</c> §3.1. All 7 methods are declared here by `MIG-SUBJ-018` — the
/// first ticket to touch this file — so `MIG-SUBJ-019` can add the remaining 5 method <i>bodies</i> to
/// <see cref="SubjectApplicationService"/> without a second interface-authoring pass. This ticket implements
/// only <see cref="CreateAsync"/> and <see cref="RecordConsentAsync"/>; see
/// <see cref="SubjectApplicationService"/>'s own doc comment for how the other 5 are kept buildable in the
/// meantime.
/// Legacy Python source being replaced: <c>apps/execution/application/services.py</c>'s
/// <c>SubjectLifecycleService</c> and the business-logic portions of
/// <c>apps/execution/presentation/routers/subjects.py</c>.
/// @req:PRD-SUB-001
/// </summary>
public interface ISubjectApplicationService
{
    /// <summary>Enrolls a new <see cref="Domain.Subjects.ClinicalSubject"/> (SCN-001). Implemented by MIG-SUBJ-018.</summary>
    Task<Result<SubjectResponse>> CreateAsync(SubjectCreateRequest request, CancellationToken ct);

    /// <summary>Records or merges a <see cref="Domain.Subjects.SubjectConsent"/> (SCN-002). Implemented by MIG-SUBJ-018.</summary>
    Task<Result<SubjectConsentResponse>> RecordConsentAsync(string subjectIdOrCode, SubjectConsentRequest request, CancellationToken ct);

    /// <summary>Transitions a subject's state (SCN-003/SCN-005). Implemented by MIG-SUBJ-019.</summary>
    Task<Result<SubjectResponse>> AdvanceStateAsync(string subjectIdOrCode, SubjectStateUpdateRequest request, string changeReason, CancellationToken ct);

    /// <summary>Re-encrypts and replaces demographics/strat factors, subject to the post-randomization lock
    /// (SCN-004/SCN-006). Implemented by MIG-SUBJ-019.</summary>
    Task<Result<SubjectResponse>> UpdateDemographicsAsync(string subjectIdOrCode, SubjectDemographicsUpdateRequest request, string changeReason, CancellationToken ct);

    /// <summary>Clears demographics, subject to the same post-randomization lock. Implemented by MIG-SUBJ-019.</summary>
    Task<Result<SubjectResponse>> DeleteDemographicsAsync(string subjectIdOrCode, string changeReason, CancellationToken ct);

    /// <summary>Full subject detail, RTSM-blinding-redacted for a blinded caller. Implemented by MIG-SUBJ-019.</summary>
    Task<Result<SubjectDetailResponse>> GetDetailAsync(string subjectId, bool isBlindedCaller, CancellationToken ct);

    /// <summary>Evaluates screening eligibility and transitions to <c>ScreenFailed</c>/<c>Enrolled</c>
    /// (SCN-005/SCN-007). Implemented by MIG-SUBJ-019.</summary>
    Task<Result<SubjectResponse>> EvaluateAndTransitionScreeningAsync(string subjectId, CancellationToken ct);
}
