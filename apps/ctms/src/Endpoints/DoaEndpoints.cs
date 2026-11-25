using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Apps.Ctms.Services;
using Cadence.Compliance;
using Cadence.Database.Concurrency;
using Cadence.Database.Temporal;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Operational Minimal API endpoints for Delegation of Authority (DoA) site task assignment,
/// revocation, Principal Investigator sign-off, log retrieval, service-signed batch endorsement, and GxP PDF export.
/// </summary>
public static class DoaEndpoints
{
    public const string ReadPermission = "ctms_doa:read";
    public const string AssignPermission = "ctms_doa:assign";
    public const string RevokePermission = "ctms_doa:revoke";
    public const string SignOffPermission = "ctms_doa:sign_off";
    public const string ServiceEndorsePermission = "ctms_doa:service_endorse";
    public const string SignOffSemanticAction = "ctms.doa.sign_off";
    public const string SignOffAction = "/api/v1/ctms/doa/sign-off";

    public static RouteGroupBuilder MapDoaEndpoints(this RouteGroupBuilder group)
    {
        var doa = group.MapGroup("/doa");

        doa.MapGet("/delegations", ListDelegationsAsync)
            .WithName("ListCtmsDelegations")
            .WithSummary("List canonical study/site-scoped Delegation of Authority records.")
            .Produces<List<DelegationResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ReadPermission).RequireCallerStudyAndSiteScope();

        doa.MapPost("/delegate", DelegateTasksAsync)
            .WithName("DelegateSiteTasks")
            .WithSummary("Assign site trial task delegation requiring Principal Investigator sign-off.")
            .Produces<DelegationResponseDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(AssignPermission).RequireCallerStudyAndSiteScope();

        doa.MapPost("/revoke", RevokeDelegationAsync)
            .WithName("RevokeSiteTasks")
            .WithSummary("Revoke or end a delegated trial duty with reason for change.")
            .Produces<DelegationResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(RevokePermission).RequireCallerStudyAndSiteScope();

