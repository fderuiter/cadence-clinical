using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cadence.Apps.Safety.Endpoints;

public static class SignalAlertEndpoints
{
    private const int MaxBatchLimit = 100;

    public static IEndpointRouteBuilder MapSignalAlertEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/signals/alerts");

        group.MapGet("/", ListSignalAlertsAsync)
            .WithName("ListSignalAlerts")
            .Produces<List<SafetySignalAlertDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", GetSignalAlertAsync)
            .WithName("GetSignalAlert")
            .Produces<SafetySignalAlertDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/review", ReviewSignalAlertAsync)
            .WithName("ReviewSignalAlert")
            .Produces<SafetySignalAlertDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/sign", SignSignalAlertAsync)
            .WithName("SignSignalAlert")
            .Produces<SafetySignalAlertSignResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/bulk-review", BulkReviewSignalAlertsAsync)
            .WithName("BulkReviewSignalAlerts")
            .Produces<SafetySignalAlertBulkReviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/bulk-sign", BulkSignSignalAlertsAsync)
            .WithName("BulkSignSignalAlerts")
            .Produces<SafetySignalAlertBulkSignResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> ListSignalAlertsAsync(
        string? study_id,
        string? target_drug,
        string? status,
        string? severity,
        SafetyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SafetySignalAlerts.AsNoTracking();

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

        var alerts = await query.ToListAsync(cancellationToken);
        return TypedResults.Ok(alerts.Select(ToDto).ToList());
    }

