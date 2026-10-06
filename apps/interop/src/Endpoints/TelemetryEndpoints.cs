using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Interop.Endpoints;

/// <summary>
/// REST Minimal API endpoints for continuous biometric telemetry ingestion and daily summary retrieval.
/// @req:PRD-SYS-001
/// @req:PRD-DCT-001
/// @req:PRD-DCT-002
/// </summary>
public static class TelemetryEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/interop/telemetry")
            .WithTags("Biometric Sensor Telemetry & Safety Alerting");

        group.MapPost("/stream", IngestTelemetryStreamAsync)
            .WithName("IngestTelemetryStream")
            .Produces<TelemetryStreamIngestResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/summary", ListTelemetrySummariesAsync)
            .WithName("ListTelemetrySummaries")
            .Produces<TelemetrySummaryListResponse>(StatusCodes.Status200OK);

        group.MapGet("/alerts", ListSafetyAlertsAsync)
            .WithName("ListSafetyAlerts")
            .Produces<SafetyAlertsListResponse>(StatusCodes.Status200OK);

        var sensorGroup = app.MapGroup("/api/v1/interop/sensors")
            .WithTags("Biometric Sensors & DCT");

        sensorGroup.MapPost("/ingest", IngestTelemetryStreamAsync)
            .WithName("IngestSensorsStream")
            .Produces<TelemetryStreamIngestResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        sensorGroup.MapGet("/summary", ListTelemetrySummariesAsync)
            .WithName("ListSensorsSummary")
            .Produces<TelemetrySummaryListResponse>(StatusCodes.Status200OK);

        return app;
    }

    public static async Task<IResult> IngestTelemetryStreamAsync(
        TelemetryStreamIngestRequest request,
        InteropDbContext db,
        DeviceTokenHasher deviceTokenHasher,
        IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.study_id) || string.IsNullOrWhiteSpace(request.subject_id))
        {
            return Results.Problem("study_id and subject_id are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var rawDeviceToken = request.device_token
            ?? request.raw_device_token
            ?? httpContext.Request.Headers["X-Device-Token"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(rawDeviceToken))
        {
            return Results.Problem("A valid device token is required for telemetry ingestion.", statusCode: StatusCodes.Status400BadRequest);
        }

        string deviceTokenHash;
        try
        {
            deviceTokenHash = deviceTokenHasher.Hash(rawDeviceToken);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Device token verification failed: {ex.Message}", statusCode: StatusCodes.Status400BadRequest);
        }

        var readingsList = request.readings ?? [];
        var readingCount = readingsList.Count > 0 ? readingsList.Count : request.reading_count;

        var omhPayload = !string.IsNullOrWhiteSpace(request.omh_payload_json)
            ? request.omh_payload_json
            : JsonSerializer.Serialize(readingsList);

        var fhirObs = !string.IsNullOrWhiteSpace(request.fhir_observations_json)
            ? request.fhir_observations_json
            : "[]";

        var payloadToHash = $"{request.study_id}:{request.subject_id}:{omhPayload}:{fhirObs}";
        var rawDataHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadToHash)));

        var now = clock.UtcNow;
        var startTs = request.start_timestamp ?? readingsList.Select(r => r.Timestamp).Where(t => t.HasValue).Select(t => t!.Value).DefaultIfEmpty(now).Min();
        var endTs = request.end_timestamp ?? readingsList.Select(r => r.Timestamp).Where(t => t.HasValue).Select(t => t!.Value).DefaultIfEmpty(now).Max();

        var streamRecord = new SensorTelemetryStreamRecord
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = request.study_id,
            SiteId = request.site_id,
            SubjectId = request.subject_id,
            DeviceTokenHash = deviceTokenHash,
            DeviceType = request.device_type ?? "CGM",
            BatchId = !string.IsNullOrWhiteSpace(request.batch_id) ? request.batch_id : Guid.NewGuid().ToString("N"),
            ReceivedAt = now,
            ClockDriftOffsetMs = request.clock_drift_offset_ms,
            ReadingCount = readingCount,
            StartTimestamp = startTs,
            EndTimestamp = endTs,
            OmhPayloadJson = omhPayload,
            FhirObservationsJson = fhirObs,
            RawDataHash = rawDataHash,
            FeedLakehouseRecordIdsJson = "[]",
            CreatedAt = now
        };

        db.SensorTelemetryStreamRecords.Add(streamRecord);

        var triggeredAlerts = EvaluateSafetyAlerts(
            request.study_id,
            request.site_id,
            request.subject_id,
            streamRecord.DeviceType,
            deviceTokenHash,
            readingsList,
            now);

        foreach (var alert in triggeredAlerts)
        {
            db.SensorSafetyAlertRecords.Add(alert);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Created($"/api/v1/interop/telemetry/stream/{streamRecord.Id}", new TelemetryStreamIngestResponse(
            Success: true,
            StreamRecord: streamRecord,
            SafetyAlertsTriggered: triggeredAlerts,
            Message: $"Successfully ingested telemetry stream batch '{streamRecord.BatchId}' with {readingCount} reading(s)."));
    }

    public static async Task<IResult> ListTelemetrySummariesAsync(
        string? subject_id,
        string? subjectId,
        string? study_id,
        string? studyId,
        string? site_id,
        string? siteId,
        InteropDbContext db,
        CancellationToken ct)
    {
        var targetSubject = !string.IsNullOrWhiteSpace(subject_id) ? subject_id : subjectId;
        var targetStudy = !string.IsNullOrWhiteSpace(study_id) ? study_id : studyId;
        var targetSite = !string.IsNullOrWhiteSpace(site_id) ? site_id : siteId;

        var query = db.SensorDailySummaryRecords.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(targetSubject))
        {
            query = query.Where(s => s.SubjectId == targetSubject);
        }
        if (!string.IsNullOrWhiteSpace(targetStudy))
        {
            query = query.Where(s => s.StudyId == targetStudy);
        }
        if (!string.IsNullOrWhiteSpace(targetSite))
        {
            query = query.Where(s => s.SiteId == targetSite);
        }

        var list = await query.ToListAsync(ct).ConfigureAwait(false);
        var summaries = list.OrderByDescending(s => s.SummaryDate).ToList();
        return Results.Ok(new TelemetrySummaryListResponse(summaries, summaries.Count));
    }

    public static async Task<IResult> ListSafetyAlertsAsync(
        string? subject_id,
        string? study_id,
        bool unacknowledged_only,
        InteropDbContext db,
        CancellationToken ct)
    {
        var query = db.SensorSafetyAlertRecords.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(subject_id))
        {
            query = query.Where(a => a.SubjectId == subject_id);
        }
        if (!string.IsNullOrWhiteSpace(study_id))
        {
            query = query.Where(a => a.StudyId == study_id);
        }
        if (unacknowledged_only)
        {
            query = query.Where(a => !a.IsAcknowledged);
        }

        var list = await query.ToListAsync(ct).ConfigureAwait(false);
        var alerts = list.OrderByDescending(a => a.TriggeredAt).ToList();
        return Results.Ok(new SafetyAlertsListResponse(alerts, alerts.Count));
    }

    public static List<SensorSafetyAlertRecord> EvaluateSafetyAlerts(
        string studyId,
        string? siteId,
        string subjectId,
        string deviceType,
        string deviceTokenHash,
        List<RawSensorReadingDto> readings,
        DateTimeOffset now)
    {
        var alerts = new List<SensorSafetyAlertRecord>();

        foreach (var r in readings)
        {
            var metric = (r.MetricName ?? r.Metric ?? string.Empty).Trim().ToLowerInvariant();
            var val = r.Value;
            var ts = r.Timestamp ?? now;

            if (metric is "heart_rate" or "hr" or "pulse")
            {
                if (val > 130)
                {
                    alerts.Add(CreateAlert(studyId, siteId, subjectId, deviceType, deviceTokenHash, "EXTREME_TACHYCARDIA", "CRITICAL", "HEART_RATE", val, 130, "bpm", ts, now));
                }
                else if (val < 40)
                {
                    alerts.Add(CreateAlert(studyId, siteId, subjectId, deviceType, deviceTokenHash, "SEVERE_BRADYCARDIA", "CRITICAL", "HEART_RATE", val, 40, "bpm", ts, now));
                }
            }
            else if (metric is "spo2" or "oxygen_saturation")
            {
                if (val < 88)
                {
                    alerts.Add(CreateAlert(studyId, siteId, subjectId, deviceType, deviceTokenHash, "HYPOXIA_EVENT", "CRITICAL", "SPO2", val, 88, "%", ts, now));
                }
            }
            else if (metric is "glucose" or "cgm")
            {
                if (val < 54)
                {
                    alerts.Add(CreateAlert(studyId, siteId, subjectId, deviceType, deviceTokenHash, "SEVERE_HYPOGLYCEMIA", "CRITICAL", "GLUCOSE", val, 54, "mg/dL", ts, now));
                }
                else if (val > 250)
                {
                    alerts.Add(CreateAlert(studyId, siteId, subjectId, deviceType, deviceTokenHash, "SEVERE_HYPERGLYCEMIA", "HIGH", "GLUCOSE", val, 250, "mg/dL", ts, now));
                }
            }
            else if (metric is "systolic_bp" or "sysbp")
            {
                if (val > 180)
                {
                    alerts.Add(CreateAlert(studyId, siteId, subjectId, deviceType, deviceTokenHash, "HYPERTENSIVE_CRISIS", "CRITICAL", "SYSBP", val, 180, "mmHg", ts, now));
                }
                else if (val < 90)
                {
                    alerts.Add(CreateAlert(studyId, siteId, subjectId, deviceType, deviceTokenHash, "SEVERE_HYPOTENSION", "HIGH", "SYSBP", val, 90, "mmHg", ts, now));
                }
            }
        }

        return alerts;
    }

    private static SensorSafetyAlertRecord CreateAlert(
        string studyId,
        string? siteId,
        string subjectId,
        string deviceType,
        string deviceTokenHash,
        string alertType,
        string severity,
        string parameterName,
        double observedValue,
        double thresholdValue,
        string unit,
        DateTimeOffset triggeredAt,
        DateTimeOffset createdAt)
    {
        return new SensorSafetyAlertRecord
        {
            Id = Guid.NewGuid().ToString(),
            AlertId = $"ALT-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            StudyId = studyId,
            SiteId = siteId,
            SubjectId = subjectId,
            DeviceType = deviceType,
            DeviceTokenHash = deviceTokenHash,
            AlertType = alertType,
            Severity = severity,
            ParameterName = parameterName,
            ObservedValue = observedValue,
            ThresholdValue = thresholdValue,
            Unit = unit,
            TriggeredAt = triggeredAt,
            IsAcknowledged = false,
            CreatedAt = createdAt
        };
    }
}

