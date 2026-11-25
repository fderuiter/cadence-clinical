using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using MassTransit;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Application;

public sealed class MonitoringVisitApplicationService(
    CtmsDbContext db,
    CtmsUnitOfWork unitOfWork,
    IClock clock,
    IPublishEndpoint? publishEndpoint = null)
{
    public async Task<IResult> GetMonitoringVisitsAsync(
        string? studyId,
        string? siteId,
        string? craId,
        string? status,
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
        var dtos = scopedVisits.Select(ToResponseDto).ToList();
        return TypedResults.Ok(dtos);
    }

    public async Task<IResult> ScheduleMonitoringVisitAsync(
        MonitoringVisitCreateDto request,
        HttpContext httpContext,
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

        if (!CtmsScopeGuard.TryExtractReasonForChange(httpContext, request.ReasonForChange, out string reason, out var problem))
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

        var (user, role) = CallerScope.ResolveActor(httpContext, request.CraId);
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

        db.CtmsAuditLogs.Add(new CtmsAuditLog
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
        });

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Created($"/api/v1/ctms/monitoring-visits/{visit.Id}", ToResponseDto(visit));
    }

    public async Task<IResult> CompleteMonitoringVisitAsync(
        string visitId,
        MonitoringVisitCompleteDto request,
        HttpContext httpContext,
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

        if (!CtmsScopeGuard.TryExtractReasonForChange(httpContext, request?.ReasonForChange, out string reason, out var problem))
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

        var (user, role) = CallerScope.ResolveActor(httpContext, visit.CraId);
        DateTimeOffset now = clock.UtcNow;

        var completeResult = visit.Complete(request.ActualDate, reason, user, now, role);
        if (completeResult.IsFailure)
        {
            return TypedResults.Problem(
                title: "Invalid visit status",
                detail: completeResult.Error!.Description,
                statusCode: StatusCodes.Status400BadRequest);
        }

        int findingsCount = 0;
        if (request.Findings is { Count: > 0 })
        {
            findingsCount = request.Findings.Count;
            foreach (var finding in request.Findings)
            {
                db.CtmsMonitoringVisitFindings.Add(new CtmsMonitoringVisitFinding
                {
                    VisitId = visit.Id,
                    Text = finding.Text ?? string.Empty,
                    Severity = finding.Severity ?? "MINOR",
                    ResolutionStatus = string.IsNullOrWhiteSpace(finding.ResolutionStatus) ? "OPEN" : finding.ResolutionStatus,
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = user,
                    ReasonForChange = reason,
                    VersionIndex = 1
                });
            }
        }

        db.CtmsAuditLogs.Add(new CtmsAuditLog
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
        });

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Ok(ToResponseDto(visit));
    }

    public async Task<IResult> SignOffMonitoringVisitAsync(
        string visitId,
        HttpContext httpContext,
        CancellationToken ct,
        IPublishEndpoint? overridePublishEndpoint = null)
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

        if (!CtmsScopeGuard.TryExtractReasonForChange(httpContext, null, out string reason, out var problem))
        {
            return problem!;
        }

        var (user, role) = CallerScope.ResolveActor(httpContext, "supervisor");
        DateTimeOffset now = clock.UtcNow;

        string signatureJson = JsonSerializer.Serialize(new
        {
            signer = user,
            signer_role = role,
            timestamp = now.ToString("o"),
            meaning = "Supervisory Sign-off on Monitoring Visit",
            endorsement = reason,
            signature_type = "ELECTRONIC_RECORD_SIGN_OFF"
        });

        var signOffResult = visit.SignOff(user, now, signatureJson, reason, user, role);
        if (signOffResult.IsFailure)
        {
            return TypedResults.Problem(
                title: "Invalid visit status",
                detail: signOffResult.Error!.Description,
                statusCode: StatusCodes.Status400BadRequest);
        }

        db.CtmsAuditLogs.Add(new CtmsAuditLog
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
        });

        await unitOfWork.CommitAsync(ct);

        var publisher = overridePublishEndpoint ?? publishEndpoint;
        if (publisher is not null)
        {
            try
            {
                await publisher.Publish(new ClinicalVisitCompleted(
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

        return TypedResults.Ok(ToResponseDto(visit));
    }

    public async Task<IResult> GetMonitoringVisitLettersAsync(
        string visitId,
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

    public async Task<IResult> GetMonitoringVisitLetterByTypeAsync(
        string visitId,
        string letterType,
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

    public static MonitoringVisitResponseDto ToResponseDto(CtmsMonitoringVisit visit) =>
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
            SigningTimestamp: visit.SigningTimestamp?.ToString("o")
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
            VersionIndex: letter.VersionIndex
        );
}
