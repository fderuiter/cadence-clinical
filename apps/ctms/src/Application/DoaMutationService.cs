using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Database.Concurrency;
using Cadence.Database.Temporal;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Application;

public sealed class DoaMutationService(
    CtmsDbContext db,
    CtmsUnitOfWork unitOfWork,
    CtmsSiteScopeService siteScopeService,
    IClock clock)
{
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

        if (!string.IsNullOrWhiteSpace(request.Study)
            && !string.IsNullOrWhiteSpace(studyId)
            && !string.Equals(request.Study.Trim(), studyId.Trim(), StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Conflicting study ID",
                detail: "Study ID in request body conflicts with study_id query parameter.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? requestedStudyId = !string.IsNullOrWhiteSpace(request.Study) ? request.Study.Trim() : studyId?.Trim();

        if (string.IsNullOrWhiteSpace(request.Site))
        {
            return TypedResults.Problem(
                title: "Site ID required",
                detail: "Site ID is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var scope = await siteScopeService.ResolveDoaSiteScopeAsync(httpContext, request.Site, requestedStudyId, ct);
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

        if (!CtmsScopeGuard.TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        if (scope.AuthorizedStudies.Count != 1)
        {
            return TypedResults.Problem(
                title: "Study ID required",
                detail: $"Site '{request.Site.Trim()}' belongs to multiple authorized studies; specify 'study_id'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string resolvedStudyId = scope.AuthorizedStudies.First();
        string trimmedSite = request.Site.Trim();
        var now = clock.UtcNow;
        var (user, role) = CallerScope.ResolveActor(httpContext, "site_coordinator");

        var delegation = new CtmsDelegation
        {
            StudyId = resolvedStudyId,
            SiteId = trimmedSite,
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

        db.CtmsDelegations.Add(delegation);
        DateTimeOffset validFrom = ParseDelegationStartDate(delegation.StartDate, now);
        db.CtmsDelegationPeriods.Add(CreateDelegationPeriod(
            delegation,
            user,
            reason,
            StandaloneBitemporalModelExtensions.CreateOpenPeriod(validFrom),
            StandaloneBitemporalModelExtensions.CreateOpenPeriod(now)));

        db.CtmsAuditLogs.Add(new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_DELEGATE",
            Details = $"Delegated tasks [{string.Join(", ", request.TaskCodes)}] to staff {delegation.StaffUserId} at site {delegation.SiteId} in study {delegation.StudyId}.",
            StudyId = delegation.StudyId,
            SiteId = delegation.SiteId,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
        });

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Created($"/api/v1/ctms/doa/sites/{delegation.SiteId}/log", ToResponseDto(delegation));
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

        if (!string.IsNullOrWhiteSpace(request.Study)
            && !string.IsNullOrWhiteSpace(studyId)
            && !string.Equals(request.Study.Trim(), studyId.Trim(), StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Conflicting study ID",
                detail: "Study ID in request body conflicts with study_id query parameter.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? requestedStudyId = !string.IsNullOrWhiteSpace(request.Study) ? request.Study.Trim() : studyId?.Trim();
        if (!string.IsNullOrWhiteSpace(requestedStudyId)
            && CtmsScopeGuard.RequireStudy(httpContext, requestedStudyId) is { } requestedStudyDenied)
        {
            return requestedStudyDenied;
        }

        string trimmedRecordId = request.RecordId.Trim();
        var delegation = await db.CtmsDelegations.FirstOrDefaultAsync(d => d.Id == trimmedRecordId, ct);
        if (delegation is null)
        {
            return TypedResults.Problem(
                title: "Delegation record not found",
                detail: $"Delegation record '{request.RecordId}' does not exist.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, delegation.StudyId, delegation.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!string.IsNullOrWhiteSpace(requestedStudyId)
            && !string.Equals(delegation.StudyId, requestedStudyId, StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Delegation record not found",
                detail: $"Delegation record '{request.RecordId}' does not exist in study '{requestedStudyId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!CtmsScopeGuard.TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        if (!delegation.IsActive)
        {
            return TypedResults.Problem(
                title: "Already revoked",
                detail: $"Delegation record '{request.RecordId}' is already inactive or revoked.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        StandaloneConcurrencyTokenInterceptor.EnforceExpectedVersion(
            db,
            delegation,
            request.ExpectedVersionIndex,
            httpContext);

        var now = clock.UtcNow;
        var (user, role) = CallerScope.ResolveActor(httpContext, "investigator");
        long baselineVersion = db.Entry(delegation).Property(x => x.VersionIndex).OriginalValue;

        delegation.IsActive = false;
        delegation.EndDate = now.ToString("yyyy-MM-dd");
        delegation.ReasonForChange = reason;
        delegation.VersionIndex = baselineVersion + 1;

        await AppendDelegationPeriodTransitionAsync(delegation, user, reason, now, isRevocation: true, ct);

        db.CtmsAuditLogs.Add(new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_REVOKE",
            Details = $"Revoked delegation assignment {delegation.Id} for staff {delegation.StaffUserId} at site {delegation.SiteId} in study {delegation.StudyId}.",
            StudyId = delegation.StudyId,
            SiteId = delegation.SiteId,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
        });

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Ok(ToResponseDto(delegation));
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

        if (!string.IsNullOrWhiteSpace(request.Study)
            && !string.IsNullOrWhiteSpace(studyId)
            && !string.Equals(request.Study.Trim(), studyId.Trim(), StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Conflicting study ID",
                detail: "Study ID in request body conflicts with study_id query parameter.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? requestedStudyId = !string.IsNullOrWhiteSpace(request.Study) ? request.Study.Trim() : studyId?.Trim();
        if (!string.IsNullOrWhiteSpace(requestedStudyId)
            && CtmsScopeGuard.RequireStudy(httpContext, requestedStudyId) is { } requestedStudyDenied)
        {
            return requestedStudyDenied;
        }

        string trimmedRecordId = request.RecordId.Trim();
        var delegation = await db.CtmsDelegations.FirstOrDefaultAsync(d => d.Id == trimmedRecordId, ct);
        if (delegation is null)
        {
            return TypedResults.Problem(
                title: "Delegation record not found",
                detail: $"Delegation record '{request.RecordId}' does not exist.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, delegation.StudyId, delegation.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!string.IsNullOrWhiteSpace(requestedStudyId)
            && !string.Equals(delegation.StudyId, requestedStudyId, StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                title: "Delegation record not found",
                detail: $"Delegation record '{request.RecordId}' does not exist in study '{requestedStudyId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!CtmsScopeGuard.TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        if (!delegation.IsActive)
        {
            return TypedResults.Problem(
                title: "Cannot sign off revoked record",
                detail: $"Delegation record '{request.RecordId}' is revoked or inactive.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (delegation.SignedOff)
        {
            return TypedResults.Problem(
                title: "Already signed off",
                detail: $"Delegation record '{request.RecordId}' has already been signed off.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        StandaloneConcurrencyTokenInterceptor.EnforceExpectedVersion(
            db,
            delegation,
            request.ExpectedVersionIndex,
            httpContext);

        var (user, role) = CallerScope.ResolveActor(httpContext, "investigator");
        var now = clock.UtcNow;
        long baselineVersion = db.Entry(delegation).Property(x => x.VersionIndex).OriginalValue;

        delegation.SignedOff = true;
        delegation.ReasonForChange = reason;
        delegation.VersionIndex = baselineVersion + 1;

        await AppendDelegationPeriodTransitionAsync(delegation, user, reason, now, isRevocation: false, ct);

        db.CtmsAuditLogs.Add(new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_PI_SIGN_OFF",
            Details = $"Principal Investigator sign-off endorsed for delegation {delegation.Id} (staff {delegation.StaffUserId}) at site {delegation.SiteId} in study {delegation.StudyId}.",
            StudyId = delegation.StudyId,
            SiteId = delegation.SiteId,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
        });

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Ok(ToResponseDto(delegation));
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

    public static DelegationResponseDto ToResponseDto(CtmsDelegation d) =>
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
            VersionIndex: d.VersionIndex);

    private static DateTimeOffset ParseDelegationStartDate(string startDate, DateTimeOffset fallback) =>
        DateTimeOffset.TryParse(
            startDate,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : fallback;

    private static CtmsDelegationPeriod CreateDelegationPeriod(
        CtmsDelegation delegation,
        string actorId,
        string reasonForChange,
        NpgsqlTypes.NpgsqlRange<DateTimeOffset> validPeriod,
        NpgsqlTypes.NpgsqlRange<DateTimeOffset> recordedPeriod) =>
        new()
        {
            DelegationId = delegation.Id,
            StudyCode = delegation.StudyId,
            SiteCode = delegation.SiteId,
            StaffUserId = delegation.StaffUserId,
            TaskCodes = delegation.TaskCodes,
            StartDate = delegation.StartDate,
            EndDate = delegation.EndDate,
            IsActive = delegation.IsActive,
            SignedOff = delegation.SignedOff,
            CreatedBy = actorId,
            ReasonForChange = reasonForChange,
            ValidPeriod = validPeriod,
            RecordedPeriod = recordedPeriod,
        };

    private async Task AppendDelegationPeriodTransitionAsync(
        CtmsDelegation delegation,
        string actorId,
        string reasonForChange,
        DateTimeOffset now,
        bool isRevocation,
        CancellationToken ct)
    {
        var allPeriods = await db.CtmsDelegationPeriods
            .Where(p => p.DelegationId == delegation.Id)
            .ToListAsync(ct);
        var openPeriods = allPeriods
            .Where(p => p.RecordedPeriod.UpperBoundInfinite)
            .ToList();

        DateTimeOffset recordedCutover = now;
        foreach (var openPeriod in openPeriods)
        {
            var closed = StandaloneBitemporalModelExtensions.CloseRecordedAt(openPeriod.RecordedPeriod, now);
            openPeriod.RecordedPeriod = closed;
            if (closed.UpperBound > recordedCutover)
            {
                recordedCutover = closed.UpperBound;
            }
        }

        DateTimeOffset validFrom = ParseDelegationStartDate(delegation.StartDate, now);
        var openRecorded = StandaloneBitemporalModelExtensions.CreateOpenPeriod(recordedCutover);

        if (isRevocation && validFrom < recordedCutover)
        {
            bool wasSignedOff = openPeriods.FirstOrDefault()?.SignedOff ?? delegation.SignedOff;
            string priorReason = openPeriods.FirstOrDefault()?.ReasonForChange ?? delegation.ReasonForChange;
            string priorActor = openPeriods.FirstOrDefault()?.CreatedBy ?? delegation.CreatedBy;

            db.CtmsDelegationPeriods.Add(new CtmsDelegationPeriod
            {
                DelegationId = delegation.Id,
                StudyCode = delegation.StudyId,
                SiteCode = delegation.SiteId,
                StaffUserId = delegation.StaffUserId,
                TaskCodes = delegation.TaskCodes,
                StartDate = delegation.StartDate,
                EndDate = null,
                IsActive = true,
                SignedOff = wasSignedOff,
                CreatedBy = priorActor,
                ReasonForChange = priorReason,
                ValidPeriod = StandaloneBitemporalModelExtensions.CreateClosedPeriod(validFrom, recordedCutover),
                RecordedPeriod = openRecorded,
            });

            db.CtmsDelegationPeriods.Add(CreateDelegationPeriod(
                delegation,
                actorId,
                reasonForChange,
                StandaloneBitemporalModelExtensions.CreateOpenPeriod(recordedCutover),
                openRecorded));
            return;
        }

        DateTimeOffset nextValidFrom = isRevocation ? recordedCutover : validFrom;
        db.CtmsDelegationPeriods.Add(CreateDelegationPeriod(
            delegation,
            actorId,
            reasonForChange,
            StandaloneBitemporalModelExtensions.CreateOpenPeriod(nextValidFrom),
            openRecorded));
    }
}
