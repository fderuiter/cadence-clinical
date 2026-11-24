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

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Operational Minimal API endpoints for Delegation of Authority (DoA) site task assignment,
/// revocation, Principal Investigator sign-off, log retrieval, and GxP PDF export.
/// </summary>
public static class DoaEndpoints
{
    public const string ReadPermission = "ctms_doa:read";
    public const string AssignPermission = "ctms_doa:assign";
    public const string RevokePermission = "ctms_doa:revoke";
    public const string SignOffPermission = "ctms_doa:sign_off";
    public const string ExportPermission = "ctms_doa:export";
    public const string SignOffDelegationAction = "/api/v1/ctms/doa/sign-off";
    public const string SignOffDelegationSemanticAction = "ctms.doa.sign_off";
    public const string ExportSignedPdfSemanticAction = "ctms.doa.export_signed_pdf";

    public static string ExportSignedPdfAction(string siteId) =>
        $"/api/v1/ctms/doa/sites/{Uri.EscapeDataString(siteId)}/export-signed-pdf";

    private static string ExportSignedPdfActionFor(HttpContext httpContext) =>
        ExportSignedPdfAction(httpContext.Request.RouteValues["site_id"]?.ToString() ?? string.Empty);

    public static RouteGroupBuilder MapDoaEndpoints(this RouteGroupBuilder group)
    {
        var doa = group.MapGroup("/doa");

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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SignOffPermission).RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(SignOffDelegationSemanticAction, _ => SignOffDelegationAction);

