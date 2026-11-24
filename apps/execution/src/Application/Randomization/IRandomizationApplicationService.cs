using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Randomization;

/// <summary>
/// Randomisation schemes, participant randomisation with kit assignment, and the unblinded arm reveal (#5607).
/// @req:PRD-SUB-001
/// </summary>
public interface IRandomizationApplicationService
{
    /// <summary>Drafts the study's next scheme version; it allocates nobody until approved.</summary>
    Task<Result<RandomizationSchemeResponse>> DraftSchemeAsync(RandomizationSchemeDraftRequest request, CancellationToken ct);

    /// <summary>
    /// Approves a draft version (go-live sign-off by someone other than its author), retires the version it replaces,
    /// and generates a permuted-block list.
    /// </summary>
    Task<Result<RandomizationSchemeResponse>> ApproveSchemeAsync(Guid schemeId, string changeReason, CancellationToken ct);

    /// <summary>Every version of a study's scheme, newest first, without any list or allocation.</summary>
    Task<Result<IReadOnlyList<RandomizationSchemeResponse>>> ListSchemesAsync(string? studyId, CancellationToken ct);

    /// <summary>
    /// Randomises an enrolled participant under the live scheme, refusing (with an audit entry) at a site that is not
    /// activated or without a current consent, and assigns a kit when the scheme needs one.
    /// </summary>
    Task<Result<RandomizationResponse>> RandomizeAsync(string subjectIdOrCode, RandomizeSubjectRequest request, string changeReason,
        CancellationToken ct);

    /// <summary>
    /// The participant's arm and kit, or null when they are not randomised. Only the unblinded subject read calls this.
    /// </summary>
    Task<RevealedAllocation?> RevealAsync(string subjectIdOrCode, CancellationToken ct);

    /// <summary>Resolves a scheme's owning study code without mutating state, for endpoint scope checks.</summary>
    Task<StudyCode?> GetSchemeStudyAsync(Guid schemeId, CancellationToken ct = default) =>
        Task.FromResult<StudyCode?>(null);

    /// <summary>Resolves a participant's study and site codes without mutating state, for endpoint scope checks.</summary>
    Task<(StudyCode StudyId, SiteCode? SiteId)?> GetSubjectScopeAsync(string subjectIdOrCode, CancellationToken ct = default) =>
        Task.FromResult<(StudyCode StudyId, SiteCode? SiteId)?>(null);

    /// <summary>Resolves a participant's study, optional site, and canonical subject code for kernel lock checks.</summary>
    Task<(StudyCode StudyCode, SiteCode? SiteCode, SubjectCode SubjectCode)?> GetSubjectLockScopeAsync(string subjectIdOrCode, CancellationToken ct = default) =>
        Task.FromResult<(StudyCode StudyCode, SiteCode? SiteCode, SubjectCode SubjectCode)?>(null);
}
