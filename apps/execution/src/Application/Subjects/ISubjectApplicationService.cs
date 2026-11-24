using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Direct typed application-service contract for the whole `execution-subjects` slice (ADR-2428 §3.2 — no
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

    /// <summary>Assigns a clinical site to a screening subject and synchronizes screening enrollment with the kernel.</summary>
    Task<Result<SubjectResponse>> AssignSiteAsync(string subjectIdOrCode, SubjectSiteAssignmentRequest request, string changeReason, CancellationToken ct);

    /// <summary>Transitions a subject's state (SCN-003/SCN-005). Implemented by MIG-SUBJ-019.</summary>
    Task<Result<SubjectResponse>> AdvanceStateAsync(string subjectIdOrCode, SubjectStateUpdateRequest request, string changeReason, CancellationToken ct);

    /// <summary>Performs an emergency unblinding under a verified Part 11 step-up signature.</summary>
    Task<Result<SubjectResponse>> UnblindAsync(string subjectIdOrCode, string changeReason, CancellationToken ct) =>
        Task.FromResult(Result<SubjectResponse>.Failure(SubjectErrors.UnblindThroughDedicatedEndpoint()));

    /// <summary>Re-encrypts and replaces demographics/strat factors, subject to the post-randomization lock
    /// (SCN-004/SCN-006). Implemented by MIG-SUBJ-019.</summary>
    Task<Result<SubjectResponse>> UpdateDemographicsAsync(string subjectIdOrCode, SubjectDemographicsUpdateRequest request, string changeReason, CancellationToken ct);

    /// <summary>Clears demographics, subject to the same post-randomization lock. Implemented by MIG-SUBJ-019.</summary>
    Task<Result<SubjectResponse>> DeleteDemographicsAsync(string subjectIdOrCode, string changeReason, CancellationToken ct);

    /// <summary>Subject detail resolved by surrogate UUID or subject code, without any treatment arm; the endpoint adds
    /// the arm for an unblinded caller from the randomisation allocation (#5607).</summary>
    Task<Result<SubjectDetailResponse>> GetDetailAsync(string subjectId, CancellationToken ct);

    /// <summary>
    /// Lists one page of a study's subjects in enrollment order, optionally narrowed to a site, status, or subject code
    /// substring (<paramref name="query"/>). <paramref name="studyId"/> is required; <paramref name="limit"/> defaults
    /// to 50 and is capped at 200.
    /// </summary>
    Task<Result<SubjectListResponse>> ListAsync(string? studyId, string? siteId, string? status, int? limit, int? offset,
        CancellationToken ct, IReadOnlyCollection<string>? allowedSiteIds = null, string? query = null);

    /// <summary>
    /// Counts a study's subjects grouped by lifecycle status, optionally narrowed to a site or caller-permitted sites.
    /// </summary>
    Task<Result<SubjectCountsResponse>> GetCountsAsync(
        string? studyId,
        string? siteId,
        CancellationToken ct,
        IReadOnlyCollection<string>? allowedSiteIds = null);

    /// <summary>Resolves a participant's study and site codes without mutating state or decrypting data.</summary>
    Task<(StudyCode StudyId, SiteCode? SiteId)?> GetSubjectScopeAsync(string subjectIdOrCode, CancellationToken ct = default) =>
        Task.FromResult<(StudyCode StudyId, SiteCode? SiteId)?>(null);

    /// <summary>Resolves a participant's study, optional site, and canonical subject code for kernel lock checks.</summary>
    Task<(StudyCode StudyCode, SiteCode? SiteCode, SubjectCode SubjectCode)?> GetSubjectLockScopeAsync(string subjectIdOrCode, CancellationToken ct = default) =>
        Task.FromResult<(StudyCode StudyCode, SiteCode? SiteCode, SubjectCode SubjectCode)?>(null);

    /// <summary>Evaluates screening eligibility and transitions to <c>ScreenFailed</c>/<c>Enrolled</c>
    /// (SCN-005/SCN-007). Implemented by MIG-SUBJ-019.</summary>
    Task<Result<SubjectResponse>> EvaluateAndTransitionScreeningAsync(string subjectId, CancellationToken ct);
}