        doa.MapGet("/sites/{site_id}/log", GetSiteDoaLogAsync)
            .WithName("GetSiteDoaLog")
            .WithSummary("Fetch active and historical DOA log matrix for a site.")
            .Produces<DoaLogResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadPermission).RequireSiteScope("site_id");

        doa.MapGet("/sites/{site_id}/export-pdf", ExportSiteDoaPdfAsync)
            .WithName("ExportSiteDoaPdf")
            .WithSummary("Export unsigned working DOA PDF log for review.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadPermission).RequireSiteScope("site_id");

        doa.MapPost("/sites/{site_id}/export-signed-pdf", ExportSignedSiteDoaPdfAsync)
            .WithName("ExportSignedSiteDoaPdf")
            .WithSummary("Export 21 CFR Part 11 signed DOA PDF log and record an export audit entry.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ExportPermission).RequireSiteScope("site_id")
            .RequireElectronicSignature(ExportSignedPdfSemanticAction, ExportSignedPdfActionFor);

        return group;
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
                detail: $"Site '{request.Site.Trim()}' belongs to multiple authorized studies; specify 'study_id'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string resolvedStudyId = scope.AuthorizedStudies.First();
        string trimmedSite = request.Site.Trim();
        var now = clock.UtcNow;
        var (user, role) = ResolveCaller(httpContext, "site_coordinator");

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
        var (user, role) = ResolveCaller(httpContext, "investigator");
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

        var (user, role) = ResolveCaller(httpContext, "investigator");
        var now = clock.UtcNow;
        long baselineVersion = db.Entry(delegation).Property(x => x.VersionIndex).OriginalValue;

        delegation.SignedOff = true;
        delegation.ReasonForChange = reason;
        delegation.VersionIndex = baselineVersion + 1;

        await AppendDelegationPeriodTransitionAsync(db, delegation, user, reason, now, isRevocation: false, ct);

        var auditEntry = new CtmsAuditLog
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
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponseDto(delegation));
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
        if (validAt.HasValue || knownAt.HasValue)
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

            delegatedStaff = periods
                .Select(p => new DelegationResponseDto(
                    RecordId: p.DelegationId,
                    Site: p.SiteCode,
                    StaffUserId: p.StaffUserId,
                    TaskCodes: ParseTaskCodes(p.TaskCodes),
                    StartDate: p.StartDate,
                    EndDate: p.EndDate,
                    IsActive: p.IsActive,
                    SignedOff: p.SignedOff,
                    ReasonForChange: p.ReasonForChange,
                    CreatedBy: p.CreatedBy,
                    VersionIndex: versionById.GetValueOrDefault(p.DelegationId, 1L)))
                .ToList();
        }
        else
        {
            var delegations = await db.CtmsDelegations.AsNoTracking()
                .Where(d => d.SiteId == trimmedSite && authorizedStudies.Contains(d.StudyId))
                .OrderBy(d => d.StartDate)
                .ToListAsync(ct);
            delegatedStaff = delegations.Select(ToResponseDto).ToList();
        }

        var audits = await db.CtmsAuditLogs.AsNoTracking()
            .Where(a => a.SiteId == trimmedSite
                && a.StudyId != null
                && authorizedStudies.Contains(a.StudyId)
                && (a.Action.StartsWith("DOA_") || a.Action.Contains("DELEGAT")))
            .Where(a => !knownAt.HasValue || a.Timestamp <= knownAt.Value)
            .OrderBy(a => a.Timestamp)
            .ToListAsync(ct);

        var gate = await db.CtmsSiteGreenlightGates.AsNoTracking()
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
            AuditHistory: audits.Select(ToAuditDto).ToList());

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
        var (user, role) = ResolveCaller(httpContext, "investigator");
        DateTimeOffset now = clock.UtcNow;

        var (_, rawPdfBytes) = await BuildSiteDoaRawPdfAsync(
            db,
            siteId,
            trimmedSite,
            scope.AuthorizedStudies,
            user,
            role,
            now,
            isSignedExport: false,
            ct);

        return Results.File(rawPdfBytes, "application/pdf", $"DOA_Log_{trimmedSite}_working_copy.pdf");
    }

    public static async Task<IResult> ExportSignedSiteDoaPdfAsync(
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

        if (!TryExtractReasonForChange(httpContext, null, out string reason, out var problem))
        {
            return problem!;
        }

        string trimmedSite = siteId.Trim();
        var authorizedStudies = scope.AuthorizedStudies;
        var (user, role) = ResolveCaller(httpContext, "investigator");
        DateTimeOffset now = clock.UtcNow;

        var (delegationCount, rawPdfBytes) = await BuildSiteDoaRawPdfAsync(
            db,
            siteId,
            trimmedSite,
            authorizedStudies,
            user,
            role,
            now,
            isSignedExport: true,
            ct);

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
            Details = $"Exported GxP Delegation of Authority PDF for site {trimmedSite} with {delegationCount} records. SHA-256: {hash}",
            StudyId = resolvedExportStudyId,
            SiteId = trimmedSite,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
        };
        db.CtmsAuditLogs.Add(auditEntry);
        await db.SaveChangesAsync(ct);

        return Results.File(signedPdfBytes, "application/pdf", $"DOA_Log_{trimmedSite}.pdf");
    }

    private static async Task<(int DelegationCount, byte[] RawPdfBytes)> BuildSiteDoaRawPdfAsync(
        CtmsDbContext db,
        string rawSiteId,
        string trimmedSite,
        IReadOnlyCollection<string> authorizedStudies,
        string user,
        string role,
        DateTimeOffset now,
        bool isSignedExport,
        CancellationToken ct)
    {
        var delegations = await db.CtmsDelegations.AsNoTracking()
            .Where(d => d.SiteId == trimmedSite && authorizedStudies.Contains(d.StudyId))
            .OrderBy(d => d.StartDate)
            .ToListAsync(ct);

        var audits = await db.CtmsAuditLogs.AsNoTracking()
            .Where(a => a.SiteId == trimmedSite
                && a.StudyId != null
                && authorizedStudies.Contains(a.StudyId)
                && (a.Action.StartsWith("DOA_") || a.Action.Contains("DELEGAT")))
            .OrderBy(a => a.Timestamp)
            .ToListAsync(ct);

        var gate = await db.CtmsSiteGreenlightGates.AsNoTracking()
            .Where(g => g.SiteId == trimmedSite && authorizedStudies.Contains(g.StudyId))
            .OrderBy(g => g.StudyId)
            .FirstOrDefaultAsync(ct);

        string piName = !string.IsNullOrWhiteSpace(gate?.GreenlightCertifiedBy)
            ? gate.GreenlightCertifiedBy
            : "Principal Investigator";

        var pdfBuilder = new StringBuilder();
        pdfBuilder.AppendLine("%PDF-1.7");
        pdfBuilder.AppendLine("% Cadence Clinical GxP PDF Document - Delegation of Authority Log");
        pdfBuilder.AppendLine($"% Site: {rawSiteId} | Principal Investigator: {piName}");
        pdfBuilder.AppendLine($"% Generated: {now:O} | Exported By: {user} ({role})");
        if (!isSignedExport)
        {
            pdfBuilder.AppendLine("% Watermark: UNSIGNED WORKING COPY");
        }
        pdfBuilder.AppendLine();
        pdfBuilder.AppendLine("================================================================================");
        pdfBuilder.AppendLine("               DELEGATION OF AUTHORITY (DOA) COMPLIANCE LOG                     ");
        pdfBuilder.AppendLine("================================================================================");
        pdfBuilder.AppendLine($"Site ID: {rawSiteId}");
        pdfBuilder.AppendLine($"Principal Investigator: {piName}");
        pdfBuilder.AppendLine($"Export Timestamp: {now:u}");
        pdfBuilder.AppendLine($"Total Delegations: {delegations.Count}");
        pdfBuilder.AppendLine("--------------------------------------------------------------------------------");
        pdfBuilder.AppendLine("DELEGATED SITE PERSONNEL:");
        foreach (var d in delegations)
        {
            var taskList = ParseTaskCodes(d.TaskCodes);
            pdfBuilder.AppendLine($"- Staff User ID : {d.StaffUserId}");
            pdfBuilder.AppendLine($"  Record ID     : {d.Id}");
            pdfBuilder.AppendLine($"  Start Date    : {d.StartDate}");
            pdfBuilder.AppendLine($"  End Date      : {d.EndDate ?? "N/A (Active)"}");
            pdfBuilder.AppendLine($"  Active Status : {(d.IsActive ? "ACTIVE" : "REVOKED")}");
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
        return (delegations.Count, rawPdfBytes);
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

    private static (string User, string Role) ResolveCaller(HttpContext httpContext, string defaultUser)
    {
        string user = CallerScope.UserId(httpContext) ?? (httpContext.User?.Identity?.Name is { Length: > 0 } n ? n : defaultUser);
        string role = CallerScope.Roles(httpContext);
        if (string.IsNullOrWhiteSpace(role))
        {
            role = "SYSTEM";
        }
        return (user, role);
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

public record DelegationTaskRequest(
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("staff_user_id")] string StaffUserId,
    [property: JsonPropertyName("task_codes")] List<string> TaskCodes,
    [property: JsonPropertyName("start_date")] string StartDate,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange,
    [property: JsonPropertyName("study_id")] string? Study = null
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
    [property: JsonPropertyName("version_index")] long VersionIndex
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
