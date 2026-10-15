using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Application;
using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Safety.Endpoints;

public static class SignalEndpoints
{
    public const string SignalPermission = "safety_report:create";

    public static RouteGroupBuilder MapSignalEndpoints(this RouteGroupBuilder group)
    {
        // Primary routes under /signals
        group.MapPost("/signals/run", ExecuteScanAsync)
            .WithName("RunSignalDetectionScan")
            .WithSummary("Executes a disproportionality signal detection scan run.")
            .Produces<SignalDetectionScanResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SignalPermission);

        group.MapPost("/signals/detect", ExecuteScanAsync)
            .WithName("DetectSignalScan")
            .WithSummary("Alias route for executing a disproportionality signal detection scan.")
            .Produces<SignalDetectionScanResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SignalPermission);

        group.MapGet("/signals/runs", ListRunsAsync)
            .WithName("ListSignalDetectionRuns")
            .WithSummary("Lists historical disproportionality signal detection scan runs.")
            .Produces<object[]>(StatusCodes.Status200OK)
            .RequirePermission(SignalPermission);

        group.MapGet("/signals/runs/{id}", GetRunByIdAsync)
            .WithName("GetSignalDetectionRunById")
            .WithSummary("Retrieves details and metric tables for a signal detection scan run.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SignalPermission);

        group.MapGet("/signals/alerts", ListAlertsAsync)
            .WithName("ListSignalAlerts")
            .WithSummary("Lists safety signal review alerts with optional filtering.")
            .Produces<SafetySignalAlertDto[]>(StatusCodes.Status200OK)
            .RequirePermission(SignalPermission);

        group.MapGet("/signals/alerts/{id}", GetAlertByIdAsync)
            .WithName("GetSignalAlertById")
            .WithSummary("Retrieves an individual safety signal review alert.")
            .Produces<SafetySignalAlertDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SignalPermission);

        group.MapPost("/signals/alerts/{id}/review", ReviewAlertAsync)
            .WithName("ReviewSignalAlert")
            .WithSummary("Reviews and updates the clinical triage disposition of a safety signal alert.")
            .Produces<SafetySignalAlertDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(SignalPermission);

        group.MapPost("/signals/alerts/{id}/sign", SignAlertAsync)
            .WithName("SignSignalAlert")
            .WithSummary("Applies a 21 CFR Part 11 Electronic Signature to approve a safety signal disposition.")
            .Produces<SafetySignalAlertSignResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SignalPermission);

        // Alias routes under /disproportionality
        group.MapPost("/disproportionality/run", ExecuteScanAsync)
            .WithName("RunDisproportionalityScan")
            .RequirePermission(SignalPermission);

        group.MapPost("/disproportionality/detect", ExecuteScanAsync)
            .WithName("DetectDisproportionalityScan")
            .RequirePermission(SignalPermission);

        group.MapGet("/disproportionality/runs", ListRunsAsync)
            .WithName("ListDisproportionalityRuns")
            .RequirePermission(SignalPermission);

        group.MapGet("/disproportionality/runs/{id}", GetRunByIdAsync)
            .WithName("GetDisproportionalityRunById")
            .RequirePermission(SignalPermission);

        group.MapGet("/disproportionality/alerts", ListAlertsAsync)
            .WithName("ListDisproportionalityAlerts")
            .RequirePermission(SignalPermission);

        group.MapGet("/disproportionality/alerts/{id}", GetAlertByIdAsync)
            .WithName("GetDisproportionalityAlertById")
            .RequirePermission(SignalPermission);

        group.MapPost("/disproportionality/alerts/{id}/review", ReviewAlertAsync)
            .WithName("ReviewDisproportionalityAlert")
            .RequirePermission(SignalPermission);

        group.MapPost("/disproportionality/alerts/{id}/sign", SignAlertAsync)
            .WithName("SignDisproportionalityAlert")
            .RequirePermission(SignalPermission);

        return group;
    }

    public static async Task<IResult> ExecuteScanAsync(
        SignalDetectionScanRequest? request,
        SafetyDbContext dbContext,
        IClock? clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.TargetDrug))
        {
            return TypedResults.Problem("target_drug is required for signal detection scan execution.", statusCode: StatusCodes.Status400BadRequest);
        }

        var callerUserId = httpContext.User?.Identity?.Name ?? "svc:safety-signal-engine";
        var engine = new SignalDetectionEngine(dbContext, clock);

        var response = await engine.ExecuteScanAsync(request, callerUserId, cancellationToken);
        return TypedResults.Ok(response);
    }

    public static async Task<IResult> ListRunsAsync(
        string? study_id,
        string? target_drug,
        SafetyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SafetySignalDetectionRuns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(study_id))
        {
            query = query.Where(r => r.StudyId == study_id);
        }

        if (!string.IsNullOrWhiteSpace(target_drug))
        {
            query = query.Where(r => r.TargetDrug == target_drug);
        }

        var runs = await query.OrderByDescending(r => r.CreatedAt).ToArrayAsync(cancellationToken);
        return TypedResults.Ok(runs);
    }

    public static async Task<IResult> GetRunByIdAsync(
        string id,
        SafetyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.SafetySignalDetectionRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run == null)
        {
            return TypedResults.Problem($"Safety signal detection run '{id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(run);
    }

    public static async Task<IResult> ListAlertsAsync(
        string? study_id,
        string? target_drug,
        string? status,
        string? severity,
        SafetyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SafetySignalAlerts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(study_id))
        {
            query = query.Where(a => a.StudyId == study_id);
        }

        if (!string.IsNullOrWhiteSpace(target_drug))
        {
            query = query.Where(a => a.TargetDrug == target_drug);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(a => a.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            query = query.Where(a => a.Severity == severity);
        }

        var alerts = await query.OrderByDescending(a => a.CreatedAt).ToListAsync(cancellationToken);
        var dtos = alerts.Select(ToDto).ToArray();
        return TypedResults.Ok(dtos);
    }

    public static async Task<IResult> GetAlertByIdAsync(
        string id,
        SafetyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var alert = await dbContext.SafetySignalAlerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert == null)
        {
            return TypedResults.Problem($"Safety signal alert '{id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(ToDto(alert));
    }

    public static async Task<IResult> ReviewAlertAsync(
        string id,
        SafetySignalAlertReviewRequest? request,
        SafetyDbContext dbContext,
        IClock? clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Decision) || string.IsNullOrWhiteSpace(request.Justification))
        {
            return TypedResults.Problem("decision and justification (min length 5) are required for signal alert review.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Justification.Length < 5)
        {
            return TypedResults.Problem("Justification must be at least 5 characters long.", statusCode: StatusCodes.Status400BadRequest);
        }

        var alert = await dbContext.SafetySignalAlerts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert == null)
        {
            return TypedResults.Problem($"Safety signal alert '{id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        string targetStatus = MapDecisionToStatus(request.Decision);

        // Part 11 e-signature enforcement constraint:
        // All alert triage decisions moving alerts to CONFIRMED_SIGNAL or CLOSED require an e-signature manifest ID.
        if ((targetStatus == SafetySignalAlertStatus.ConfirmedSignal || targetStatus == SafetySignalAlertStatus.Closed)
            && string.IsNullOrWhiteSpace(request.EsignatureManifestId))
        {
            return TypedResults.Problem(
                $"Transitioning alert to {targetStatus} requires a valid Part 11 e-signature manifest ID.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var callerUserId = httpContext.User?.Identity?.Name ?? "safety-physician";
        var now = (clock ?? SystemClock.Instance).UtcNow;

        alert.ReviewDecision = request.Decision;
        alert.ReviewJustification = request.Justification;
        if (!string.IsNullOrWhiteSpace(request.ClinicalSummary))
        {
            alert.ClinicalSummary = request.ClinicalSummary;
        }
        if (!string.IsNullOrWhiteSpace(request.AssignedSafetyOfficer))
        {
            alert.AssignedSafetyOfficer = request.AssignedSafetyOfficer;
        }
        if (!string.IsNullOrWhiteSpace(request.EsignatureManifestId))
        {
            alert.EsignatureManifestId = request.EsignatureManifestId;
        }

        alert.ReviewedBy = callerUserId;
        alert.ReviewedAt = now;
        alert.Status = targetStatus;
        alert.ReasonForChange = $"Review decision applied: {request.Decision}";
        alert.VersionIndex++;

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToDto(alert));
    }

    public static async Task<IResult> SignAlertAsync(
        string id,
        SafetySignalAlertSignRequest? request,
        SafetyDbContext dbContext,
        IClock? clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.SignatureSecret) || string.IsNullOrWhiteSpace(request.Decision))
        {
            return TypedResults.Problem("user_id, signature_secret, and decision are required for 21 CFR Part 11 e-signature.", statusCode: StatusCodes.Status400BadRequest);
        }

        var alert = await dbContext.SafetySignalAlerts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert == null)
        {
            return TypedResults.Problem($"Safety signal alert '{id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        var now = (clock ?? SystemClock.Instance).UtcNow;
        string manifestId = $"SIG-MANIFEST-{Guid.NewGuid():N}";
        string targetStatus = MapDecisionToStatus(request.Decision);

        alert.ReviewDecision = request.Decision;
        alert.ReviewJustification = request.Justification;
        if (!string.IsNullOrWhiteSpace(request.ClinicalSummary))
        {
            alert.ClinicalSummary = request.ClinicalSummary;
        }
        alert.ReviewedBy = request.UserId;
        alert.ReviewedAt = now;
        alert.EsignatureManifestId = manifestId;
        alert.Status = targetStatus;
        alert.ReasonForChange = $"21 CFR Part 11 Electronic Signature applied: {request.MeaningOfSignature}";
        alert.VersionIndex++;

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new SafetySignalAlertSignResponse
        {
            AlertId = alert.Id,
            Status = alert.Status,
            ReviewedBy = alert.ReviewedBy,
            ReviewedAt = alert.ReviewedAt?.ToString("o") ?? now.ToString("o"),
            EsignatureManifestId = manifestId,
            Message = "21 CFR Part 11 Electronic Signature applied successfully."
        });
    }

    private static string MapDecisionToStatus(string decision)
    {
        return decision switch
        {
            SafetySignalReviewDecision.ConfirmSignal => SafetySignalAlertStatus.ConfirmedSignal,
            SafetySignalReviewDecision.RefuteSignal => SafetySignalAlertStatus.Refuted,
            SafetySignalReviewDecision.RequestSafetyInquiry => SafetySignalAlertStatus.UnderReview,
            SafetySignalReviewDecision.CloseAlert => SafetySignalAlertStatus.Closed,
            _ => SafetySignalAlertStatus.UnderReview
        };
    }

    public static SafetySignalAlertDto ToDto(SafetySignalAlert alert)
    {
        return new SafetySignalAlertDto
        {
            Id = alert.Id,
            DetectionRunId = alert.DetectionRunId,
            StudyId = alert.StudyId,
            TargetDrug = alert.TargetDrug,
            MeddraTerm = alert.MeddraTerm,
            TermLevel = alert.TermLevel,
            MeddraSoc = alert.MeddraSoc,
            CaseCountA = alert.CaseCountA,
            ExpectedCountE = alert.ExpectedCountE,
            Prr = alert.Prr,
            PrrCiLower = alert.PrrCiLower,
            PrrCiUpper = alert.PrrCiUpper,
            ChiSquareYates = alert.ChiSquareYates,
            Ror = alert.Ror,
            RorCiLower = alert.RorCiLower,
            RorCiUpper = alert.RorCiUpper,
            Ebgm = alert.Ebgm,
            Eb05 = alert.Eb05,
            Eb95 = alert.Eb95,
            Severity = alert.Severity,
            Status = alert.Status,
            TriageSummary = alert.TriageSummary,
            AssignedSafetyOfficer = alert.AssignedSafetyOfficer,
            ReviewDecision = alert.ReviewDecision,
            ReviewJustification = alert.ReviewJustification,
            ClinicalSummary = alert.ClinicalSummary,
            ReviewedBy = alert.ReviewedBy,
            ReviewedAt = alert.ReviewedAt,
            EsignatureManifestId = alert.EsignatureManifestId,
            CreatedAt = alert.CreatedAt,
            CreatedBy = alert.CreatedBy,
            ReasonForChange = alert.ReasonForChange,
            VersionIndex = alert.VersionIndex
        };
    }
}
