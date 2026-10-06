using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Interop.Infrastructure.Background;

/// <summary>
/// In-process background hosted service for sliding window telemetry aggregation and real-time safety alerting.
/// @req:PRD-SYS-001
/// @req:PRD-DCT-001
/// @req:PRD-DCT-002
/// </summary>
public sealed class TelemetryWindowingBackgroundService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<TelemetryWindowingBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _processingInterval = TimeSpan.FromSeconds(15);
    private const int MaxBatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TelemetryWindowingBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteWindowingCycleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during telemetry windowing background processing cycle.");
            }

            try
            {
                await Task.Delay(_processingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("TelemetryWindowingBackgroundService stopped.");
    }

    public async Task<int> ExecuteWindowingCycleAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();

        return await ProcessWindowingAsync(db, clock, ct).ConfigureAwait(false);
    }

    public static async Task<int> ProcessWindowingAsync(InteropDbContext db, IClock clock, CancellationToken ct)
    {
        var streams = await db.SensorTelemetryStreamRecords
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Take(MaxBatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (streams.Count == 0)
        {
            return 0;
        }

        var groups = streams.GroupBy(s => new
        {
            s.SubjectId,
            s.StudyId,
            s.SiteId,
            s.DeviceType,
            s.DeviceTokenHash,
            SummaryDate = s.StartTimestamp.Date
        });

        int summariesProcessed = 0;

        foreach (var group in groups)
        {
            var key = group.Key;
            var summaryDate = new DateTimeOffset(key.SummaryDate, TimeSpan.Zero);

            var existingSummary = await db.SensorDailySummaryRecords
                .FirstOrDefaultAsync(s =>
                    s.SubjectId == key.SubjectId &&
                    s.StudyId == key.StudyId &&
                    s.DeviceType == key.DeviceType &&
                    s.DeviceTokenHash == key.DeviceTokenHash &&
                    s.SummaryDate == summaryDate, ct)
                .ConfigureAwait(false);

            var groupStreams = group.ToList();
            int totalReadings = groupStreams.Sum(s => s.ReadingCount);

            double activeHours = groupStreams
                .Select(s => (s.EndTimestamp - s.StartTimestamp).TotalHours)
                .Where(h => h > 0)
                .DefaultIfEmpty(1.0)
                .Sum();

            if (activeHours > 24.0) activeHours = 24.0;

            var allReadings = new List<RawSensorReadingDto>();
            foreach (var json in groupStreams.Select(s => s.OmhPayloadJson).Where(j => !string.IsNullOrWhiteSpace(j)))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<RawSensorReadingDto>>(json);
                    if (parsed != null && parsed.Count > 0)
                    {
                        allReadings.AddRange(parsed);
                    }
                }
                catch
                {
                    // Ignore malformed JSON payloads gracefully
                }
            }

            // Calculate metrics from parsed readings
            var glucoseVals = allReadings
                .Where(r => (r.MetricName ?? r.Metric ?? string.Empty).Trim().Equals("glucose", StringComparison.OrdinalIgnoreCase) ||
                            (r.MetricName ?? r.Metric ?? string.Empty).Trim().Equals("cgm", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Value)
                .ToList();

            double? meanGlucose = glucoseVals.Count > 0 ? glucoseVals.Average() : null;
            double? glucoseSd = null;
            double? glucoseCv = null;

            if (glucoseVals.Count > 1 && meanGlucose.HasValue)
            {
                var sumSqDiff = glucoseVals.Sum(v => Math.Pow(v - meanGlucose.Value, 2));
                glucoseSd = Math.Sqrt(sumSqDiff / (glucoseVals.Count - 1));
                if (meanGlucose.Value > 0)
                {
                    glucoseCv = (glucoseSd.Value / meanGlucose.Value) * 100.0;
                }
            }

            double? tirPct = glucoseVals.Count > 0
                ? (double)glucoseVals.Count(v => v >= 70 && v <= 180) / glucoseVals.Count * 100.0
                : null;
            double? tbr1Pct = glucoseVals.Count > 0
                ? (double)glucoseVals.Count(v => v >= 54 && v < 70) / glucoseVals.Count * 100.0
                : null;
            double? tbr2Pct = glucoseVals.Count > 0
                ? (double)glucoseVals.Count(v => v < 54) / glucoseVals.Count * 100.0
                : null;
            double? tar1Pct = glucoseVals.Count > 0
                ? (double)glucoseVals.Count(v => v > 180 && v <= 250) / glucoseVals.Count * 100.0
                : null;
            double? tar2Pct = glucoseVals.Count > 0
                ? (double)glucoseVals.Count(v => v > 250) / glucoseVals.Count * 100.0
                : null;

            var hrVals = allReadings
                .Where(r => (r.MetricName ?? r.Metric ?? string.Empty).Trim().Equals("heart_rate", StringComparison.OrdinalIgnoreCase) ||
                            (r.MetricName ?? r.Metric ?? string.Empty).Trim().Equals("hr", StringComparison.OrdinalIgnoreCase) ||
                            (r.MetricName ?? r.Metric ?? string.Empty).Trim().Equals("pulse", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Value)
                .ToList();

            double? restingHr = hrVals.Count > 0 ? hrVals.Average() : null;

            var sysBpVals = allReadings
                .Where(r => (r.MetricName ?? r.Metric ?? string.Empty).Trim().Equals("systolic_bp", StringComparison.OrdinalIgnoreCase) ||
                            (r.MetricName ?? r.Metric ?? string.Empty).Trim().Equals("sysbp", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Value)
                .ToList();

            double? meanSysBp = sysBpVals.Count > 0 ? sysBpVals.Average() : null;

            var diaBpVals = allReadings
                .Where(r => (r.MetricName ?? r.Metric ?? string.Empty).Trim().Equals("diastolic_bp", StringComparison.OrdinalIgnoreCase) ||
                            (r.MetricName ?? r.Metric ?? string.Empty).Trim().Equals("diabp", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Value)
                .ToList();

            double? meanDiaBp = diaBpVals.Count > 0 ? diaBpVals.Average() : null;

            var stepVals = allReadings
                .Where(r => (r.MetricName ?? r.Metric ?? string.Empty).Trim().Equals("step_count", StringComparison.OrdinalIgnoreCase) ||
                            (r.MetricName ?? r.Metric ?? string.Empty).Trim().Equals("steps", StringComparison.OrdinalIgnoreCase))
                .Select(r => (int)r.Value)
                .ToList();

            int? totalSteps = stepVals.Count > 0 ? stepVals.Sum() : null;

            var summaryContent = $"{key.SubjectId}:{key.StudyId}:{key.DeviceType}:{summaryDate:O}:{totalReadings}:{meanGlucose}:{restingHr}:{meanSysBp}:{totalSteps}";
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(summaryContent)));

            if (existingSummary != null)
            {
                existingSummary.ReadingsCount += totalReadings;
                existingSummary.ActiveWearHours = Math.Min(24.0, existingSummary.ActiveWearHours + activeHours);
                if (tirPct.HasValue) existingSummary.TirPercentage = tirPct;
                if (tbr1Pct.HasValue) existingSummary.TbrLevel1Percentage = tbr1Pct;
                if (tbr2Pct.HasValue) existingSummary.TbrLevel2Percentage = tbr2Pct;
                if (tar1Pct.HasValue) existingSummary.TarLevel1Percentage = tar1Pct;
                if (tar2Pct.HasValue) existingSummary.TarLevel2Percentage = tar2Pct;
                if (meanGlucose.HasValue) existingSummary.MeanGlucoseMgDl = meanGlucose;
                if (glucoseSd.HasValue) existingSummary.GlucoseSd = glucoseSd;
                if (glucoseCv.HasValue) existingSummary.GlucoseCvPercent = glucoseCv;
                if (restingHr.HasValue) existingSummary.RestingHeartRateBpm = restingHr;
                if (meanSysBp.HasValue) existingSummary.MeanSystolicMmhg = meanSysBp;
                if (meanDiaBp.HasValue) existingSummary.MeanDiastolicMmhg = meanDiaBp;
                if (totalSteps.HasValue) existingSummary.TotalSteps = (existingSummary.TotalSteps ?? 0) + totalSteps.Value;
                existingSummary.Sha256Hash = sha256;
            }
            else
            {
                var newSummary = new SensorDailySummaryRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    StudyId = key.StudyId,
                    SiteId = key.SiteId,
                    SubjectId = key.SubjectId,
                    SummaryDate = summaryDate,
                    DeviceType = key.DeviceType,
                    DeviceTokenHash = key.DeviceTokenHash,
                    ReadingsCount = totalReadings,
                    ActiveWearHours = Math.Min(24.0, activeHours),
                    TirPercentage = tirPct,
                    TbrLevel1Percentage = tbr1Pct,
                    TbrLevel2Percentage = tbr2Pct,
                    TarLevel1Percentage = tar1Pct,
                    TarLevel2Percentage = tar2Pct,
                    MeanGlucoseMgDl = meanGlucose,
                    GlucoseSd = glucoseSd,
                    GlucoseCvPercent = glucoseCv,
                    RestingHeartRateBpm = restingHr,
                    MeanSystolicMmhg = meanSysBp,
                    MeanDiastolicMmhg = meanDiaBp,
                    TotalSteps = totalSteps,
                    Sha256Hash = sha256,
                    CreatedAt = clock.UtcNow
                };

                db.SensorDailySummaryRecords.Add(newSummary);
            }

            summariesProcessed++;

            // Evaluate safety thresholds for any un-alerted readings
            var alerts = TelemetryEndpoints.EvaluateSafetyAlerts(
                key.StudyId,
                key.SiteId,
                key.SubjectId,
                key.DeviceType,
                key.DeviceTokenHash,
                allReadings,
                clock.UtcNow);

            foreach (var alert in alerts)
            {
                var existingAlert = await db.SensorSafetyAlertRecords
                    .AnyAsync(a => a.SubjectId == alert.SubjectId &&
                                   a.AlertType == alert.AlertType &&
                                   a.TriggeredAt == alert.TriggeredAt, ct)
                    .ConfigureAwait(false);

                if (!existingAlert)
                {
                    db.SensorSafetyAlertRecords.Add(alert);
                }
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return summariesProcessed;
    }
}
