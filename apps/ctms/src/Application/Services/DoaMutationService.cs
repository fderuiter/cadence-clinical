using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Kernel;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Database.Concurrency;
using Cadence.Database.Tenancy;
using Cadence.Kernel;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Ctms.Application.Services;

public interface IDoaMutationService
{
    Task<IResult> DelegateTasksAsync(
        DelegationTaskRequest request,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null);

    Task<IResult> RevokeDelegationAsync(
        RevokeDelegationRequest request,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null);

    Task<IResult> SignOffDelegationAsync(
        DoaSignOffRequest request,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null);
}

public sealed class DoaMutationService(
    CtmsDbContext db,
    ICtmsUnitOfWork unitOfWork,
    ICtmsSiteScopeService siteScope,
    IClock clock,
    ICtmsKernelTrustClient? kernelTrust = null) : IDoaMutationService
{
    private readonly ICtmsKernelTrustClient _kernelTrust = kernelTrust ?? new InMemoryCtmsKernelTrustClient(clock);

    public async Task<IResult> DelegateTasksAsync(
        DelegationTaskRequest request,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null)
    {
        if (request is null)
        {
            return TypedResults.Problem(
                title: "Invalid request",
                detail: "Request payload cannot be empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (CtmsScopeGuard.RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return missingScope;
        }

        string? bodyStudy = request.Study?.TrimmedOrNull;
        if (!string.IsNullOrWhiteSpace(bodyStudy)
            && !string.IsNullOrWhiteSpace(studyId)
            && !string.Equals(bodyStudy, studyId.Trim(), StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Conflicting study ID",
                detail: "Study ID in request body conflicts with study_id query parameter.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? requestedStudyId = bodyStudy ?? studyId?.Trim();
        string? requestedSiteId = request.Site.TrimmedOrNull;

        if (string.IsNullOrWhiteSpace(requestedSiteId))
        {
            return TypedResults.Problem(
                title: "Site ID required",
                detail: "Site ID is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var scope = await siteScope.ResolveDoaSiteScopeAsync(httpContext, requestedSiteId, requestedStudyId, ct);
        if (scope.Error is not null)
        {
            return scope.Error;
        }

        if (string.IsNullOrWhiteSpace(request.StaffUserId))
        {
            return TypedResults.Problem(
                title: "Staff User ID required",
                detail: "Staff User ID is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.TaskCodes is null || request.TaskCodes.Count == 0)
        {
            return TypedResults.Problem(
                title: "Task codes required",
                detail: "At least one delegated task duty must be selected.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.StartDate))
        {
            return TypedResults.Problem(
                title: "Start date required",
                detail: "Effective start date is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!MonitoringVisitEndpoints.TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        if (scope.AuthorizedStudies.Count != 1)
        {
            return TypedResults.Problem(
                title: "Study ID required",
                detail: $"Site '{requestedSiteId}' belongs to multiple authorized studies; specify 'study_id'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string resolvedStudyId = scope.AuthorizedStudies.First();
        var now = clock.UtcNow;
        var (user, role) = CallerScope.ResolveActor(httpContext, "site_coordinator");

        var endorsement = new CtmsDoaTaskEndorsement
        {
            StudyId = new KernelStudyCode(resolvedStudyId),
            SiteId = new KernelSiteCode(requestedSiteId),
            KernelGrantId = null,
            StaffUserId = request.StaffUserId.Trim(),
            TaskCodes = JsonSerializer.Serialize(request.TaskCodes),
            StartDate = request.StartDate.Trim(),
            EndDate = null,
            IsActive = true,
            SignedOff = false,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            ReasonForChange = reason,
            VersionIndex = 1,
        };

        db.CtmsDoaTaskEndorsements.Add(endorsement);

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_DELEGATE",
            Details = $"Delegated tasks [{string.Join(", ", request.TaskCodes)}] to staff {endorsement.StaffUserId} at site {endorsement.SiteId.Raw} in study {endorsement.StudyId.Raw}.",
            StudyId = endorsement.StudyId.Raw,
            SiteId = endorsement.SiteId.Raw,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Created($"/api/v1/ctms/doa/sites/{endorsement.SiteId.Raw}/log", ToResponseDto(endorsement));
    }

    public async Task<IResult> RevokeDelegationAsync(
        RevokeDelegationRequest request,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
        {
            return TypedResults.Problem(
                title: "Record ID required",
                detail: "Delegation record ID is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (CtmsScopeGuard.RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return missingScope;
        }

        string? bodyStudy = request.Study?.TrimmedOrNull;
        if (!string.IsNullOrWhiteSpace(bodyStudy)
            && !string.IsNullOrWhiteSpace(studyId)
            && !string.Equals(bodyStudy, studyId.Trim(), StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Conflicting study ID",
                detail: "Study ID in request body conflicts with study_id query parameter.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? requestedStudyId = bodyStudy ?? studyId?.Trim();
        if (!string.IsNullOrWhiteSpace(requestedStudyId)
            && CtmsScopeGuard.RequireStudy(httpContext, requestedStudyId) is { } requestedStudyDenied)
        {
            return requestedStudyDenied;
        }

        string trimmedRecordId = request.RecordId.Trim();
        Guid? parsedGrantId = TryParseGrantGuid(trimmedRecordId);
        var endorsement = await db.CtmsDoaTaskEndorsements.FirstOrDefaultAsync(
            d => d.Id == trimmedRecordId || (parsedGrantId.HasValue && d.KernelGrantId == parsedGrantId.Value),
            ct);

        if (endorsement is null)
        {
            return TypedResults.Problem(
                title: "Delegation record not found",
                detail: $"Delegation record '{request.RecordId}' does not exist.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, endorsement.StudyId.Raw, endorsement.SiteId.Raw) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!string.IsNullOrWhiteSpace(requestedStudyId)
            && !string.Equals(endorsement.StudyId.Raw, requestedStudyId, StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Delegation record not found",
                detail: $"Delegation record '{request.RecordId}' does not exist in study '{requestedStudyId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!MonitoringVisitEndpoints.TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        if (!endorsement.IsActive)
        {
            return TypedResults.Problem(
                title: "Already revoked",
                detail: $"Delegation record '{request.RecordId}' is already inactive or revoked.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        StandaloneConcurrencyTokenInterceptor.EnforceExpectedVersion(
            db,
            endorsement,
            request.ExpectedVersionIndex,
            httpContext);

        var now = clock.UtcNow;
        var (user, role) = CallerScope.ResolveActor(httpContext, "investigator");
        var (tenantId, tenantCode, actorRoles) = await ResolveCallerContextAsync(httpContext, role, ct);
        long baselineVersion = db.Entry(endorsement).Property(x => x.VersionIndex).OriginalValue;

        if (endorsement.KernelGrantId is { } kernelGrantId && kernelGrantId != Guid.Empty)
        {
            await _kernelTrust.RevokeSiteGrantAsync(
                tenantId,
                tenantCode,
                kernelGrantId,
                endorsement.StudyId.Raw,
                endorsement.SiteId.Raw,
                now,
                user,
                actorRoles,
                reason,
                $"ctms-doa-revoke:{endorsement.Id}:{baselineVersion + 1}",
                ct);
        }

        endorsement.IsActive = false;
        endorsement.EndDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        endorsement.RevokedAt = now;
        endorsement.RevokedBy = user;
        endorsement.ReasonForChange = reason;
        endorsement.VersionIndex = baselineVersion + 1;

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_REVOKE",
            Details = $"Revoked delegation assignment {endorsement.Id} for staff {endorsement.StaffUserId} at site {endorsement.SiteId.Raw} in study {endorsement.StudyId.Raw}.",
            StudyId = endorsement.StudyId.Raw,
            SiteId = endorsement.SiteId.Raw,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Ok(ToResponseDto(endorsement));
    }

    public async Task<IResult> SignOffDelegationAsync(
        DoaSignOffRequest request,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
        {
            return TypedResults.Problem(
                title: "Record ID required",
                detail: "Delegation record ID is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (CtmsScopeGuard.RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return missingScope;
        }

        string? bodyStudy = request.Study?.TrimmedOrNull;
        if (!string.IsNullOrWhiteSpace(bodyStudy)
            && !string.IsNullOrWhiteSpace(studyId)
            && !string.Equals(bodyStudy, studyId.Trim(), StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Conflicting study ID",
                detail: "Study ID in request body conflicts with study_id query parameter.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? requestedStudyId = bodyStudy ?? studyId?.Trim();
        if (!string.IsNullOrWhiteSpace(requestedStudyId)
            && CtmsScopeGuard.RequireStudy(httpContext, requestedStudyId) is { } requestedStudyDenied)
        {
            return requestedStudyDenied;
        }

        string trimmedRecordId = request.RecordId.Trim();
        var endorsement = await db.CtmsDoaTaskEndorsements.FirstOrDefaultAsync(d => d.Id == trimmedRecordId, ct);
        if (endorsement is null)
        {
            return TypedResults.Problem(
                title: "Delegation record not found",
                detail: $"Delegation record '{request.RecordId}' does not exist.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, endorsement.StudyId.Raw, endorsement.SiteId.Raw) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!string.IsNullOrWhiteSpace(requestedStudyId)
            && !string.Equals(endorsement.StudyId.Raw, requestedStudyId, StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Delegation record not found",
                detail: $"Delegation record '{request.RecordId}' does not exist in study '{requestedStudyId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!MonitoringVisitEndpoints.TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        if (!endorsement.IsActive)
        {
            return TypedResults.Problem(
                title: "Cannot sign off revoked record",
                detail: $"Delegation record '{request.RecordId}' is revoked or inactive.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (endorsement.SignedOff)
        {
            return TypedResults.Problem(
                title: "Already signed off",
                detail: $"Delegation record '{request.RecordId}' has already been signed off.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        StandaloneConcurrencyTokenInterceptor.EnforceExpectedVersion(
            db,
            endorsement,
            request.ExpectedVersionIndex,
            httpContext);

        var (user, role) = CallerScope.ResolveActor(httpContext, "investigator");
        var (tenantId, tenantCode, actorRoles) = await ResolveCallerContextAsync(httpContext, role, ct);
        var now = clock.UtcNow;
        long baselineVersion = db.Entry(endorsement).Property(x => x.VersionIndex).OriginalValue;

        var taskCodes = ParseTaskCodes(endorsement.TaskCodes);
        DateTimeOffset validFrom = ParseDelegationStartDate(endorsement.StartDate, now);
        var kernelGrant = await _kernelTrust.IssueOrDelegateSiteGrantAsync(
            tenantId,
            tenantCode,
            endorsement.StudyId.Raw,
            endorsement.SiteId.Raw,
            endorsement.StaffUserId,
            taskCodes,
            validFrom,
            user,
            actorRoles,
            reason,
            $"ctms-doa-signoff:{endorsement.Id}",
            ct);

        endorsement.KernelGrantId = kernelGrant.GrantId;
        endorsement.SignedOff = true;
        endorsement.SignedOffAt = now;
        endorsement.SignedOffBy = user;
        endorsement.ReasonForChange = reason;
        endorsement.VersionIndex = baselineVersion + 1;

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_PI_SIGN_OFF",
            Details = $"Principal Investigator sign-off endorsed for delegation {endorsement.Id} (staff {endorsement.StaffUserId}, kernel grant {kernelGrant.GrantId:D}) at site {endorsement.SiteId.Raw} in study {endorsement.StudyId.Raw}.",
            StudyId = endorsement.StudyId.Raw,
            SiteId = endorsement.SiteId.Raw,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Ok(ToResponseDto(endorsement));
    }

    public static List<string> ParseTaskCodes(string taskCodesJson)
    {
        if (string.IsNullOrWhiteSpace(taskCodesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(taskCodesJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static DelegationResponseDto ToResponseDto(CtmsDoaTaskEndorsement d) =>
        new(
            RecordId: d.Id,
            Site: d.SiteId,
            StaffUserId: d.StaffUserId,
            TaskCodes: ParseTaskCodes(d.TaskCodes),
            StartDate: d.StartDate,
            EndDate: d.EndDate,
            IsActive: d.IsActive,
            SignedOff: d.SignedOff,
            ReasonForChange: d.ReasonForChange,
            CreatedBy: d.CreatedBy,
            VersionIndex: d.VersionIndex,
            KernelGrantId: d.KernelGrantId);

    internal static DateTimeOffset ParseDelegationStartDate(string startDate, DateTimeOffset fallback) =>
        DateTimeOffset.TryParse(
            startDate,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : fallback;

    internal static async Task<(TenantId TenantId, string? TenantCode, IReadOnlyList<string> ActorRoles)> ResolveCallerContextAsync(
        HttpContext httpContext,
        string fallbackRole,
        CancellationToken ct)
    {
        var principal = CtmsScopeGuard.ResolvePrincipal(httpContext);
        string? tenantCode = principal?.TenantId;
        IReadOnlyList<string> actorRoles;
        if (principal?.RawRoles is { Count: > 0 } rawRoles)
        {
            actorRoles = rawRoles;
        }
        else if (principal?.Roles is { Count: > 0 } normRoles)
        {
            actorRoles = normRoles;
        }
        else
        {
            actorRoles = [fallbackRole];
        }

        var accessor = httpContext.RequestServices?.GetService<ICurrentTenantAccessor>();
        if (accessor is not null)
        {
            Guid? resolved = await accessor.GetCurrentTenantIdAsync(ct).ConfigureAwait(false);
            if (resolved.HasValue && resolved.Value != Guid.Empty)
            {
                return (TenantId.FromGuid(resolved.Value), tenantCode, actorRoles);
            }
        }

        if (!string.IsNullOrWhiteSpace(tenantCode))
        {
            Guid deterministic = Guid.TryParse(tenantCode, out var parsed)
                ? parsed
                : DeterministicTenantCodeResolver.ComputeDeterministicGuid(tenantCode);
            return (TenantId.FromGuid(deterministic), tenantCode, actorRoles);
        }

        return (default, null, actorRoles);
    }

    private static Guid? TryParseGrantGuid(string raw)
    {
        if (raw.StartsWith("grant:", StringComparison.OrdinalIgnoreCase))
        {
            string suffix = raw["grant:".Length..];
            int at = suffix.IndexOf('@', StringComparison.Ordinal);
            if (at >= 0)
            {
                suffix = suffix[..at];
            }

            if (Guid.TryParse(suffix, out var fromRef))
            {
                return fromRef;
            }
        }

        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
