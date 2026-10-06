using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Background;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Interop;

public sealed class TelemetryEndpointsAndBackgroundWorkerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DeviceTokenHasher _hasher = new(TestFieldProtection.CreateLookupHasher());
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    public TelemetryEndpointsAndBackgroundWorkerTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private InteropDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InteropDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new InteropDbContext(options);
    }

    [Fact]
    [GxPRequirement("PRD-DCT-001")]
    public async Task StreamEndpoint_IngestsBatch_VerifiesDeviceTokenHash_AndSavesStreamRecord()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateContext();
        var rawToken = "sensor-token-xyz-99";
        var expectedTokenHash = _hasher.Hash(rawToken);

        var request = new TelemetryStreamIngestRequest(
            study_id: "STUDY-101",
            site_id: "SITE-01",
            subject_id: "PAT-001",
            device_type: "CGM",
            vendor: "Dexcom",
            model_name: "G7",
            device_token: rawToken,
            raw_device_token: null,
            batch_id: "BATCH-001",
            clock_drift_offset_ms: 12.5,
            reading_count: 2,
            start_timestamp: _clock.UtcNow.AddMinutes(-10),
            end_timestamp: _clock.UtcNow,
            readings:
            [
                new RawSensorReadingDto(_clock.UtcNow.AddMinutes(-5), "glucose", null, 110, "mg/dL", "VALID"),
                new RawSensorReadingDto(_clock.UtcNow, "glucose", null, 120, "mg/dL", "VALID")
            ],
            omh_payload_json: null,
            fhir_observations_json: null
        );

        var httpContext = new DefaultHttpContext();

        var result = await TelemetryEndpoints.IngestTelemetryStreamAsync(
            request, db, _hasher, _clock, httpContext, ct);

        var okResult = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<TelemetryStreamIngestResponse>>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.Success.Should().BeTrue();
        okResult.Value.StreamRecord.DeviceTokenHash.Should().Be(expectedTokenHash);
        okResult.Value.StreamRecord.RawDataHash.Should().NotBeNullOrEmpty();

        var recordInDb = await db.SensorTelemetryStreamRecords.FirstOrDefaultAsync(s => s.SubjectId == "PAT-001", ct);
        recordInDb.Should().NotBeNull();
        recordInDb!.DeviceTokenHash.Should().Be(expectedTokenHash);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task StreamEndpoint_RejectsMissingDeviceToken_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateContext();

        var request = new TelemetryStreamIngestRequest(
            study_id: "STUDY-101",
            site_id: "SITE-01",
            subject_id: "PAT-001",
            device_type: "CGM",
            vendor: "Dexcom",
            model_name: "G7",
            device_token: null,
            raw_device_token: null,
            batch_id: "BATCH-002",
            clock_drift_offset_ms: 0,
            reading_count: 1,
            start_timestamp: _clock.UtcNow,
            end_timestamp: _clock.UtcNow,
            readings: [],
            omh_payload_json: null,
            fhir_observations_json: null
        );

        var httpContext = new DefaultHttpContext();

        var result = await TelemetryEndpoints.IngestTelemetryStreamAsync(
            request, db, _hasher, _clock, httpContext, ct);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
    }

    [Fact]
    [GxPRequirement("PRD-DCT-002")]
    public async Task StreamEndpoint_TriggersSafetyAlert_WhenThresholdBreached()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateContext();
        var rawToken = "sensor-token-hr-01";
        var expectedTokenHash = _hasher.Hash(rawToken);

        var request = new TelemetryStreamIngestRequest(
            study_id: "STUDY-101",
            site_id: "SITE-01",
            subject_id: "PAT-002",
            device_type: "ECG",
            vendor: "Apple",
            model_name: "Watch",
            device_token: rawToken,
            raw_device_token: null,
            batch_id: "BATCH-003",
            clock_drift_offset_ms: 0,
            reading_count: 1,
            start_timestamp: _clock.UtcNow,
            end_timestamp: _clock.UtcNow,
            readings:
            [
                new RawSensorReadingDto(_clock.UtcNow, "heart_rate", null, 145, "bpm", "VALID")
            ],
            omh_payload_json: null,
            fhir_observations_json: null
        );

        var httpContext = new DefaultHttpContext();

        var result = await TelemetryEndpoints.IngestTelemetryStreamAsync(
            request, db, _hasher, _clock, httpContext, ct);

        var createdResult = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<TelemetryStreamIngestResponse>>().Subject;
        createdResult.Value!.SafetyAlertsTriggered.Should().HaveCount(1);

        var alert = createdResult.Value.SafetyAlertsTriggered[0];
        alert.AlertType.Should().Be("EXTREME_TACHYCARDIA");
        alert.Severity.Should().Be("CRITICAL");
        alert.ObservedValue.Should().Be(145);
        alert.DeviceTokenHash.Should().Be(expectedTokenHash);

        var alertInDb = await db.SensorSafetyAlertRecords.FirstOrDefaultAsync(a => a.SubjectId == "PAT-002", ct);
        alertInDb.Should().NotBeNull();
        alertInDb!.AlertType.Should().Be("EXTREME_TACHYCARDIA");
    }

    [Fact]
    [GxPRequirement("PRD-DCT-001")]
    public async Task BackgroundWorker_AggregatesStreamsIntoDailySummary()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateContext();
        var rawToken = "sensor-token-summary-01";
        var tokenHash = _hasher.Hash(rawToken);

        var readings = new List<RawSensorReadingDto>
        {
            new(_clock.UtcNow.AddHours(-2), "glucose", null, 100, "mg/dL", "VALID"),
            new(_clock.UtcNow.AddHours(-1), "glucose", null, 150, "mg/dL", "VALID"),
            new(_clock.UtcNow.AddHours(-1), "heart_rate", null, 72, "bpm", "VALID")
        };

        var stream = new SensorTelemetryStreamRecord
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = "STUDY-101",
            SiteId = "SITE-01",
            SubjectId = "PAT-003",
            DeviceTokenHash = tokenHash,
            DeviceType = "CGM",
            BatchId = "BATCH-SUMMARY-1",
            ReceivedAt = _clock.UtcNow,
            ClockDriftOffsetMs = 0,
            ReadingCount = 3,
            StartTimestamp = _clock.UtcNow.AddHours(-2),
            EndTimestamp = _clock.UtcNow,
            OmhPayloadJson = System.Text.Json.JsonSerializer.Serialize(readings),
            FhirObservationsJson = "[]",
            RawDataHash = "sha256-dummy",
            CreatedAt = _clock.UtcNow
        };

        db.SensorTelemetryStreamRecords.Add(stream);
        await db.SaveChangesAsync(ct);

        var processedCount = await TelemetryWindowingBackgroundService.ProcessWindowingAsync(db, _clock, ct);
        processedCount.Should().BeGreaterThan(0);

        var summary = await db.SensorDailySummaryRecords.FirstOrDefaultAsync(s => s.SubjectId == "PAT-003", ct);
        summary.Should().NotBeNull();
        summary!.ReadingsCount.Should().Be(3);
        summary.MeanGlucoseMgDl.Should().Be(125.0);
        summary.TirPercentage.Should().Be(100.0);
        summary.RestingHeartRateBpm.Should().Be(72.0);
        summary.Sha256Hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-DCT-001")]
    public async Task SummaryEndpoint_ReturnsSummariesFilteredBySubject()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateContext();
        db.SensorDailySummaryRecords.Add(new SensorDailySummaryRecord
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = "STUDY-101",
            SiteId = "SITE-01",
            SubjectId = "PAT-010",
            SummaryDate = _clock.UtcNow.Date,
            DeviceType = "CGM",
            DeviceTokenHash = "hash-10",
            ReadingsCount = 50,
            ActiveWearHours = 20,
            TirPercentage = 85.0,
            MeanGlucoseMgDl = 115.0,
            Sha256Hash = "hash-abc",
            CreatedAt = _clock.UtcNow
        });

        db.SensorDailySummaryRecords.Add(new SensorDailySummaryRecord
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = "STUDY-101",
            SiteId = "SITE-01",
            SubjectId = "PAT-020",
            SummaryDate = _clock.UtcNow.Date,
            DeviceType = "CGM",
            DeviceTokenHash = "hash-20",
            ReadingsCount = 40,
            ActiveWearHours = 18,
            TirPercentage = 90.0,
            MeanGlucoseMgDl = 110.0,
            Sha256Hash = "hash-def",
            CreatedAt = _clock.UtcNow
        });

        await db.SaveChangesAsync(ct);

        var result = await TelemetryEndpoints.ListTelemetrySummariesAsync("PAT-010", null, null, null, null, null, db, ct);
        var okResult = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<TelemetrySummaryListResponse>>().Subject;

        okResult.Value!.Total.Should().Be(1);
        okResult.Value.Summaries[0].SubjectId.Should().Be("PAT-010");
    }
}
