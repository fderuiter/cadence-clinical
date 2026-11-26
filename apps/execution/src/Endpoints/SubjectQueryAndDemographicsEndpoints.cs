using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Randomization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Execution.Endpoints;

public static partial class SubjectEndpoints
{
    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Debug,
        Message = "Fetching detail for subject '{SubjectId}' (isBlindedCaller: {IsBlindedCaller})")]
    public static partial void LogFetchingSubjectDetail(this ILogger logger, string subjectId, bool isBlindedCaller);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Information,
        Message = "Updating demographics for subject '{SubjectId}' with reason '{ChangeReason}'")]
    public static partial void LogUpdatingSubjectDemographics(this ILogger logger, string subjectId, string changeReason);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Information,
        Message = "Deleting demographics for subject '{SubjectId}' with reason '{ChangeReason}'")]
    public static partial void LogDeletingSubjectDemographics(this ILogger logger, string subjectId, string changeReason);

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Information,
        Message = "Assigning site '{SiteId}' to subject '{SubjectId}' with reason '{ChangeReason}'")]
    public static partial void LogAssigningSubjectSite(this ILogger logger, string subjectId, string? siteId, string changeReason);

    /// <summary>
    /// <c>GET /api/v1/execution/subjects?study_id=&amp;site_id=&amp;status=&amp;q=&amp;limit=&amp;offset=</c>. The list
    /// carries no treatment group, so it needs no blinding check.
    /// </summary>
    public static async Task<IResult> ListSubjectsAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "offset")] int? offset,
        HttpContext http,
        ISubjectApplicationService svc,
        CancellationToken ct,
        [FromQuery(Name = "q")] string? q = null)
    {
        ct.ThrowIfCancellationRequested();

        IResult? studyDenied = !string.IsNullOrWhiteSpace(studyId)
            ? ClinicalScopeGuard.RequireStudy(http, studyId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            : ClinicalScopeGuard.RequireAnyStudyScope(http, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);
        if (studyDenied is not null)
        {
            return studyDenied;
        }

        if (ClinicalScopeGuard.ResolveSingleSiteFilter(http, siteId, out var siteFilter, out _, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } siteDenied)
        {
            return siteDenied;
        }

        var result = await svc.ListAsync(
                studyId,
                siteId,
                status,
                limit,
                offset,
                ct,
                string.IsNullOrWhiteSpace(siteId) ? siteFilter : null,
                q)
            .ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>GET /api/v1/execution/subjects/counts?study_id=&amp;site_id=</c>. Counts subjects grouped by lifecycle status
    /// under the same study and site scope guard as <see cref="ListSubjectsAsync"/>.
    /// </summary>
    public static async Task<IResult> GetSubjectCountsAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        HttpContext http,
        ISubjectApplicationService svc,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IResult? studyDenied = !string.IsNullOrWhiteSpace(studyId)
            ? ClinicalScopeGuard.RequireStudy(http, studyId, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true)
            : ClinicalScopeGuard.RequireAnyStudyScope(http, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);
        if (studyDenied is not null)
        {
            return studyDenied;
        }

        if (ClinicalScopeGuard.ResolveSingleSiteFilter(http, siteId, out var siteFilter, out _, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true) is { } siteDenied)
        {
            return siteDenied;
        }

        var result = await svc.GetCountsAsync(
                studyId,
                siteId,
                ct,
                string.IsNullOrWhiteSpace(siteId) ? siteFilter : null)
            .ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>GET /api/v1/execution/subjects/{subjectId}</c>. Blinded by default: the treatment group and kit are revealed
    /// from the randomisation allocation only to a caller whose signed identity carries
    /// <see cref="CadenceClaimTypes.UnblindedAccess"/>, or to an unblinding authority
    /// (<see cref="SubjectAuthority.CanUnblind"/>) inspecting an emergency-<c>UNBLINDED</c> subject (#4943, #5607).
    /// Study and site scope on the loaded <see cref="ClinicalSubject"/> are verified before <c>RevealAsync</c> runs.
    /// </summary>
    public static async Task<IResult> GetSubjectDetailAsync(
        string subjectId, HttpContext http, ISubjectApplicationService svc, IRandomizationApplicationService randomization,
        CancellationToken ct, [FromServices] ILogger? logger = null)
    {
        ct.ThrowIfCancellationRequested();

        if (ClinicalScopeGuard.RequireVerifiedTenant(http, ClinicalScopeGuard.ForbiddenCode) is { } tenantDenied)
        {
            return tenantDenied;
        }

        bool hasUnblindedAccess = ClinicalScopeGuard.ResolvePrincipal(http)?.UnblindedAccess is true;
        bool isBlindedCaller = !hasUnblindedAccess;

        if (logger != null)
        {
            logger.LogFetchingSubjectDetail(subjectId, isBlindedCaller);
        }

        var result = await svc.GetDetailAsync(subjectId, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return GuardMissingRecordScope(http) ?? result.Error!.ToProblemResult();
        }

        var detail = result.Value!;
        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(
                http,
                detail.StudyId.Value,
                detail.SiteId?.Value,
                allowUnassignedSiteForScopedCaller: true,
                code: ClinicalScopeGuard.ForbiddenCode,
                requireVerifiedTenant: true) is { } scopeDenied)
        {
            return scopeDenied;
        }

        bool canRevealEmergencyUnblinded =
            string.Equals(detail.Status, SubjectStateCodec.ToCode(SubjectState.Unblinded), StringComparison.Ordinal)
            && ResolveCaller(http)?.FirstRoleMatching(SubjectAuthority.CanUnblind) is not null;

        if (!hasUnblindedAccess && !canRevealEmergencyUnblinded)
        {
            return Results.Ok(detail);
        }

        var revealed = await randomization.RevealAsync(subjectId, ct).ConfigureAwait(false);
        return Results.Ok(revealed is null
            ? detail
            : detail with { TreatmentGroup = revealed.Arm, InvestigationalProductId = revealed.KitNumber });
    }

    /// <summary>
    /// <c>PUT /api/v1/execution/subjects/{subjectId}/demographics</c>. The null-forgiving <c>!</c> below is
    /// provably safe, not a suppressed nullable warning: <see cref="ChangeReasonHeaderFilter"/> (applied to
    /// this route, see <see cref="MapSubjectEndpoints"/>) guarantees <c>X-Change-Reason</c> is present and
    /// non-whitespace before this delegate ever runs — matching that filter's own doc comment and
    /// `02_technical_design.md` §3.3's reference implementation for this exact route.
    /// </summary>
    public static async Task<IResult> UpdateSubjectDemographicsAsync(
        string subjectId,
        SubjectDemographicsUpdateRequest request,
        HttpContext http,
        ISubjectApplicationService svc,
        CancellationToken ct,
        [FromServices] ILogger? logger = null,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        ct.ThrowIfCancellationRequested();

        string changeReason = ChangeReasonHeader.Read(http.Request)!;

        if (SubjectAuthority.AuthorizeParticipantChange(ResolveCaller(http)) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        var (scopeDenied, resolvedScope) = await GuardSubjectAndResolveLockScopeAsync(subjectId, http, svc, ct).ConfigureAwait(false);
        if (scopeDenied is not null)
        {
            return scopeDenied;
        }

        if (resolvedScope is { } ls
            && await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, ls.StudyCode, ls.SiteCode, ls.SubjectCode, ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        if (logger != null)
        {
            logger.LogUpdatingSubjectDemographics(subjectId, changeReason);
        }

        var result = await svc.UpdateDemographicsAsync(subjectId, request, changeReason, ct).ConfigureAwait(false);
        return result.Match(r => Results.Ok(r), e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>DELETE /api/v1/execution/subjects/{subjectId}/demographics</c>, closing MIG-SUBJ-022's noted
    /// `!`-safety gap: this route did not exist before this ticket, so <see cref="ChangeReasonHeaderFilter"/>
    /// is applied here for the first time rather than retrofitted. The null-forgiving <c>!</c> below has the
    /// exact same justification as <see cref="UpdateSubjectDemographicsAsync"/>'s.
    /// </summary>
    public static async Task<IResult> DeleteSubjectDemographicsAsync(
        string subjectId,
        HttpContext http,
        ISubjectApplicationService svc,
        CancellationToken ct,
        [FromServices] ILogger? logger = null,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        ct.ThrowIfCancellationRequested();

        string changeReason = ChangeReasonHeader.Read(http.Request)!;

        if (SubjectAuthority.AuthorizeParticipantChange(ResolveCaller(http)) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        var (scopeDenied, resolvedScope) = await GuardSubjectAndResolveLockScopeAsync(subjectId, http, svc, ct).ConfigureAwait(false);
        if (scopeDenied is not null)
        {
            return scopeDenied;
        }

        if (resolvedScope is { } ls
            && await ExecutionLockGuard.EnsureUnlockedAsync(lockGuard, ls.StudyCode, ls.SiteCode, ls.SubjectCode, ct).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

        if (logger != null)
        {
            logger.LogDeletingSubjectDemographics(subjectId, changeReason);
        }

        var result = await svc.DeleteDemographicsAsync(subjectId, changeReason, ct).ConfigureAwait(false);
        return result.Match(r => Results.Ok(r), e => e.ToProblemResult());
    }

    /// <summary>
    /// <c>PATCH /api/v1/execution/subjects/{subjectId}/site</c>. Assigns a clinical site to an unassigned subject and
    /// synchronizes screening enrollment with the kernel.
    /// </summary>
    public static async Task<IResult> AssignSubjectSiteAsync(
        string subjectId,
        SubjectSiteAssignmentRequest request,
        HttpContext http,
        ISubjectApplicationService svc,
        CancellationToken ct,
        [FromServices] ILogger? logger = null,
        [FromServices] IKernelLockGuard? lockGuard = null)
    {
        ct.ThrowIfCancellationRequested();

        string changeReason = ChangeReasonHeader.Read(http.Request)
            ?? throw new InvalidOperationException("ChangeReasonHeaderFilter should have rejected this request.");

        if (SubjectAuthority.AuthorizeParticipantChange(ResolveCaller(http)) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        if (ClinicalScopeGuard.RequireVerifiedTenant(http, ClinicalScopeGuard.ForbiddenCode) is { } tenantDenied)
        {
            return tenantDenied;
        }

        var scope = await svc.GetSubjectScopeAsync(subjectId, ct).ConfigureAwait(false);
        var lockScope = await svc.GetSubjectLockScopeAsync(subjectId, ct).ConfigureAwait(false);
        if (scope is { } s)
        {
            if (ClinicalScopeGuard.RequireStudyAndOptionalSite(
                    http,
                    s.StudyId.Value,
                    s.SiteId?.Value,
                    allowUnassignedSiteForScopedCaller: true,
                    code: ClinicalScopeGuard.ForbiddenCode,
                    requireVerifiedTenant: true) is { } existingScopeDenied)
            {
                return existingScopeDenied;
            }

            if (!string.IsNullOrWhiteSpace(request.SiteCode)
                && ClinicalScopeGuard.RequireStudyAndOptionalSite(
                    http,
                    s.StudyId.Value,
                    request.SiteCode.Trim(),
                    allowUnassignedSiteForScopedCaller: false,
                    code: ClinicalScopeGuard.ForbiddenCode,
                    requireVerifiedTenant: true) is { } targetScopeDenied)
            {
                return targetScopeDenied;
            }

            string resolvedSubjectCode = lockScope?.SubjectCode.Value ?? subjectId;
            if (await ExecutionLockGuard.EnsureUnlockedAsync(
                    lockGuard,
                    s.StudyId.Value,
                    s.SiteId?.Value,
                    resolvedSubjectCode,
                    ct).ConfigureAwait(false) is { } existingLocked)
            {
                return existingLocked;
            }

            if (!string.IsNullOrWhiteSpace(request.SiteCode)
                && !string.Equals(s.SiteId?.Value, request.SiteCode.Trim(), StringComparison.Ordinal)
                && await ExecutionLockGuard.EnsureUnlockedAsync(
                    lockGuard,
                    s.StudyId.Value,
                    request.SiteCode.Trim(),
                    resolvedSubjectCode,
                    ct).ConfigureAwait(false) is { } targetLocked)
            {
                return targetLocked;
            }
        }
        else
        {
            if (GuardMissingRecordScope(http) is { } missingDenied)
            {
                return missingDenied;
            }

            if (!string.IsNullOrWhiteSpace(request.SiteCode)
                && ClinicalScopeGuard.RequireSite(
                    http,
                    request.SiteCode.Trim(),
                    ClinicalScopeGuard.ForbiddenCode,
                    requireVerifiedTenant: true) is { } targetSiteDenied)
            {
                return targetSiteDenied;
            }
        }

        if (logger != null)
        {
            logger.LogAssigningSubjectSite(subjectId, request.SiteCode, changeReason);
        }

        var result = await svc.AssignSiteAsync(subjectId, request, changeReason, ct).ConfigureAwait(false);
        return result.Match(r => Results.Ok(r), e => e.ToProblemResult());
    }
}