        doa.MapPost("/sign-off", SignOffDelegationAsync)
            .WithName("SignOffDelegation")
            .WithSummary("Endorse Delegation of Authority task assignment with Principal Investigator eSignature.")
            .Produces<DelegationResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SignOffPermission).RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(SignOffSemanticAction, http => http.Request.Path.Value ?? string.Empty);

        doa.MapPost("/endorse-signed-batch", EndorseSignedBatchAsync)
            .WithName("EndorseSignedDoaBatch")
            .WithSummary("Service-authenticated batch Principal Investigator endorsement and eTMF Zone 5 filing.")
            .Produces<BulkDoaEndorseResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ServiceEndorsePermission).RequireCallerStudyAndSiteScope();

        doa.MapGet("/sites/{site_id}/log", GetSiteDoaLogAsync)
            .WithName("GetSiteDoaLog")
            .WithSummary("Fetch active and historical DOA log matrix for a site.")
            .Produces<DoaLogResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadPermission).RequireSiteScope("site_id");

        doa.MapGet("/sites/{site_id}/export-pdf", ExportSiteDoaPdfAsync)
            .WithName("ExportSiteDoaPdf")
            .WithSummary("Export 21 CFR Part 11 signed DOA PDF log.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadPermission).RequireSiteScope("site_id")
            .RequireElectronicSignature("ctms.doa.export_pdf", http => http.Request.Path.Value ?? string.Empty);

        return group;
    }

    public static async Task<IResult> ListDelegationsAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        CtmsDbContext db,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "status")] string? status = null,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt = null,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt = null)
    {
        if (CtmsScopeGuard.ResolveStudyAndSiteFilters(httpContext, studyId, siteId, out var studyFilter, out var siteFilter) is { } scopeDenied)
        {
            return scopeDenied;
        }

        List<DelegationResponseDto> items;
        if (validAt.HasValue || knownAt.HasValue)
        {
            DateTimeOffset effectiveValidAt = validAt ?? knownAt!.Value;
            DateTimeOffset effectiveKnownAt = knownAt ?? validAt!.Value;
            IQueryable<CtmsDelegationPeriod> periodQuery = db.CtmsDelegationPeriods.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(studyId))
            {
                string trimmedStudy = studyId.Trim();
                periodQuery = periodQuery.Where(p => p.StudyCode == trimmedStudy);
            }

            if (!string.IsNullOrWhiteSpace(siteId))
            {
                string trimmedSite = siteId.Trim();
                periodQuery = periodQuery.Where(p => p.SiteCode == trimmedSite);
            }

            var periods = await periodQuery.QueryAsOfAsync(db, effectiveValidAt, effectiveKnownAt, ct);
            var scopedPeriods = CtmsScopeGuard.ApplyFilters(periods, studyFilter, p => p.StudyCode, siteFilter, p => p.SiteCode);
            items = [.. scopedPeriods
                .OrderBy(p => p.SiteCode)
                .ThenBy(p => p.StaffName ?? p.StaffUserId)
                .Select(p =>
                {
                    var tasks = ParseTaskCodes(p.TaskCodes);
                    return new DelegationResponseDto(
                        RecordId: p.DelegationId,
                        Site: p.SiteCode,
                        StaffUserId: p.StaffUserId,
                        TaskCodes: tasks,
                        StartDate: p.StartDate,
                        EndDate: p.EndDate,
                        IsActive: p.IsActive,
                        SignedOff: p.SignedOff,
                        ReasonForChange: p.ReasonForChange,
                        CreatedBy: p.CreatedBy,
                        VersionIndex: 1L,
                        Study: p.StudyCode,
                        StaffName: string.IsNullOrWhiteSpace(p.StaffName) ? p.StaffUserId : p.StaffName,
                        Role: p.StaffRole,
                        Duties: string.IsNullOrWhiteSpace(p.Duties) ? string.Join(", ", tasks) : p.Duties,
                        Status: ResolveDelegationStatus(p.IsActive, p.SignedOff),
                        PiSignatureHash: p.PiSignatureHash,
                        PiApprovedAt: p.PiApprovedAt,
                        BatchTransactionId: p.BatchTransactionId,
                        EtmfArtifactId: p.EtmfArtifactDocId);
                })];
        }
        else
        {
            IQueryable<CtmsDelegation> query = db.CtmsDelegations.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(studyId))
            {
                string trimmedStudy = studyId.Trim();
                query = query.Where(d => d.StudyId == trimmedStudy);
            }

            if (!string.IsNullOrWhiteSpace(siteId))
            {
                string trimmedSite = siteId.Trim();
                query = query.Where(d => d.SiteId == trimmedSite);
            }

            var rows = await query
                .OrderBy(d => d.SiteId)
                .ThenBy(d => d.StaffName ?? d.StaffUserId)
                .ToListAsync(ct);

            var scoped = CtmsScopeGuard.ApplyFilters(rows, studyFilter, d => d.StudyId, siteFilter, d => d.SiteId);
            items = [.. scoped.Select(ToResponseDto)];
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            string trimmedStatus = status.Trim();
            items = [.. items.Where(i => MatchesStatusFilter(i.Status, trimmedStatus))];
        }

        return TypedResults.Ok(items);
    }

    public static async Task<IResult> DelegateTasksAsync(
        [FromBody] DelegationTaskRequest request,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null)
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

        var scope = await CtmsScopeGuard.ResolveDoaSiteScopeAsync(httpContext, db, request.Site, requestedStudyId, ct);
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

        if (!TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        if (scope.AuthorizedStudies.Count != 1)
        {
            return TypedResults.Problem(
                title: "Study ID required",
                detail: $"Site '{request.Site.Trim()}' requires an explicit 'study_id' to record a delegation.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string resolvedStudyId = scope.AuthorizedStudies.First();
        string trimmedSite = request.Site.Trim();
        var now = clock.UtcNow;
        var (user, role) = CallerScope.ResolveActor(httpContext, "site_coordinator");
        string trimmedStaffId = request.StaffUserId.Trim();
        string resolvedStaffName = string.IsNullOrWhiteSpace(request.StaffName) ? trimmedStaffId : request.StaffName.Trim();
        string resolvedStaffRole = string.IsNullOrWhiteSpace(request.Role) ? "Site Staff" : request.Role.Trim();
        string resolvedDuties = string.IsNullOrWhiteSpace(request.Duties)
            ? string.Join(", ", request.TaskCodes)
            : request.Duties.Trim();

        var delegation = new CtmsDelegation
        {
            StudyId = resolvedStudyId,
            SiteId = trimmedSite,
            StaffUserId = trimmedStaffId,
            StaffName = resolvedStaffName,
            StaffRole = resolvedStaffRole,
            Duties = resolvedDuties,
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

        var auditEntry = new CtmsAuditLog
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
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/ctms/doa/sites/{delegation.SiteId}/log", ToResponseDto(delegation));
    }

    public static async Task<IResult> RevokeDelegationAsync(
        [FromBody] RevokeDelegationRequest request,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null)
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

        if (!TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
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

        await AppendDelegationPeriodTransitionAsync(db, delegation, user, reason, now, isRevocation: true, ct);

        var auditEntry = new CtmsAuditLog
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
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponseDto(delegation));
    }

    public static async Task<IResult> SignOffDelegationAsync(
        [FromBody] DoaSignOffRequest request,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null,
        [FromServices] ICtmsEtmfFilingClient? etmfFilingClient = null,
        [FromServices] IConfiguration? configuration = null)
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

        if (!TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
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

        string seal = ComputePiSignatureSeal(configuration, delegation.Id, delegation.StudyId, delegation.SiteId, user, now, reason);
        delegation.SignedOff = true;
        delegation.PiSignatureHash = seal;
        delegation.PiApprovedAt = now;
        delegation.ReasonForChange = reason;
        delegation.VersionIndex = baselineVersion + 1;

        var filingClient = etmfFilingClient ?? httpContext.RequestServices?.GetService<ICtmsEtmfFilingClient>();
        if (filingClient is not null)
        {
            var siteDelegations = await db.CtmsDelegations.AsNoTracking()
                .Where(d => d.StudyId == delegation.StudyId && d.SiteId == delegation.SiteId && d.Id != delegation.Id)
                .OrderBy(d => d.StartDate)
                .ToListAsync(ct);
            siteDelegations.Add(delegation);

            var (signedPdf, sha256, manifestationJson) = BuildSignedDoaPdf(
                delegation.StudyId,
                delegation.SiteId,
                user,
                role,
                now,
                reason,
                seal,
                siteDelegations,
                clock);

            string? etmfDocId = await filingClient.FileSignedDoaLogAsync(
                delegation.StudyId,
                delegation.SiteId,
                $"doa-signoff:{delegation.Id}:v{delegation.VersionIndex}",
                signedPdf,
                sha256,
                user,
                now,
                manifestationJson,
                reason,
                ct);
            if (!string.IsNullOrWhiteSpace(etmfDocId))
            {
                delegation.EtmfArtifactDocId = etmfDocId;
            }
        }

        await AppendDelegationPeriodTransitionAsync(db, delegation, user, reason, now, isRevocation: false, ct);

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_PI_SIGN_OFF",
            Details = $"Principal Investigator sign-off endorsed for delegation {delegation.Id} (staff {delegation.StaffUserId}) at site {delegation.SiteId} in study {delegation.StudyId}. Seal: {seal}. eTMF: {delegation.EtmfArtifactDocId ?? "N/A"}.",
            StudyId = delegation.StudyId,
            SiteId = delegation.SiteId,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponseDto(delegation));
    }

    public static async Task<IResult> EndorseSignedBatchAsync(
        [FromBody] BulkDoaEndorseRequest request,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        CancellationToken ct,
        [FromServices] ICtmsEtmfFilingClient? etmfFilingClient = null,
        [FromServices] IConfiguration? configuration = null)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.SignerUserId)
            || request.RecordIds is not { Count: > 0 })
        {
            return TypedResults.Problem(
                title: "Invalid batch endorsement request",
                detail: "signer_user_id and at least one delegation record ID are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (CtmsScopeGuard.RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return missingScope;
        }

        string? trimmedStudy = string.IsNullOrWhiteSpace(request.StudyCode) ? null : request.StudyCode.Trim();
        if (trimmedStudy is not null && CtmsScopeGuard.RequireStudy(httpContext, trimmedStudy) is { } studyDenied)
        {
            return studyDenied;
        }

        var distinctIds = request.RecordIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        IQueryable<CtmsDelegation> recordQuery = db.CtmsDelegations
            .Where(d => distinctIds.Contains(d.Id));
        if (trimmedStudy is not null)
        {
            recordQuery = recordQuery.Where(d => d.StudyId == trimmedStudy);
        }

        var records = await recordQuery.ToListAsync(ct);

        if (records.Count != distinctIds.Count)
        {
            var missing = distinctIds.Except(records.Select(r => r.Id), StringComparer.Ordinal).ToList();
            return TypedResults.Problem(
                title: "Delegation record not found",
                detail: $"No delegation record exists for: {string.Join(", ", missing)}. Nothing was signed.",
                statusCode: StatusCodes.Status404NotFound);
        }

        foreach (var rec in records)
        {
            if (CtmsScopeGuard.RequireStudyAndSite(httpContext, rec.StudyId, rec.SiteId) is { } siteDenied)
            {
                return siteDenied;
            }

            if (!rec.IsActive)
            {
                return TypedResults.Problem(
                    title: "Cannot sign off revoked record",
                    detail: $"Delegation record '{rec.Id}' is revoked or inactive.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        string reason = string.IsNullOrWhiteSpace(request.ReasonForChange)
            ? "Principal Investigator bulk e-signature sign-off"
            : request.ReasonForChange.Trim();
        DateTimeOffset now = request.ApprovedAt ?? clock.UtcNow;
        string signerUserId = request.SignerUserId.Trim();
        string batchTxId = string.IsNullOrWhiteSpace(request.BatchTransactionId)
            ? $"DOA-BATCH-{Guid.NewGuid():N}"
            : request.BatchTransactionId.Trim();
        string sealStudySummary = string.Join(",", records.Select(r => r.StudyId).Distinct(StringComparer.Ordinal));
        string seal = !string.IsNullOrWhiteSpace(request.SignatureHash)
            ? request.SignatureHash.Trim()
            : ComputePiSignatureSeal(configuration, batchTxId, sealStudySummary, string.Join(",", records.Select(r => r.SiteId).Distinct(StringComparer.Ordinal)), signerUserId, now, reason);

        var filingClient = etmfFilingClient ?? httpContext.RequestServices?.GetService<ICtmsEtmfFilingClient>();
        var etmfDocByStudySite = new Dictionary<(string Study, string Site), string?>();

        foreach (var rec in records)
        {
            long baselineVersion = db.Entry(rec).Property(x => x.VersionIndex).OriginalValue;
            rec.SignedOff = true;
            rec.PiSignatureHash = seal;
            rec.PiApprovedAt = now;
            rec.BatchTransactionId = batchTxId;
            rec.ReasonForChange = reason;
            rec.VersionIndex = baselineVersion + 1;
        }

        if (filingClient is not null)
        {
            foreach (var siteGroup in records.GroupBy(r => (r.StudyId, r.SiteId)))
            {
                var allSiteRecords = await db.CtmsDelegations.AsNoTracking()
                    .Where(d => d.StudyId == siteGroup.Key.StudyId && d.SiteId == siteGroup.Key.SiteId && !distinctIds.Contains(d.Id))
                    .OrderBy(d => d.StartDate)
                    .ToListAsync(ct);
                allSiteRecords.AddRange(siteGroup);

                var (signedPdf, sha256, manifestationJson) = BuildSignedDoaPdf(
                    siteGroup.Key.StudyId,
                    siteGroup.Key.SiteId,
                    signerUserId,
                    RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR,
                    now,
                    reason,
                    seal,
                    allSiteRecords,
                    clock);

                string? etmfDocId = await filingClient.FileSignedDoaLogAsync(
                    siteGroup.Key.StudyId,
                    siteGroup.Key.SiteId,
                    $"doa-batch:{batchTxId}:{siteGroup.Key.StudyId}:{siteGroup.Key.SiteId}",
                    signedPdf,
                    sha256,
                    signerUserId,
                    now,
                    manifestationJson,
                    reason,
                    ct);
                etmfDocByStudySite[siteGroup.Key] = etmfDocId;
            }
        }

        foreach (var rec in records)
        {
            if (etmfDocByStudySite.TryGetValue((rec.StudyId, rec.SiteId), out string? docId) && !string.IsNullOrWhiteSpace(docId))
            {
                rec.EtmfArtifactDocId = docId;
            }

            await AppendDelegationPeriodTransitionAsync(db, rec, signerUserId, reason, now, isRevocation: false, ct);

            db.CtmsAuditLogs.Add(new CtmsAuditLog
            {
                Timestamp = now,
                UserId = signerUserId,
                UserRole = RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR,
                Action = "DOA_PI_BULK_SIGN_OFF",
                Details = $"Principal Investigator bulk sign-off ({batchTxId}) endorsed for delegation {rec.Id} (staff {rec.StaffUserId}) at site {rec.SiteId} in study {rec.StudyId}. Seal: {seal}. eTMF: {rec.EtmfArtifactDocId ?? "N/A"}.",
                StudyId = rec.StudyId,
                SiteId = rec.SiteId,
                ReasonForChange = reason,
                CreatedAt = now.UtcDateTime,
                CreatedBy = signerUserId,
                VersionIndex = 1,
            });
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new BulkDoaEndorseResponseDto(
            BatchTransactionId: batchTxId,
            SignatureHash: seal,
            ApprovedAt: now,
            ApprovedCount: records.Count,
            Records: [.. records.Select(ToResponseDto)]));
    }

    public static async Task<IResult> GetSiteDoaLogAsync(
        [FromRoute(Name = "site_id")] string siteId,
        CtmsDbContext db,
        HttpContext httpContext,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt = null,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt = null)
    {
        var scope = await CtmsScopeGuard.ResolveDoaSiteScopeAsync(httpContext, db, siteId, studyId, ct);
        if (scope.Error is not null)
        {
            return scope.Error;
        }

        string trimmedSite = siteId.Trim();
        var authorizedStudies = scope.AuthorizedStudies;

        List<DelegationResponseDto> delegatedStaff;
        if (authorizedStudies.Count == 0)
        {
            delegatedStaff = [];
        }
        else if (validAt.HasValue || knownAt.HasValue)
        {
            DateTimeOffset effectiveValidAt = validAt ?? knownAt!.Value;
            DateTimeOffset effectiveKnownAt = knownAt ?? validAt!.Value;
            var periods = (await db.CtmsDelegationPeriods.AsNoTracking()
                .Where(p => p.SiteCode == trimmedSite && authorizedStudies.Contains(p.StudyCode))
                .QueryAsOfAsync(db, effectiveValidAt, effectiveKnownAt, ct))
                .OrderBy(p => p.StartDate, StringComparer.Ordinal)
                .ToList();

            var versionById = await db.CtmsDelegations.AsNoTracking()
                .Where(d => d.SiteId == trimmedSite && authorizedStudies.Contains(d.StudyId))
                .ToDictionaryAsync(d => d.Id, d => d.VersionIndex, StringComparer.Ordinal, ct);

            delegatedStaff = [.. periods
                .Select(p =>
                {
                    var tasks = ParseTaskCodes(p.TaskCodes);
                    return new DelegationResponseDto(
                        RecordId: p.DelegationId,
                        Site: p.SiteCode,
                        StaffUserId: p.StaffUserId,
                        TaskCodes: tasks,
                        StartDate: p.StartDate,
                        EndDate: p.EndDate,
                        IsActive: p.IsActive,
                        SignedOff: p.SignedOff,
                        ReasonForChange: p.ReasonForChange,
                        CreatedBy: p.CreatedBy,
                        VersionIndex: versionById.GetValueOrDefault(p.DelegationId, 1L),
                        Study: p.StudyCode,
                        StaffName: string.IsNullOrWhiteSpace(p.StaffName) ? p.StaffUserId : p.StaffName,
                        Role: p.StaffRole,
                        Duties: string.IsNullOrWhiteSpace(p.Duties) ? string.Join(", ", tasks) : p.Duties,
                        Status: ResolveDelegationStatus(p.IsActive, p.SignedOff),
                        PiSignatureHash: p.PiSignatureHash,
                        PiApprovedAt: p.PiApprovedAt,
                        BatchTransactionId: p.BatchTransactionId,
                        EtmfArtifactId: p.EtmfArtifactDocId);
                })];
        }
        else
        {
            var delegations = await db.CtmsDelegations.AsNoTracking()
                .Where(d => d.SiteId == trimmedSite && authorizedStudies.Contains(d.StudyId))
                .OrderBy(d => d.StartDate)
                .ToListAsync(ct);
            delegatedStaff = [.. delegations.Select(ToResponseDto)];
        }

        var audits = authorizedStudies.Count == 0
            ? []
            : await db.CtmsAuditLogs.AsNoTracking()
                .Where(a => a.SiteId == trimmedSite
                    && a.StudyId != null
                    && authorizedStudies.Contains(a.StudyId)
                    && (a.Action.StartsWith("DOA_") || a.Action.Contains("DELEGAT")))
                .Where(a => !knownAt.HasValue || a.Timestamp <= knownAt.Value)
                .OrderBy(a => a.Timestamp)
                .ToListAsync(ct);

        var gate = authorizedStudies.Count == 0
            ? null
            : await db.CtmsSiteGreenlightGates.AsNoTracking()
                .Where(g => g.SiteId == trimmedSite && authorizedStudies.Contains(g.StudyId))
                .OrderBy(g => g.StudyId)
                .FirstOrDefaultAsync(ct);

        string piName = !string.IsNullOrWhiteSpace(gate?.GreenlightCertifiedBy)
            ? gate.GreenlightCertifiedBy
            : "Principal Investigator";

        var response = new DoaLogResponseDto(
            Site: trimmedSite,
            PiName: piName,
            DelegatedStaff: delegatedStaff,
            AuditHistory: [.. audits.Select(ToAuditDto)]);

        return TypedResults.Ok(response);
    }

    public static async Task<IResult> ExportSiteDoaPdfAsync(
        [FromRoute(Name = "site_id")] string siteId,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        CancellationToken ct,
        [FromQuery(Name = "study_id")] string? studyId = null)
    {
        var scope = await CtmsScopeGuard.ResolveDoaSiteScopeAsync(httpContext, db, siteId, studyId, ct);
        if (scope.Error is not null)
        {
            return scope.Error;
        }

        string trimmedSite = siteId.Trim();
        var authorizedStudies = scope.AuthorizedStudies;

        var delegations = authorizedStudies.Count == 0
            ? []
            : await db.CtmsDelegations.AsNoTracking()
                .Where(d => d.SiteId == trimmedSite && authorizedStudies.Contains(d.StudyId))
                .OrderBy(d => d.StartDate)
                .ToListAsync(ct);

        var audits = authorizedStudies.Count == 0
            ? []
            : await db.CtmsAuditLogs.AsNoTracking()
                .Where(a => a.SiteId == trimmedSite
                    && a.StudyId != null
                    && authorizedStudies.Contains(a.StudyId)
                    && (a.Action.StartsWith("DOA_") || a.Action.Contains("DELEGAT")))
                .OrderBy(a => a.Timestamp)
                .ToListAsync(ct);

        var gate = authorizedStudies.Count == 0
            ? null
            : await db.CtmsSiteGreenlightGates.AsNoTracking()
                .Where(g => g.SiteId == trimmedSite && authorizedStudies.Contains(g.StudyId))
                .OrderBy(g => g.StudyId)
                .FirstOrDefaultAsync(ct);

        string piName = !string.IsNullOrWhiteSpace(gate?.GreenlightCertifiedBy)
            ? gate.GreenlightCertifiedBy
            : "Principal Investigator";

        var (user, role) = CallerScope.ResolveActor(httpContext, "investigator");
        DateTimeOffset now = clock.UtcNow;

        var pdfBuilder = new StringBuilder();
        pdfBuilder.AppendLine("%PDF-1.7");
        pdfBuilder.AppendLine("% Cadence Clinical GxP PDF Document - Delegation of Authority Log");
        pdfBuilder.AppendLine($"% Site: {siteId} | Principal Investigator: {piName}");
        pdfBuilder.AppendLine($"% Generated: {now:O} | Exported By: {user} ({role})");
        pdfBuilder.AppendLine();
        pdfBuilder.AppendLine("================================================================================");
        pdfBuilder.AppendLine("               DELEGATION OF AUTHORITY (DOA) COMPLIANCE LOG                     ");
        pdfBuilder.AppendLine("================================================================================");
        pdfBuilder.AppendLine($"Site ID: {siteId}");
        pdfBuilder.AppendLine($"Principal Investigator: {piName}");
        pdfBuilder.AppendLine($"Export Timestamp: {now:u}");
        pdfBuilder.AppendLine($"Total Delegations: {delegations.Count}");
        pdfBuilder.AppendLine("--------------------------------------------------------------------------------");
        pdfBuilder.AppendLine("DELEGATED SITE PERSONNEL:");
        foreach (var d in delegations)
        {
            var taskList = ParseTaskCodes(d.TaskCodes);
            pdfBuilder.AppendLine($"- Staff User ID : {d.StaffUserId}");
            pdfBuilder.AppendLine($"  Staff Name    : {d.StaffName ?? d.StaffUserId}");
            pdfBuilder.AppendLine($"  Record ID     : {d.Id}");
            pdfBuilder.AppendLine($"  Start Date    : {d.StartDate}");
            pdfBuilder.AppendLine($"  End Date      : {d.EndDate ?? "N/A (Active)"}");
            pdfBuilder.AppendLine($"  Active Status : {ResolveDelegationStatus(d.IsActive, d.SignedOff)}");
            pdfBuilder.AppendLine($"  PI Signed Off : {(d.SignedOff ? "YES" : "PENDING")}");
            pdfBuilder.AppendLine($"  Task Codes    : {string.Join(", ", taskList)}");
            pdfBuilder.AppendLine($"  Justification : {d.ReasonForChange}");
            pdfBuilder.AppendLine();
        }

        pdfBuilder.AppendLine("--------------------------------------------------------------------------------");
        pdfBuilder.AppendLine("IMMUTABLE AUDIT TRAIL HISTORY:");
        foreach (var a in audits)
        {
            pdfBuilder.AppendLine($"[{a.Timestamp:O}] [{a.Action}] User: {a.UserId} ({a.UserRole}) - {a.Details} (Reason: {a.ReasonForChange})");
        }
        pdfBuilder.AppendLine("================================================================================");
        pdfBuilder.AppendLine("%%EOF");

        byte[] rawPdfBytes = Encoding.UTF8.GetBytes(pdfBuilder.ToString().TrimEnd());

        var (cert, key) = Pkcs7Signer.GenerateSigningCredentials(user, clock);
        var signer = new Pkcs7Signer(cert, key);
        byte[] signedPdfBytes = signer.SignPdf(rawPdfBytes);

        string hash = Convert.ToHexStringLower(SHA256.HashData(signedPdfBytes));

        string? resolvedExportStudyId = null;
        if (authorizedStudies.Count == 1)
        {
            resolvedExportStudyId = authorizedStudies.First();
        }
        else if (!string.IsNullOrWhiteSpace(studyId))
        {
            resolvedExportStudyId = studyId.Trim();
        }

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_EXPORT_PDF",
            Details = $"Exported GxP Delegation of Authority PDF for site {trimmedSite} with {delegations.Count} records. SHA-256: {hash}",
            StudyId = resolvedExportStudyId,
            SiteId = trimmedSite,
            ReasonForChange = "Export GxP Delegation of Authority PDF",
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
        };
        db.CtmsAuditLogs.Add(auditEntry);
        await db.SaveChangesAsync(ct);

        return Results.File(signedPdfBytes, "application/pdf", $"DOA_Log_{trimmedSite}.pdf");
    }

    public static async Task<bool> SiteExistsAsync(CtmsDbContext db, string siteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(siteId))
        {
            return false;
        }

        string trimmedSite = siteId.Trim();
        return await db.CtmsSiteGreenlightGates.AnyAsync(g => g.SiteId == trimmedSite, ct)
            || await db.CtmsDelegations.AnyAsync(d => d.SiteId == trimmedSite, ct)
            || await db.CtmsSiteMilestones.AnyAsync(m => m.SiteId == trimmedSite, ct)
            || await db.CtmsMonitoringVisits.AnyAsync(v => v.SiteId == trimmedSite, ct)
            || await db.CtmsCraAllocations.AnyAsync(a => a.SiteId == trimmedSite, ct)
            || await db.CtmsClinicalQueries.AnyAsync(q => q.SiteId == trimmedSite, ct)
            || await db.CtmsInvestigatorGrants.AnyAsync(g => g.SiteId == trimmedSite, ct)
            || await db.CtmsRecruitmentRecords.AnyAsync(r => r.SiteId == trimmedSite, ct)
            || await db.CtmsRegulatoryForms.AnyAsync(f => f.SiteId == trimmedSite, ct)
            || await db.CtmsGeneratedLetters.AnyAsync(l => l.SiteId == trimmedSite, ct)
            || await db.DoaDelegationRecords.AnyAsync(r => r.SiteId == trimmedSite, ct);
    }

    public static bool TryExtractReasonForChange(
        HttpContext httpContext,
        string? bodyReason,
        out string reason,
        out IResult? problem) =>
        MonitoringVisitEndpoints.TryExtractReasonForChange(httpContext, bodyReason, out reason, out problem);

    private static string ResolveDelegationStatus(bool isActive, bool signedOff)
    {
        if (!isActive)
        {
            return "REVOKED";
        }

        return signedOff ? "ACTIVE" : "PENDING_PI_SIGNATURE";
    }

    private static bool MatchesStatusFilter(string actualStatus, string filter) =>
        string.Equals(actualStatus, filter, StringComparison.OrdinalIgnoreCase)
        || (string.Equals(filter, "PENDING", StringComparison.OrdinalIgnoreCase) && string.Equals(actualStatus, "PENDING_PI_SIGNATURE", StringComparison.OrdinalIgnoreCase))
        || (string.Equals(filter, "COMPLETED", StringComparison.OrdinalIgnoreCase) && string.Equals(actualStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase));

    private static string ComputePiSignatureSeal(
        IConfiguration? config,
        string recordOrBatchId,
        string studyCode,
        string siteCode,
        string signerUserId,
        DateTimeOffset approvedAt,
        string reason)
    {
        string keyText = config?["GATEWAY_SECRET"] is { Length: > 0 } secret
            ? secret
            : "cadence-ctms-doa-seal";
        byte[] keyBytes = Encoding.UTF8.GetBytes(keyText);
        byte[] payload = Encoding.UTF8.GetBytes(
            $"ctms.doa.pi_seal|{studyCode}|{siteCode}|{recordOrBatchId}|{signerUserId}|{approvedAt:O}|{reason}");
        return "SIG-PI-" + Convert.ToHexStringLower(HMACSHA256.HashData(keyBytes, payload))[..24];
    }

    private static (byte[] PdfBytes, string Sha256Hash, string SignatureManifestationJson) BuildSignedDoaPdf(
        string studyCode,
        string siteCode,
        string signerUserId,
        string signerRole,
        DateTimeOffset signedAt,
        string reason,
        string seal,
        IReadOnlyList<CtmsDelegation> delegations,
        IClock clock)
    {
        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.7");
        sb.AppendLine("% Cadence Clinical GxP PDF Document - Delegation of Authority Log (05.02.04)");
        sb.AppendLine($"% Study: {studyCode} | Site: {siteCode} | PI Signer: {signerUserId} ({signerRole})");
        sb.AppendLine($"% Signed At: {signedAt:O} | Seal: {seal}");
        sb.AppendLine();
        foreach (var d in delegations)
        {
            sb.AppendLine($"[{d.Id}] {d.StaffName ?? d.StaffUserId} ({d.StaffRole ?? "Site Staff"}) | Tasks: {d.TaskCodes} | Status: {ResolveDelegationStatus(d.IsActive, d.SignedOff)}");
        }
        sb.AppendLine("%%EOF");

        byte[] rawPdfBytes = Encoding.UTF8.GetBytes(sb.ToString().TrimEnd());
        var (cert, key) = Pkcs7Signer.GenerateSigningCredentials(signerUserId, clock);
        var pdfSigner = new Pkcs7Signer(cert, key);
        byte[] signedPdfBytes = pdfSigner.SignPdf(rawPdfBytes);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(signedPdfBytes));
        string manifestationJson = JsonSerializer.Serialize(new
        {
            signer = signerUserId,
            signer_role = signerRole,
            timestamp = signedAt.ToString("O"),
            meaning = "Principal Investigator Delegation of Authority Endorsement",
            endorsement = reason,
            seal,
            certificate_subject = cert.Subject,
            signature_algorithm = "SHA256withRSA-PSS"
        });

        return (signedPdfBytes, sha256, manifestationJson);
    }
    private static List<string> ParseTaskCodes(string taskCodesJson)
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

    public static DelegationResponseDto ToResponseDto(CtmsDelegation d)
    {
        var tasks = ParseTaskCodes(d.TaskCodes);
        return new DelegationResponseDto(
            RecordId: d.Id,
            Site: d.SiteId,
            StaffUserId: d.StaffUserId,
            TaskCodes: tasks,
            StartDate: d.StartDate,
            EndDate: d.EndDate,
            IsActive: d.IsActive,
            SignedOff: d.SignedOff,
            ReasonForChange: d.ReasonForChange,
            CreatedBy: d.CreatedBy,
            VersionIndex: d.VersionIndex,
            Study: d.StudyId,
            StaffName: string.IsNullOrWhiteSpace(d.StaffName) ? d.StaffUserId : d.StaffName,
            Role: d.StaffRole,
            Duties: string.IsNullOrWhiteSpace(d.Duties) ? string.Join(", ", tasks) : d.Duties,
            Status: ResolveDelegationStatus(d.IsActive, d.SignedOff),
            PiSignatureHash: d.PiSignatureHash,
            PiApprovedAt: d.PiApprovedAt,
            BatchTransactionId: d.BatchTransactionId,
            EtmfArtifactId: d.EtmfArtifactDocId);
    }

    public static DoaAuditHistoryItemDto ToAuditDto(CtmsAuditLog a) =>
        new(
            Id: a.Id,
            Timestamp: a.Timestamp,
            UserId: a.UserId,
            UserRole: a.UserRole,
            Action: a.Action,
            Details: a.Details);

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
            StaffName = delegation.StaffName,
            StaffRole = delegation.StaffRole,
            Duties = delegation.Duties,
            TaskCodes = delegation.TaskCodes,
            StartDate = delegation.StartDate,
            EndDate = delegation.EndDate,
            IsActive = delegation.IsActive,
            SignedOff = delegation.SignedOff,
            PiSignatureHash = delegation.PiSignatureHash,
            PiApprovedAt = delegation.PiApprovedAt,
            BatchTransactionId = delegation.BatchTransactionId,
            EtmfArtifactDocId = delegation.EtmfArtifactDocId,
            CreatedBy = actorId,
            ReasonForChange = reasonForChange,
            ValidPeriod = validPeriod,
            RecordedPeriod = recordedPeriod,
        };

    private static async Task AppendDelegationPeriodTransitionAsync(
        CtmsDbContext db,
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
            var prior = openPeriods.FirstOrDefault();
            bool wasSignedOff = prior?.SignedOff ?? delegation.SignedOff;
            string priorReason = prior?.ReasonForChange ?? delegation.ReasonForChange;
            string priorActor = prior?.CreatedBy ?? delegation.CreatedBy;

            db.CtmsDelegationPeriods.Add(new CtmsDelegationPeriod
            {
                DelegationId = delegation.Id,
                StudyCode = delegation.StudyId,
                SiteCode = delegation.SiteId,
                StaffUserId = delegation.StaffUserId,
                StaffName = prior?.StaffName ?? delegation.StaffName,
                StaffRole = prior?.StaffRole ?? delegation.StaffRole,
                Duties = prior?.Duties ?? delegation.Duties,
                TaskCodes = delegation.TaskCodes,
                StartDate = delegation.StartDate,
                EndDate = null,
                IsActive = true,
                SignedOff = wasSignedOff,
                PiSignatureHash = prior?.PiSignatureHash ?? delegation.PiSignatureHash,
                PiApprovedAt = prior?.PiApprovedAt ?? delegation.PiApprovedAt,
                BatchTransactionId = prior?.BatchTransactionId ?? delegation.BatchTransactionId,
                EtmfArtifactDocId = prior?.EtmfArtifactDocId ?? delegation.EtmfArtifactDocId,
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

public record DelegationTaskRequest(
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("staff_user_id")] string StaffUserId,
    [property: JsonPropertyName("task_codes")] List<string> TaskCodes,
    [property: JsonPropertyName("start_date")] string StartDate,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange,
    [property: JsonPropertyName("study_id")] string? Study = null,
    [property: JsonPropertyName("staff_name")] string? StaffName = null,
    [property: JsonPropertyName("role")] string? Role = null,
    [property: JsonPropertyName("duties")] string? Duties = null
);

public record RevokeDelegationRequest(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange,
    [property: JsonPropertyName("study_id")] string? Study = null,
    [property: JsonPropertyName("expected_version_index")] long? ExpectedVersionIndex = null
);

public record DoaSignOffRequest(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange,
    [property: JsonPropertyName("study_id")] string? Study = null,
    [property: JsonPropertyName("expected_version_index")] long? ExpectedVersionIndex = null
);

public record BulkDoaEndorseRequest(
    [property: JsonPropertyName("study_code")] string? StudyCode,
    [property: JsonPropertyName("record_ids")] List<string> RecordIds,
    [property: JsonPropertyName("signer_user_id")] string SignerUserId,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange = null,
    [property: JsonPropertyName("batch_transaction_id")] string? BatchTransactionId = null,
    [property: JsonPropertyName("signature_hash")] string? SignatureHash = null,
    [property: JsonPropertyName("approved_at")] DateTimeOffset? ApprovedAt = null
);

public record BulkDoaEndorseResponseDto(
    [property: JsonPropertyName("batch_transaction_id")] string BatchTransactionId,
    [property: JsonPropertyName("signature_hash")] string SignatureHash,
    [property: JsonPropertyName("approved_at")] DateTimeOffset ApprovedAt,
    [property: JsonPropertyName("approved_count")] int ApprovedCount,
    [property: JsonPropertyName("records")] List<DelegationResponseDto> Records
);

public record DelegationResponseDto(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("staff_user_id")] string StaffUserId,
    [property: JsonPropertyName("task_codes")] List<string> TaskCodes,
    [property: JsonPropertyName("start_date")] string StartDate,
    [property: JsonPropertyName("end_date")] string? EndDate,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("signed_off")] bool SignedOff,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("version_index")] long VersionIndex,
    [property: JsonPropertyName("study_id")] string? Study = null,
    [property: JsonPropertyName("staff_name")] string? StaffName = null,
    [property: JsonPropertyName("role")] string? Role = null,
    [property: JsonPropertyName("duties")] string? Duties = null,
    [property: JsonPropertyName("status")] string Status = "PENDING_PI_SIGNATURE",
    [property: JsonPropertyName("pi_signature_hash")] string? PiSignatureHash = null,
    [property: JsonPropertyName("pi_approved_at")] DateTimeOffset? PiApprovedAt = null,
    [property: JsonPropertyName("batch_transaction_id")] string? BatchTransactionId = null,
    [property: JsonPropertyName("etmf_document_id")] string? EtmfArtifactId = null
);

public record DoaAuditHistoryItemDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_role")] string UserRole,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("details")] string Details
);

public record DoaLogResponseDto(
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("pi_name")] string PiName,
    [property: JsonPropertyName("delegated_staff")] List<DelegationResponseDto> DelegatedStaff,
    [property: JsonPropertyName("audit_history")] List<DoaAuditHistoryItemDto> AuditHistory
);