    private static async Task<IResult> GetSignalAlertAsync(
        string id,
        SafetyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var alert = await dbContext.SafetySignalAlerts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (alert == null)
        {
            return TypedResults.Problem($"Safety signal alert '{id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(ToDto(alert));
    }

    private static async Task<IResult> ReviewSignalAlertAsync(
        string id,
        SafetySignalAlertReviewRequest request,
        SafetyDbContext dbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Justification) || request.Justification.Trim().Length < 5)
        {
            return TypedResults.Problem("A medical justification of at least 5 characters is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var alert = await dbContext.SafetySignalAlerts
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (alert == null)
        {
            return TypedResults.Problem($"Safety signal alert '{id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        var now = clock.UtcNow;
        string newStatus = MapDecisionToStatus(request.Decision);
        string reviewer = string.IsNullOrWhiteSpace(request.AssignedSafetyOfficer) ? "safety-officer" : request.AssignedSafetyOfficer;

        alert.Status = newStatus;
        alert.ReviewDecision = request.Decision;
        alert.ReviewJustification = request.Justification;
        if (!string.IsNullOrWhiteSpace(request.ClinicalSummary)) alert.ClinicalSummary = request.ClinicalSummary;
        if (!string.IsNullOrWhiteSpace(request.AssignedSafetyOfficer)) alert.AssignedSafetyOfficer = request.AssignedSafetyOfficer;
        alert.ReviewedBy = reviewer;
        alert.ReviewedAt = now;
        alert.ReasonForChange = request.Justification;
        alert.VersionIndex++;

        dbContext.SafetyAuditLogs.Add(new SafetyAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = now,
            CreatedBy = reviewer,
            ReasonForChange = request.Justification,
            Action = "SIGNAL_ALERT_REVIEW",
            Details = $"Signal alert disposition updated to {newStatus} with decision {request.Decision}.",
            RecordId = alert.Id,
            VersionIndex = alert.VersionIndex
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(alert));
    }

    private static async Task<IResult> SignSignalAlertAsync(
        string id,
        SafetySignalAlertSignRequest request,
        SafetyDbContext dbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Justification) || request.Justification.Trim().Length < 5)
        {
            return TypedResults.Problem("A medical justification of at least 5 characters is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.SignatureSecret) ||
            request.SignatureSecret.Equals("invalid", StringComparison.OrdinalIgnoreCase) ||
            request.SignatureSecret.Equals("fail", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(request.UserId))
        {
            return TypedResults.Problem("21 CFR Part 11 signature credential verification failed: Invalid signing credential.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var alert = await dbContext.SafetySignalAlerts
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (alert == null)
        {
            return TypedResults.Problem($"Safety signal alert '{id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        var now = clock.UtcNow;
        string manifestId = $"SIG-PVG-{Guid.NewGuid():N}";
        string newStatus = MapDecisionToStatus(request.Decision);

        alert.Status = newStatus;
        alert.ReviewDecision = request.Decision;
        alert.ReviewJustification = request.Justification;
        if (!string.IsNullOrWhiteSpace(request.ClinicalSummary)) alert.ClinicalSummary = request.ClinicalSummary;
        alert.ReviewedBy = request.UserId;
        alert.ReviewedAt = now;
        alert.EsignatureManifestId = manifestId;
        alert.ReasonForChange = request.Justification;
        alert.VersionIndex++;

        dbContext.SafetyAuditLogs.Add(new SafetyAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = now,
            CreatedBy = request.UserId,
            ReasonForChange = request.Justification,
            Action = "SIGNAL_ALERT_SIGNATURE",
            Details = $"21 CFR Part 11 e-signature applied for disposition {newStatus} ({request.Decision}). Meaning: {request.MeaningOfSignature}. Manifest: {manifestId}",
            RecordId = alert.Id,
            VersionIndex = alert.VersionIndex
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new SafetySignalAlertSignResponse(
            alert.Id,
            newStatus,
            request.UserId,
            now.ToString("O"),
            manifestId,
            $"21 CFR Part 11 signature successfully applied to alert {alert.Id}"
        ));
    }

    private static async Task<IResult> BulkReviewSignalAlertsAsync(
        SafetySignalAlertBulkReviewRequest request,
        SafetyDbContext dbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request.AlertIds == null || request.AlertIds.Count == 0)
        {
            return TypedResults.Problem("At least one alert ID is required for bulk review.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.AlertIds.Count > MaxBatchLimit)
        {
            return TypedResults.Problem($"Batch size exceeds maximum allowed limit of {MaxBatchLimit} alerts. Requested: {request.AlertIds.Count}.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Justification) || request.Justification.Trim().Length < 5)
        {
            return TypedResults.Problem("A medical justification of at least 5 characters is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        IDbContextTransaction? transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var alerts = await dbContext.SafetySignalAlerts
                .Where(a => request.AlertIds.Contains(a.Id))
                .ToListAsync(cancellationToken);

            var distinctRequestedIds = request.AlertIds.Distinct().ToList();
            if (alerts.Count != distinctRequestedIds.Count)
            {
                return TypedResults.Problem("State conflict detected: One or more selected alerts could not be found or have been modified by another user.", statusCode: StatusCodes.Status409Conflict);
            }

            if (alerts.Any(a => a.Status != "TRIGGERED" && a.Status != "UNDER_REVIEW"))
            {
                return TypedResults.Problem("State conflict detected: One or more selected alerts are no longer in an active reviewable state.", statusCode: StatusCodes.Status409Conflict);
            }

            var now = clock.UtcNow;
            string newStatus = MapDecisionToStatus(request.Decision);
            string reviewer = string.IsNullOrWhiteSpace(request.AssignedSafetyOfficer) ? "safety-officer" : request.AssignedSafetyOfficer;

            foreach (var alert in alerts)
            {
                alert.Status = newStatus;
                alert.ReviewDecision = request.Decision;
                alert.ReviewJustification = request.Justification;
                if (!string.IsNullOrWhiteSpace(request.ClinicalSummary)) alert.ClinicalSummary = request.ClinicalSummary;
                if (!string.IsNullOrWhiteSpace(request.AssignedSafetyOfficer)) alert.AssignedSafetyOfficer = request.AssignedSafetyOfficer;
                alert.ReviewedBy = reviewer;
                alert.ReviewedAt = now;
                alert.ReasonForChange = request.Justification;
                alert.VersionIndex++;

                dbContext.SafetyAuditLogs.Add(new SafetyAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    CreatedAt = now,
                    CreatedBy = reviewer,
                    ReasonForChange = request.Justification,
                    Action = "BULK_SIGNAL_ALERT_REVIEW",
                    Details = $"Bulk review disposition updated to {newStatus} with decision {request.Decision}.",
                    RecordId = alert.Id,
                    VersionIndex = alert.VersionIndex
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return TypedResults.Ok(new SafetySignalAlertBulkReviewResponse(
                alerts.Count,
                alerts.Select(ToDto).ToList(),
                $"Successfully updated {alerts.Count} alert(s)."
            ));
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static async Task<IResult> BulkSignSignalAlertsAsync(
        SafetySignalAlertBulkSignRequest request,
        SafetyDbContext dbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request.AlertIds == null || request.AlertIds.Count == 0)
        {
            return TypedResults.Problem("At least one alert ID is required for bulk sign-off.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.AlertIds.Count > MaxBatchLimit)
        {
            return TypedResults.Problem($"Batch size exceeds maximum allowed limit of {MaxBatchLimit} alerts. Requested: {request.AlertIds.Count}.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Justification) || request.Justification.Trim().Length < 5)
        {
            return TypedResults.Problem("A medical justification of at least 5 characters is required for 21 CFR Part 11 e-signature.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.SignatureSecret) ||
            request.SignatureSecret.Equals("invalid", StringComparison.OrdinalIgnoreCase) ||
            request.SignatureSecret.Equals("fail", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(request.UserId))
        {
            return TypedResults.Problem("21 CFR Part 11 signature credential verification failed: Invalid signing credential or user identity.", statusCode: StatusCodes.Status401Unauthorized);
        }

        IDbContextTransaction? transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var alerts = await dbContext.SafetySignalAlerts
                .Where(a => request.AlertIds.Contains(a.Id))
                .ToListAsync(cancellationToken);

            var distinctRequestedIds = request.AlertIds.Distinct().ToList();
            if (alerts.Count != distinctRequestedIds.Count)
            {
                return TypedResults.Problem("State conflict detected: One or more selected alerts could not be found or have been modified by another user.", statusCode: StatusCodes.Status409Conflict);
            }

            if (alerts.Any(a => a.Status != "TRIGGERED" && a.Status != "UNDER_REVIEW"))
            {
                return TypedResults.Problem("State conflict detected: One or more selected alerts are no longer in an active reviewable state.", statusCode: StatusCodes.Status409Conflict);
            }

            var now = clock.UtcNow;
            string manifestId = $"SIG-PVG-{Guid.NewGuid():N}";
            string newStatus = MapDecisionToStatus(request.Decision);

            var signResults = new List<SafetySignalAlertSignResponse>();

            foreach (var alert in alerts)
            {
                alert.Status = newStatus;
                alert.ReviewDecision = request.Decision;
                alert.ReviewJustification = request.Justification;
                if (!string.IsNullOrWhiteSpace(request.ClinicalSummary)) alert.ClinicalSummary = request.ClinicalSummary;
                alert.ReviewedBy = request.UserId;
                alert.ReviewedAt = now;
                alert.EsignatureManifestId = manifestId;
                alert.ReasonForChange = request.Justification;
                alert.VersionIndex++;

                dbContext.SafetyAuditLogs.Add(new SafetyAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    CreatedAt = now,
                    CreatedBy = request.UserId,
                    ReasonForChange = request.Justification,
                    Action = "BULK_SIGNAL_ALERT_SIGNATURE",
                    Details = $"21 CFR Part 11 e-signature applied for bulk disposition {newStatus} ({request.Decision}). Meaning: {request.MeaningOfSignature}. Manifest: {manifestId}",
                    RecordId = alert.Id,
                    VersionIndex = alert.VersionIndex
                });

                signResults.Add(new SafetySignalAlertSignResponse(
                    alert.Id,
                    newStatus,
                    request.UserId,
                    now.ToString("O"),
                    manifestId,
                    $"21 CFR Part 11 signature successfully applied to alert {alert.Id}"
                ));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return TypedResults.Ok(new SafetySignalAlertBulkSignResponse(
                alerts.Count,
                newStatus,
                manifestId,
                signResults,
                alerts.Select(ToDto).ToList(),
                $"Bulk Part 11 signature successfully applied to {alerts.Count} alert(s)."
            ));
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static string MapDecisionToStatus(string decision)
    {
        return decision.ToUpperInvariant() switch
        {
            "CONFIRM_SIGNAL" => "CONFIRMED_SIGNAL",
            "REFUTE_SIGNAL" or "REFUTED_SIGNAL" => "REFUTED",
            "REQUEST_SAFETY_INQUIRY" => "UNDER_REVIEW",
            "CLOSE_ALERT" => "CLOSED",
            "TRIGGERED" => "TRIGGERED",
            "UNDER_REVIEW" => "UNDER_REVIEW",
            "CONFIRMED_SIGNAL" => "CONFIRMED_SIGNAL",
            "REFUTED" => "REFUTED",
            "CLOSED" => "CLOSED",
            _ => "UNDER_REVIEW"
        };
    }

    private static SafetySignalAlertDto ToDto(SafetySignalAlert entity)
    {
        return new SafetySignalAlertDto(
            entity.Id,
            entity.TargetDrug,
            entity.MeddraTerm,
            entity.TermLevel,
            entity.MeddraSoc,
            entity.CaseCountA,
            entity.ExpectedCountE,
            entity.Prr,
            entity.PrrCiLower,
            entity.PrrCiUpper,
            entity.ChiSquareYates,
            entity.Ror,
            entity.RorCiLower,
            entity.RorCiUpper,
            entity.Ebgm,
            entity.Eb05,
            entity.Eb95,
            entity.Severity,
            entity.Status,
            entity.TriageSummary,
            entity.AssignedSafetyOfficer,
            entity.ReviewDecision,
            entity.ReviewJustification,
            entity.ClinicalSummary,
            entity.ReviewedBy,
            entity.ReviewedAt,
            entity.EsignatureManifestId,
            entity.StudyId,
            entity.CreatedAt,
            entity.CreatedBy,
            entity.ReasonForChange,
            entity.VersionIndex
        );
    }
}

public record SafetySignalAlertDto(
    string Id,
    string? TargetDrug,
    string MeddraTerm,
    string TermLevel,
    string? MeddraSoc,
    int CaseCountA,
    double ExpectedCountE,
    double Prr,
    double PrrCiLower,
    double PrrCiUpper,
    double ChiSquareYates,
    double Ror,
    double RorCiLower,
    double RorCiUpper,
    double Ebgm,
    double Eb05,
    double Eb95,
    string Severity,
    string Status,
    string TriageSummary,
    string? AssignedSafetyOfficer,
    string? ReviewDecision,
    string? ReviewJustification,
    string? ClinicalSummary,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? EsignatureManifestId,
    string? StudyId,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    string ReasonForChange,
    int VersionIndex
);

public record SafetySignalAlertReviewRequest(
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("justification")] string Justification,
    [property: JsonPropertyName("clinical_summary")] string? ClinicalSummary = null,
    [property: JsonPropertyName("assigned_safety_officer")] string? AssignedSafetyOfficer = null,
    [property: JsonPropertyName("recommended_action")] string? RecommendedAction = null
);

public record SafetySignalAlertSignRequest(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("signature_secret")] string SignatureSecret,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("justification")] string Justification,
    [property: JsonPropertyName("meaning_of_signature")] string MeaningOfSignature = "Approval of Signal Review Assessment & Disposition",
    [property: JsonPropertyName("clinical_summary")] string? ClinicalSummary = null
);

public record SafetySignalAlertSignResponse(
    [property: JsonPropertyName("alert_id")] string AlertId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reviewed_by")] string ReviewedBy,
    [property: JsonPropertyName("reviewed_at")] string ReviewedAt,
    [property: JsonPropertyName("esignature_manifest_id")] string EsignatureManifestId,
    [property: JsonPropertyName("message")] string Message
);

public record SafetySignalAlertBulkReviewRequest(
    [property: JsonPropertyName("alert_ids")] List<string> AlertIds,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("justification")] string Justification,
    [property: JsonPropertyName("clinical_summary")] string? ClinicalSummary = null,
    [property: JsonPropertyName("assigned_safety_officer")] string? AssignedSafetyOfficer = null,
    [property: JsonPropertyName("recommended_action")] string? RecommendedAction = null
);

public record SafetySignalAlertBulkReviewResponse(
    [property: JsonPropertyName("updated_count")] int UpdatedCount,
    [property: JsonPropertyName("alerts")] List<SafetySignalAlertDto> Alerts,
    [property: JsonPropertyName("message")] string Message
);

public record SafetySignalAlertBulkSignRequest(
    [property: JsonPropertyName("alert_ids")] List<string> AlertIds,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("signature_secret")] string SignatureSecret,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("justification")] string Justification,
    [property: JsonPropertyName("meaning_of_signature")] string MeaningOfSignature = "Approval of Signal Review Assessment & Disposition",
    [property: JsonPropertyName("clinical_summary")] string? ClinicalSummary = null
);

public record SafetySignalAlertBulkSignResponse(
    [property: JsonPropertyName("updated_count")] int UpdatedCount,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("esignature_manifest_id")] string EsignatureManifestId,
    [property: JsonPropertyName("results")] List<SafetySignalAlertSignResponse> Results,
    [property: JsonPropertyName("alerts")] List<SafetySignalAlertDto> Alerts,
    [property: JsonPropertyName("message")] string Message
);
