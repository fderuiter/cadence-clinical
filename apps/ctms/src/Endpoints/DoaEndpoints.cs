using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Kernel;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
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
using Microsoft.Extensions.DependencyInjection;

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
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(AssignPermission).RequireCallerStudyAndSiteScope();

        doa.MapPost("/revoke", RevokeDelegationAsync)
            .WithName("RevokeSiteTasks")
            .WithSummary("Revoke or end a delegated trial duty with reason for change.")
            .Produces<DelegationResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(RevokePermission).RequireCallerStudyAndSiteScope();

        doa.MapPost("/sign-off", SignOffDelegationAsync)
            .WithName("SignOffDelegation")
            .WithSummary("Endorse Delegation of Authority task assignment with Principal Investigator eSignature.")
            .Produces<DelegationResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SignOffPermission).RequireCallerStudyAndSiteScope();

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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadPermission).RequireSiteScope("site_id");

        return group;
    }

    public static async Task<IResult> DelegateTasksAsync(
        [FromBody] DelegationTaskRequest request,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        if (request is null)
        {
            return TypedResults.Problem(
                title: "Invalid request",
                detail: "Request payload cannot be empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Site))
        {
            return TypedResults.Problem(
                title: "Site ID required",
                detail: "Site ID is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.StaffUserId) && string.IsNullOrWhiteSpace(request.StaffEmail))
        {
            return TypedResults.Problem(
                title: "Staff User ID required",
                detail: "Either Staff User ID or colleague email address is required.",
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

        if (await CtmsScopeGuard.RequireDoaSiteScopeAsync(httpContext, db, request.Site, ct) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        var siteGate = await db.CtmsSiteGreenlightGates.AsNoTracking()
            .FirstOrDefaultAsync(g => g.SiteId == request.Site, ct);
        if (siteGate is null && !await SiteExistsAsync(db, request.Site, ct))
        {
            return TypedResults.Problem(
                title: "Site not found",
                detail: $"Site '{request.Site}' does not exist.",
                statusCode: StatusCodes.Status404NotFound);
        }

        string resolvedStaffUserId = request.StaffUserId?.Trim() ?? string.Empty;
        IReadOnlyList<Guid> kernelGrantIds = [];
        if (!string.IsNullOrWhiteSpace(request.StaffEmail))
        {
            var bridge = httpContext.RequestServices?.GetService<IKernelDelegationBridge>() ?? new NoOpKernelDelegationBridge();
            var bridgeOutcome = await bridge.DelegateDutiesAsync(
                httpContext,
                new KernelDelegateDutiesInput(
                    Email: request.StaffEmail.Trim(),
                    FullName: request.StaffName,
                    SiteCode: request.Site.Trim(),
                    StudyCode: !string.IsNullOrWhiteSpace(request.StudyCode) ? request.StudyCode.Trim() : siteGate?.StudyId,
                    Duties: request.TaskCodes,
                    StartDate: request.StartDate.Trim(),
                    EndDate: request.EndDate),
                reason,
                ct);
            if (!bridgeOutcome.IsSuccess)
            {
                return Results.Text(
                    bridgeOutcome.RawProblemJson,
                    contentType: bridgeOutcome.ContentType,
                    statusCode: bridgeOutcome.StatusCode);
            }

            if (string.IsNullOrWhiteSpace(resolvedStaffUserId))
            {
                resolvedStaffUserId = bridgeOutcome.Value!.PersonId.ToString("D");
            }

            kernelGrantIds = bridgeOutcome.Value!.GrantIds;
        }

        var now = clock.UtcNow;
        var (user, role) = ResolveCaller(httpContext, "site_coordinator");

        var delegation = new CtmsDelegation
        {
            SiteId = request.Site.Trim(),
            StaffUserId = resolvedStaffUserId,
            TaskCodes = JsonSerializer.Serialize(request.TaskCodes),
            StartDate = request.StartDate.Trim(),
            EndDate = string.IsNullOrWhiteSpace(request.EndDate) ? null : request.EndDate.Trim(),
            IsActive = true,
            SignedOff = false,
            KernelGrantIds = kernelGrantIds.Count > 0 ? JsonSerializer.Serialize(kernelGrantIds) : null,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            ReasonForChange = reason,
            VersionIndex = 1,
        };

        db.CtmsDelegations.Add(delegation);

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_DELEGATE",
            Details = $"Delegated tasks [{string.Join(", ", request.TaskCodes)}] to staff {delegation.StaffUserId} at site {delegation.SiteId}.",
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
        CancellationToken ct)
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

        var delegation = await db.CtmsDelegations.FirstOrDefaultAsync(d => d.Id == request.RecordId, ct);
        if (delegation is null)
        {
            return TypedResults.Problem(
                title: "Delegation record not found",
                detail: $"Delegation record '{request.RecordId}' does not exist.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (await CtmsScopeGuard.RequireDoaSiteScopeAsync(httpContext, db, delegation.SiteId, ct) is { } scopeDenied)
        {
            return scopeDenied;
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

        var linkedGrantIds = ParseKernelGrantIds(delegation.KernelGrantIds);
        if (linkedGrantIds.Count > 0 && httpContext.RequestServices?.GetService<IKernelDelegationBridge>() is { } bridge)
        {
            var revokeOutcome = await bridge.RevokeGrantsAsync(httpContext, linkedGrantIds, reason, ct);
            if (!revokeOutcome.IsSuccess)
            {
                return Results.Text(
                    revokeOutcome.RawProblemJson,
                    contentType: revokeOutcome.ContentType,
                    statusCode: revokeOutcome.StatusCode);
            }
        }

        var now = clock.UtcNow;
        var (user, role) = ResolveCaller(httpContext, "investigator");

        delegation.IsActive = false;
        delegation.EndDate = now.ToString("yyyy-MM-dd");
        delegation.ReasonForChange = reason;
        delegation.VersionIndex++;

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_REVOKE",
            Details = $"Revoked delegation assignment {delegation.Id} for staff {delegation.StaffUserId} at site {delegation.SiteId}.",
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
        CancellationToken ct)
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

        var delegation = await db.CtmsDelegations.FirstOrDefaultAsync(d => d.Id == request.RecordId, ct);
        if (delegation is null)
        {
            return TypedResults.Problem(
                title: "Delegation record not found",
                detail: $"Delegation record '{request.RecordId}' does not exist.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (await CtmsScopeGuard.RequireDoaSiteScopeAsync(httpContext, db, delegation.SiteId, ct) is { } scopeDenied)
        {
            return scopeDenied;
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

        var (user, role) = ResolveCaller(httpContext, "investigator");
        var now = clock.UtcNow;

        delegation.SignedOff = true;
        delegation.ReasonForChange = reason;
        delegation.VersionIndex++;

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_PI_SIGN_OFF",
            Details = $"Principal Investigator sign-off endorsed for delegation {delegation.Id} (staff {delegation.StaffUserId}) at site {delegation.SiteId}.",
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
        CancellationToken ct)
    {
        if (await CtmsScopeGuard.RequireDoaSiteScopeAsync(httpContext, db, siteId, ct) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (string.IsNullOrWhiteSpace(siteId) || !await SiteExistsAsync(db, siteId, ct))
        {
            return TypedResults.Problem(
                title: "Site not found",
                detail: $"Site '{siteId}' does not exist.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var delegations = await db.CtmsDelegations.AsNoTracking()
            .Where(d => d.SiteId == siteId)
            .OrderBy(d => d.StartDate)
            .ToListAsync(ct);

        var audits = await db.CtmsAuditLogs.AsNoTracking()
            .Where(a => a.Details.Contains(siteId) && (a.Action.StartsWith("DOA_") || a.Action.Contains("DELEGAT")))
            .OrderBy(a => a.Timestamp)
            .ToListAsync(ct);

        var gate = await db.CtmsSiteGreenlightGates.AsNoTracking()
            .FirstOrDefaultAsync(g => g.SiteId == siteId, ct);

        string piName = !string.IsNullOrWhiteSpace(gate?.GreenlightCertifiedBy)
            ? gate.GreenlightCertifiedBy
            : "Principal Investigator";

        var response = new DoaLogResponseDto(
            Site: siteId,
            PiName: piName,
            DelegatedStaff: delegations.Select(ToResponseDto).ToList(),
            AuditHistory: audits.Select(ToAuditDto).ToList());

        return TypedResults.Ok(response);
    }

    public static async Task<IResult> ExportSiteDoaPdfAsync(
        [FromRoute(Name = "site_id")] string siteId,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        if (await CtmsScopeGuard.RequireDoaSiteScopeAsync(httpContext, db, siteId, ct) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (string.IsNullOrWhiteSpace(siteId) || !await SiteExistsAsync(db, siteId, ct))
        {
            return TypedResults.Problem(
                title: "Site not found",
                detail: $"Site '{siteId}' does not exist.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var delegations = await db.CtmsDelegations.AsNoTracking()
            .Where(d => d.SiteId == siteId)
            .OrderBy(d => d.StartDate)
            .ToListAsync(ct);

        var audits = await db.CtmsAuditLogs.AsNoTracking()
            .Where(a => a.Details.Contains(siteId) && (a.Action.StartsWith("DOA_") || a.Action.Contains("DELEGAT")))
            .OrderBy(a => a.Timestamp)
            .ToListAsync(ct);

        var gate = await db.CtmsSiteGreenlightGates.AsNoTracking()
            .FirstOrDefaultAsync(g => g.SiteId == siteId, ct);

        string piName = !string.IsNullOrWhiteSpace(gate?.GreenlightCertifiedBy)
            ? gate.GreenlightCertifiedBy
            : "Principal Investigator";

        var (user, role) = ResolveCaller(httpContext, "investigator");
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

        var (cert, key) = Pkcs7Signer.GenerateSigningCredentials(user, clock);
        var signer = new Pkcs7Signer(cert, key);
        byte[] signedPdfBytes = signer.SignPdf(rawPdfBytes);

        string hash = Convert.ToHexStringLower(SHA256.HashData(signedPdfBytes));

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_EXPORT_PDF",
            Details = $"Exported GxP Delegation of Authority PDF for site {siteId} with {delegations.Count} records. SHA-256: {hash}",
            ReasonForChange = "Export GxP Delegation of Authority PDF",
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
        };
        db.CtmsAuditLogs.Add(auditEntry);
        await db.SaveChangesAsync(ct);

        return Results.File(signedPdfBytes, "application/pdf", $"DOA_Log_{siteId}.pdf");
    }

    public static async Task<bool> SiteExistsAsync(CtmsDbContext db, string siteId, CancellationToken ct)
    {
        return await db.CtmsSiteGreenlightGates.AnyAsync(g => g.SiteId == siteId, ct)
            || await db.CtmsDelegations.AnyAsync(d => d.SiteId == siteId, ct)
            || await db.CtmsSiteMilestones.AnyAsync(m => m.SiteId == siteId, ct)
            || await db.CtmsMonitoringVisits.AnyAsync(v => v.SiteId == siteId, ct);
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

    private static List<Guid> ParseKernelGrantIds(string? grantIdsJson)
    {
        if (string.IsNullOrWhiteSpace(grantIdsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(grantIdsJson) ?? [];
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
            VersionIndex: d.VersionIndex,
            KernelGrantIds: ParseKernelGrantIds(d.KernelGrantIds));

    public static DoaAuditHistoryItemDto ToAuditDto(CtmsAuditLog a) =>
        new(
            Id: a.Id,
            Timestamp: a.Timestamp,
            UserId: a.UserId,
            UserRole: a.UserRole,
            Action: a.Action,
            Details: a.Details);
}

public record DelegationTaskRequest(
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("staff_user_id")] string? StaffUserId,
    [property: JsonPropertyName("task_codes")] List<string> TaskCodes,
    [property: JsonPropertyName("start_date")] string StartDate,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange,
    [property: JsonPropertyName("staff_email")] string? StaffEmail = null,
    [property: JsonPropertyName("staff_name")] string? StaffName = null,
    [property: JsonPropertyName("end_date")] string? EndDate = null,
    [property: JsonPropertyName("study_id")] string? StudyCode = null
);

public record RevokeDelegationRequest(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange
);

public record DoaSignOffRequest(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange
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
    [property: JsonPropertyName("kernel_grant_ids")] IReadOnlyList<Guid>? KernelGrantIds = null
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
