using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Randomization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Execution.Endpoints;

public static partial class SubjectEndpoints
{
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

    public static async Task<IResult> GetSubjectDetailAsync(
        string subjectId,
        HttpContext http,
        ISubjectApplicationService svc,
        IRandomizationApplicationService randomization,
        CancellationToken ct,
        [FromServices] ILogger? logger = null)
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

    private static async Task<(IResult? Denied, (string StudyCode, string? SiteCode, string SubjectCode)? Scope)> GuardSubjectAndResolveLockScopeAsync(
        string subjectId,
        HttpContext http,
        ISubjectApplicationService svc,
        CancellationToken ct)
    {
        if (ClinicalScopeGuard.RequireVerifiedTenant(http, ClinicalScopeGuard.ForbiddenCode) is { } tenantDenied)
        {
            return (tenantDenied, null);
        }

        var scope = await svc.GetSubjectScopeAsync(subjectId, ct).ConfigureAwait(false);
        var lockScope = await svc.GetSubjectLockScopeAsync(subjectId, ct).ConfigureAwait(false);
        if (scope is { } s)
        {
            var denied = ClinicalScopeGuard.RequireStudyAndOptionalSite(
                http,
                s.StudyId.Value,
                s.SiteId?.Value,
                allowUnassignedSiteForScopedCaller: true,
                code: ClinicalScopeGuard.ForbiddenCode,
                requireVerifiedTenant: true);
            return (denied, (s.StudyId.Value, s.SiteId?.Value, lockScope?.SubjectCode.Value ?? subjectId));
        }

        if (lockScope is { } ls)
        {
            var denied = ClinicalScopeGuard.RequireStudyAndOptionalSite(
                http,
                ls.StudyCode.Value,
                ls.SiteCode?.Value,
                allowUnassignedSiteForScopedCaller: true,
                code: ClinicalScopeGuard.ForbiddenCode,
                requireVerifiedTenant: true);
            return (denied, (ls.StudyCode.Value, ls.SiteCode?.Value, ls.SubjectCode.Value));
        }

        return (GuardMissingRecordScope(http), null);
    }

    private static IResult? GuardMissingRecordScope(HttpContext http) =>
        ClinicalScopeGuard.RequireAnyStudyAndSiteScope(http, ClinicalScopeGuard.ForbiddenCode, requireVerifiedTenant: true);
}
