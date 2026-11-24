using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Operational Minimal API endpoints for CTMS site monitoring visits and Part 11 sign-offs.
/// </summary>
public static class MonitoringVisitEndpoints
{
    public const string ReadPermission = "ctms_monitoring_visit:read";
    public const string CreatePermission = "ctms_monitoring_visit:create";
    public const string UpdatePermission = "ctms_monitoring_visit:update";
    public const string SignOffPermission = "ctms_monitoring_visit:sign_off";
    public const string ReadLetterPermission = "ctms_monitoring_letter:read";
    public const string ReadLetterTypePermission = "ctms_monitoring_letter:read_type";
    public const string SignOffSemanticAction = "ctms.monitoring_visit.sign_off";

    public static string SignOffAction(string visit_id) => $"/api/v1/ctms/monitoring-visits/{visit_id}/sign-off";

    public static RouteGroupBuilder MapMonitoringVisitEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/monitoring-visits", GetMonitoringVisitsAsync)
            .WithName("ListMonitoringVisits")
            .WithSummary("List monitoring visits filtered by study, site, CRA, or status.")
            .Produces<List<MonitoringVisitResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ReadPermission).RequireCallerStudyAndSiteScope();

        group.MapPost("/monitoring-visits", ScheduleMonitoringVisitAsync)
            .WithName("ScheduleMonitoringVisit")
            .WithSummary("Schedule a new monitoring visit.")
            .Produces<MonitoringVisitResponseDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(CreatePermission).RequireCallerStudyAndSiteScope();

        group.MapPost("/monitoring-visits/{visit_id}/complete", CompleteMonitoringVisitAsync)
            .WithName("CompleteMonitoringVisit")
            .WithSummary("Complete a scheduled monitoring visit with actual date and clinical findings.")
            .Produces<MonitoringVisitResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(UpdatePermission).RequireCallerStudyAndSiteScope();

        group.MapPost("/monitoring-visits/{visit_id}/sign-off", SignOffMonitoringVisitAsync)
            .WithName("SignOffMonitoringVisit")
            .WithSummary("Perform supervisory Part 11 sign-off on a completed monitoring visit.")
            .Produces<MonitoringVisitResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SignOffPermission).RequireCallerStudyAndSiteScope()
            .AddEndpointFilter(PreCheckVisitScopeFilter)
            .RequireElectronicSignature(
                SignOffSemanticAction,
                ctx => SignOffAction(ctx.Request.RouteValues["visit_id"]?.ToString() ?? string.Empty));

        group.MapGet("/monitoring-visits/{visit_id}/letters", GetMonitoringVisitLettersAsync)
            .WithName("GetMonitoringVisitLetters")
            .WithSummary("Get generated letters for a monitoring visit.")
            .Produces<List<GeneratedLetterResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadLetterPermission).RequireCallerStudyAndSiteScope();

        group.MapGet("/monitoring-visits/{visit_id}/letters/{letter_type}", GetMonitoringVisitLetterByTypeAsync)
            .WithName("GetMonitoringVisitLetterByType")
            .WithSummary("Get a generated visit letter by type.")
            .Produces<GeneratedLetterResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadLetterTypePermission).RequireCallerStudyAndSiteScope();

        return group;
    }

    public static async Task<IResult> GetMonitoringVisitsAsync(
        [FromQuery(Name = "study_id")] string? studyId,
        [FromQuery(Name = "site_id")] string? siteId,
        [FromQuery(Name = "cra_id")] string? craId,
        [FromQuery(Name = "status")] string? status,
        CtmsDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (CtmsScopeGuard.ResolveStudyAndSiteFilters(httpContext, studyId, siteId, out var studyFilter, out var siteFilter) is { } scopeDenied)
        {
            return scopeDenied;
        }

        IQueryable<CtmsMonitoringVisit> query = db.CtmsMonitoringVisits.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(v => v.StudyId == studyId);
        }

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            query = query.Where(v => v.SiteId == siteId);
        }

        if (!string.IsNullOrWhiteSpace(craId))
        {
            query = query.Where(v => v.CraId == craId);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<CtmsMonitoringVisitStatus>(status, true, out var statusEnum))
        {
            query = query.Where(v => v.Status == statusEnum);
        }

        var visits = await query
            .OrderByDescending(v => v.ScheduledDate)
            .ToListAsync(ct);

        var scopedVisits = CtmsScopeGuard.ApplyFilters(visits, studyFilter, v => v.StudyId, siteFilter, v => v.SiteId);
        var visitIds = scopedVisits.Select(v => v.Id).ToList();
        var findingsByVisit = visitIds.Count == 0
            ? new Dictionary<string, List<MonitoringVisitFindingDto>>(StringComparer.Ordinal)
            : (await db.CtmsMonitoringVisitFindings.AsNoTracking()
                .Where(f => visitIds.Contains(f.VisitId))
                .OrderBy(f => f.CreatedAt)
                .ToListAsync(ct))
                .GroupBy(f => f.VisitId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(ToFindingDto).ToList(), StringComparer.Ordinal);

        var dtos = scopedVisits
            .Select(v => ToResponseDto(v, findingsByVisit.GetValueOrDefault(v.Id)))
            .ToList();
        return TypedResults.Ok(dtos);
    }

    private static async ValueTask<object?> PreCheckVisitScopeFilter(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (CtmsScopeGuard.RequireAnyScopeForLookup(context.HttpContext) is { } missingScope)
        {
            return missingScope;
        }

        string? visitId = context.HttpContext.Request.RouteValues.TryGetValue("visit_id", out var rv) ? rv?.ToString() : null;
        if (!string.IsNullOrWhiteSpace(visitId))
        {
            var db = context.HttpContext.RequestServices.GetService(typeof(CtmsDbContext)) as CtmsDbContext;
            if (db is not null)
            {
                var visit = await db.CtmsMonitoringVisits.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == visitId, context.HttpContext.RequestAborted);
                if (visit is not null
                    && CtmsScopeGuard.RequireStudyAndSite(context.HttpContext, visit.StudyId, visit.SiteId) is { } denied)
                {
                    return denied;
                }
            }
        }

        return await next(context);
    }

    public static async Task<IResult> ScheduleMonitoringVisitAsync(
        [FromBody] MonitoringVisitCreateDto request,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        if (request is null)
        {
            return TypedResults.Problem(
                title: "Invalid request payload",
                detail: "Request body cannot be null.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Study) ||
            string.IsNullOrWhiteSpace(request.Site) ||
            string.IsNullOrWhiteSpace(request.CraId) ||
            string.IsNullOrWhiteSpace(request.VisitType))
        {
            return TypedResults.Problem(
                title: "Validation error",
                detail: "study_id, site_id, cra_id, and visit_type are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, request.Study, request.Site) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        if (request.ScheduledDate == default)
        {
            return TypedResults.Problem(
                title: "Validation error",
                detail: "scheduled_date is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var (user, role) = ResolveCaller(httpContext, request.CraId);
        DateTimeOffset now = clock.UtcNow;

        var visit = new CtmsMonitoringVisit(Guid.NewGuid().ToString(), CtmsMonitoringVisitStatus.Scheduled)
        {
            StudyId = request.Study.Trim(),
            SiteId = request.Site.Trim(),
            CraId = request.CraId.Trim(),
            VisitType = request.VisitType.Trim(),
            ScheduledDate = request.ScheduledDate,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            ReasonForChange = reason,
            VersionIndex = 1
        };

        db.CtmsMonitoringVisits.Add(visit);

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "SCHEDULE_MONITORING_VISIT",
            Details = $"Scheduled {visit.VisitType} monitoring visit {visit.Id} for site {visit.SiteId} in study {visit.StudyId} on {visit.ScheduledDate:yyyy-MM-dd}.",
            StudyId = visit.StudyId,
            SiteId = visit.SiteId,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/ctms/monitoring-visits/{visit.Id}", ToResponseDto(visit));
    }

    public static async Task<IResult> CompleteMonitoringVisitAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        [FromBody] MonitoringVisitCompleteDto request,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        if (CtmsScopeGuard.RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return missingScope;
        }

        var visit = await db.CtmsMonitoringVisits.FirstOrDefaultAsync(v => v.Id == visitId, ct);
        if (visit is null)
        {
            return TypedResults.Problem(
                title: "Visit not found",
                detail: $"Monitoring visit '{visitId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, visit.StudyId, visit.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!TryExtractReasonForChange(httpContext, request?.ReasonForChange, out string reason, out var problem))
        {
            return problem!;
        }

        if (request is null || request.ActualDate == default)
        {
            return TypedResults.Problem(
                title: "Validation error",
                detail: "actual_date is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var (user, role) = ResolveCaller(httpContext, visit.CraId);
        DateTimeOffset now = clock.UtcNow;

        var completeResult = visit.Complete(request.ActualDate, reason, user, now, role);
        if (completeResult.IsFailure)
        {
            return TypedResults.Problem(
                title: "Invalid visit status",
                detail: completeResult.Error!.Description,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var createdFindings = new List<CtmsMonitoringVisitFinding>();
        int findingsCount = 0;
        if (request.Findings is { Count: > 0 })
        {
            findingsCount = request.Findings.Count;
            for (int findingIndex = 0; findingIndex < request.Findings.Count; findingIndex++)
            {
                var finding = request.Findings[findingIndex];
                var findingEntity = new CtmsMonitoringVisitFinding
                {
                    VisitId = visit.Id,
                    Text = finding.Text ?? string.Empty,
                    Severity = finding.Severity ?? "MINOR",
                    ResolutionStatus = string.IsNullOrWhiteSpace(finding.ResolutionStatus) ? "OPEN" : finding.ResolutionStatus,
                    CreatedAt = now.UtcDateTime.AddTicks(findingIndex),
                    CreatedBy = user,
                    ReasonForChange = reason,
                    VersionIndex = 1
                };
                db.CtmsMonitoringVisitFindings.Add(findingEntity);
                createdFindings.Add(findingEntity);
            }
        }

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "COMPLETE_MONITORING_VISIT",
            Details = $"Completed monitoring visit {visit.Id} for site {visit.SiteId} in study {visit.StudyId} with {findingsCount} findings.",
            StudyId = visit.StudyId,
            SiteId = visit.SiteId,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await db.SaveChangesAsync(ct);

        var persistedFindings = await db.CtmsMonitoringVisitFindings.AsNoTracking()
            .Where(f => f.VisitId == visit.Id)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(ct);
        var findingDtos = (persistedFindings.Count > 0 ? persistedFindings : createdFindings)
            .Select(ToFindingDto)
            .ToList();

        return TypedResults.Ok(ToResponseDto(visit, findingDtos));
    }

    public static async Task<IResult> SignOffMonitoringVisitAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        CancellationToken ct,
        [FromServices] IPublishEndpoint? publishEndpoint = null)
    {
        if (CtmsScopeGuard.RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return missingScope;
        }

        var visit = await db.CtmsMonitoringVisits.FirstOrDefaultAsync(v => v.Id == visitId, ct);
        if (visit is null)
        {
            return TypedResults.Problem(
                title: "Visit not found",
                detail: $"Monitoring visit '{visitId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, visit.StudyId, visit.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        if (!TryExtractReasonForChange(httpContext, null, out string reason, out var problem))
        {
            return problem!;
        }

        var (user, role) = ResolveCaller(httpContext, "supervisor");
        DateTimeOffset now = clock.UtcNow;

        var manifestationObj = new
        {
            signer = user,
            signer_role = role,
            timestamp = now.ToString("o"),
            meaning = "Supervisory Sign-off on Monitoring Visit",
            endorsement = reason,
            signature_type = "ELECTRONIC_RECORD_SIGN_OFF"
        };
        string signatureJson = JsonSerializer.Serialize(manifestationObj);

        var signOffResult = visit.SignOff(user, now, signatureJson, reason, user, role);
        if (signOffResult.IsFailure)
        {
            return TypedResults.Problem(
                title: "Invalid visit status",
                detail: signOffResult.Error!.Description,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "SIGN_OFF_MONITORING_VISIT",
            Details = $"Supervisory sign-off completed for monitoring visit {visit.Id} at site {visit.SiteId} in study {visit.StudyId}.",
            StudyId = visit.StudyId,
            SiteId = visit.SiteId,
            ReasonForChange = reason,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await db.SaveChangesAsync(ct);

        if (publishEndpoint is not null)
        {
            try
            {
                await publishEndpoint.Publish(new ClinicalVisitCompleted(
                    EventId: $"MVR-{visit.Id}",
                    Study: visit.StudyId,
                    Site: visit.SiteId,
                    VisitCode: visit.VisitType,
                    CompletedAt: now,
                    CraId: visit.CraId,
                    Signer: user,
                    VisitId: visit.Id
                ), ct);
            }
            catch
            {
                // Non-blocking asynchronous outbox publishing
            }
        }

        var visitFindings = await db.CtmsMonitoringVisitFindings.AsNoTracking()
            .Where(f => f.VisitId == visit.Id)
            .OrderBy(f => f.CreatedAt)
            .Select(f => ToFindingDto(f))
            .ToListAsync(ct);

        return TypedResults.Ok(ToResponseDto(visit, visitFindings));
    }

    public static async Task<IResult> GetMonitoringVisitLettersAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        CtmsDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (CtmsScopeGuard.RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return missingScope;
        }

        var visit = await db.CtmsMonitoringVisits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId, ct);
        if (visit is null)
        {
            return TypedResults.Problem(
                title: "Visit not found",
                detail: $"Monitoring visit '{visitId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, visit.StudyId, visit.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        var letters = await db.CtmsGeneratedLetters.AsNoTracking()
            .Where(l => l.VisitId == visitId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        var dtos = letters.Select(ToLetterResponseDto).ToList();
        return TypedResults.Ok(dtos);
    }

    public static async Task<IResult> GetMonitoringVisitLetterByTypeAsync(
        [FromRoute(Name = "visit_id")] string visitId,
        [FromRoute(Name = "letter_type")] string letterType,
        CtmsDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (CtmsScopeGuard.RequireAnyScopeForLookup(httpContext) is { } missingScope)
        {
            return missingScope;
        }

        var visit = await db.CtmsMonitoringVisits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId, ct);
        if (visit is null)
        {
            return TypedResults.Problem(
                title: "Visit not found",
                detail: $"Monitoring visit '{visitId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, visit.StudyId, visit.SiteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        var letter = await db.CtmsGeneratedLetters.AsNoTracking()
            .Where(l => l.VisitId == visitId && l.LetterType == letterType)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (letter is null)
        {
            return TypedResults.Problem(
                title: "Letter not found",
                detail: $"Letter of type '{letterType}' for visit '{visitId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(ToLetterResponseDto(letter));
    }

    public static bool TryExtractReasonForChange(
        HttpContext httpContext,
        string? bodyReason,
        out string reason,
        out IResult? problem)
    {
        reason = string.Empty;
        problem = null;

        if (httpContext.Request.Headers.TryGetValue("X-Change-Reason", out var headerVal))
        {
            string headerStr = headerVal.ToString();
            if (!ChangeReasonHeader.TryDecode(headerStr, out string? decoded))
            {
                problem = TypedResults.Problem(
                    title: "Invalid reason for change header",
                    detail: "X-Change-Reason must be printable ASCII or RFC 8187 \"UTF-8''\" followed by percent-encoded UTF-8.",
                    statusCode: StatusCodes.Status400BadRequest);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(decoded))
            {
                reason = decoded.Trim();
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(bodyReason))
        {
            reason = bodyReason.Trim();
            return true;
        }

        problem = TypedResults.Problem(
            title: "Reason for change required",
            detail: "Reason for change is required.",
            statusCode: StatusCodes.Status400BadRequest);
        return false;
    }

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

    public static MonitoringVisitFindingDto ToFindingDto(CtmsMonitoringVisitFinding f) =>
        new(
            Id: f.Id,
            Text: f.Text,
            Severity: f.Severity,
            ResolutionStatus: f.ResolutionStatus,
            CreatedAt: f.CreatedAt.ToString("o"));

    public static MonitoringVisitResponseDto ToResponseDto(
        CtmsMonitoringVisit visit,
        List<MonitoringVisitFindingDto>? findings = null) =>
        new(
            Id: visit.Id,
            Study: visit.StudyId,
            Site: visit.SiteId,
            CraId: visit.CraId,
            VisitType: visit.VisitType,
            ScheduledDate: visit.ScheduledDate.ToString("o"),
            ActualDate: visit.ActualDate?.ToString("o"),
            Status: visit.Status.ToString().ToUpperInvariant(),
            CreatedAt: visit.CreatedAt.ToString("o"),
            CreatedBy: visit.CreatedBy,
            ReasonForChange: visit.ReasonForChange,
            VersionIndex: visit.VersionIndex,
            SignatureManifestation: visit.SignatureManifestation,
            Signer: visit.Signer,
            SigningTimestamp: visit.SigningTimestamp?.ToString("o"),
            Findings: findings ?? []
        );

    public static GeneratedLetterResponseDto ToLetterResponseDto(CtmsGeneratedLetter letter) =>
        new(
            Id: letter.Id,
            VisitId: letter.VisitId,
            LetterType: letter.LetterType,
            RenderedContent: letter.RenderedContent,
            CreatedAt: letter.CreatedAt.ToString("o"),
            CreatedBy: letter.CreatedBy,
            ReasonForChange: letter.ReasonForChange,
            VersionIndex: letter.VersionIndex,
            Status: letter.Status.ToString().ToUpperInvariant(),
            IsSigned: letter.IsSigned,
            PdfSha256Hash: letter.PdfSha256Hash,
            EtmfArtifactId: letter.EtmfDocumentId,
            PdfAccessUrl: $"/api/v1/ctms/letters/{letter.Id}/pdf"
        );
}

public record MonitoringVisitFindingDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("resolution_status")] string ResolutionStatus,
    [property: JsonPropertyName("created_at")] string CreatedAt
);

public record MonitoringVisitResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("study_id")] string Study,
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("cra_id")] string CraId,
    [property: JsonPropertyName("visit_type")] string VisitType,
    [property: JsonPropertyName("scheduled_date")] string ScheduledDate,
    [property: JsonPropertyName("actual_date")] string? ActualDate,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("version_index")] long VersionIndex,
    [property: JsonPropertyName("signature_manifestation")] string? SignatureManifestation = null,
    [property: JsonPropertyName("signer")] string? Signer = null,
    [property: JsonPropertyName("signing_timestamp")] string? SigningTimestamp = null,
    [property: JsonPropertyName("findings")] List<MonitoringVisitFindingDto>? Findings = null
);

public record MonitoringVisitCreateDto(
    [property: JsonPropertyName("study_id")] string Study,
    [property: JsonPropertyName("site_id")] string Site,
    [property: JsonPropertyName("cra_id")] string CraId,
    [property: JsonPropertyName("visit_type")] string VisitType,
    [property: JsonPropertyName("scheduled_date")] DateTimeOffset ScheduledDate,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange = null
);

public record FindingCreateDto(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("resolution_status")] string? ResolutionStatus = null
);

public record MonitoringVisitCompleteDto(
    [property: JsonPropertyName("actual_date")] DateTimeOffset ActualDate,
    [property: JsonPropertyName("findings")] List<FindingCreateDto>? Findings = null,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange = null
);

public record GeneratedLetterResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("visit_id")] string VisitId,
    [property: JsonPropertyName("letter_type")] string LetterType,
    [property: JsonPropertyName("rendered_content")] string RenderedContent,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange,
    [property: JsonPropertyName("version_index")] long VersionIndex,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("is_signed")] bool IsSigned = false,
    [property: JsonPropertyName("pdf_sha256_hash")] string? PdfSha256Hash = null,
    [property: JsonPropertyName("etmf_document_id")] string? EtmfArtifactId = null,
    [property: JsonPropertyName("pdf_access_url")] string? PdfAccessUrl = null
);