public record RawSensorReadingDto(
    [property: JsonPropertyName("timestamp")] DateTimeOffset? Timestamp,
    [property: JsonPropertyName("metric_name")] string? MetricName,
    [property: JsonPropertyName("metric")] string? Metric,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("flag")] string? Flag
);

public record TelemetryStreamIngestRequest(
    [property: JsonPropertyName("study_id")] string study_id,
    [property: JsonPropertyName("site_id")] string? site_id,
    [property: JsonPropertyName("subject_id")] string subject_id,
    [property: JsonPropertyName("device_type")] string? device_type,
    [property: JsonPropertyName("vendor")] string? vendor,
    [property: JsonPropertyName("model_name")] string? model_name,
    [property: JsonPropertyName("device_token")] string? device_token,
    [property: JsonPropertyName("raw_device_token")] string? raw_device_token,
    [property: JsonPropertyName("batch_id")] string? batch_id,
    [property: JsonPropertyName("clock_drift_offset_ms")] double clock_drift_offset_ms,
    [property: JsonPropertyName("reading_count")] int reading_count,
    [property: JsonPropertyName("start_timestamp")] DateTimeOffset? start_timestamp,
    [property: JsonPropertyName("end_timestamp")] DateTimeOffset? end_timestamp,
    [property: JsonPropertyName("readings")] List<RawSensorReadingDto>? readings,
    [property: JsonPropertyName("omh_payload_json")] string? omh_payload_json,
    [property: JsonPropertyName("fhir_observations_json")] string? fhir_observations_json
);

public record TelemetryStreamIngestResponse(
    bool Success,
    SensorTelemetryStreamRecord StreamRecord,
    List<SensorSafetyAlertRecord> SafetyAlertsTriggered,
    string Message
);

public record TelemetrySummaryListResponse(
    List<SensorDailySummaryRecord> Summaries,
    int Total
);

public record SafetyAlertsListResponse(
    List<SensorSafetyAlertRecord> Alerts,
    int Total
);
