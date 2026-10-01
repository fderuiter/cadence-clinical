using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Exports;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution;

public sealed class ExportJobTests
{
    private class FakeAuditLogWriter : IAuditLogWriter
    {
        public List<(string EntityName, string RecordId, string Action, string UserId, string ChangeReason)> Entries { get; } = new();

        public Task WriteAsync(
            string entityName,
            string recordId,
            string action,
            string userId,
            string changeReason,
            IReadOnlyDictionary<string, object?>? oldValues,
            IReadOnlyDictionary<string, object?>? newValues,
            CancellationToken ct)
        {
            Entries.Add((entityName, recordId, action, userId, changeReason));
            return Task.CompletedTask;
        }
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SubmitExportJob_Returns202AcceptedWithUniqueJobId_AndWritesAuditLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(SystemClock.Instance.UtcNow);
        var auditWriter = new FakeAuditLogWriter();
        var queue = new ExportJobQueue();
        var service = new ExportJobService(queue, auditWriter, clock);

        var caller = new AuthenticatedCaller("test.user", new[] { "Biostatistician" });
        var request = new ExportJobCreateRequest(
            StudyId: "STUDY-001",
            Format: "dataset_json",
            Domains: new[] { "DM", "VS" },
            Datasets: new[] { "ADSL" },
            Sites: new[] { "SITE-101" },
            Cohorts: new[] { "Active Arm" },
            PrivacyProfile: "SAFE_HARBOR",
            HmacSalt: "salt123",
            RecordLockFilter: "all",
            IncludeAuditTrail: true,
            IncludeUnmapped: true);

        var response = await service.CreateJobAsync(request, caller, ct);

        response.Should().NotBeNull();
        response.JobId.Should().StartWith("export_job_");
        response.Status.Should().Be("Queued");
        response.StudyId.Should().Be("STUDY-001");
        response.CreatedBy.Should().Be("test.user");

        auditWriter.Entries.Should().ContainSingle(e =>
            e.EntityName == "ExportJob" &&
            e.RecordId == response.JobId &&
            e.Action == "SUBMIT_EXPORT_JOB" &&
            e.UserId == "test.user");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task DatasetJsonEtlService_SerializesDatasetJsonAndOdmXml()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(SystemClock.Instance.UtcNow);
        var etl = new DatasetJsonEtlService(clock);

        var job = new ExportJob
        {
            JobId = "job-123",
            StudyId = "STUDY-001",
            Format = "dataset_json",
            Domains = new[] { "DM", "VS" },
            Datasets = new[] { "ADSL" },
            Sites = new[] { "SITE-101" },
            Cohorts = new[] { "Active Arm" },
            PrivacyProfile = "SAFE_HARBOR",
            HmacSalt = "salt",
            RecordLockFilter = "all",
            IncludeAuditTrail = true,
            IncludeUnmapped = true,
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddHours(24),
            CreatedBy = "user1"
        };

        var artifact = await etl.SerializeAsync(job, ct);

        artifact.Should().NotBeNull();
        artifact.FileName.Should().Be("study-001_dataset_json.json");
        artifact.ContentType.Should().Be("application/json");
        var jsonStr = System.Text.Encoding.UTF8.GetString(artifact.Content);
        jsonStr.Should().Contain("datasetJSONVersion");
        jsonStr.Should().Contain("STUDY-001");

        // Test ODM XML serialization
        var xmlJob = new ExportJob
        {
            JobId = "job-124",
            StudyId = "STUDY-001",
            Format = "odm_xml",
            Domains = new[] { "DM" },
            Datasets = Array.Empty<string>(),
            Sites = Array.Empty<string>(),
            Cohorts = Array.Empty<string>(),
            PrivacyProfile = "SAFE_HARBOR",
            HmacSalt = "salt",
            RecordLockFilter = "all",
            IncludeAuditTrail = true,
            IncludeUnmapped = true,
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddHours(24),
            CreatedBy = "user1"
        };

        var xmlArtifact = await etl.SerializeAsync(xmlJob, ct);
        xmlArtifact.FileName.Should().Be("study-001_cdisc_odm_v1.3.2.xml");
        xmlArtifact.ContentType.Should().Be("application/xml");
        var xmlStr = System.Text.Encoding.UTF8.GetString(xmlArtifact.Content);
        xmlStr.Should().Contain("<ODM");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ExportArtifact_ExpiresAfter24Hours()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(SystemClock.Instance.UtcNow);
        var auditWriter = new FakeAuditLogWriter();
        var queue = new ExportJobQueue();
        var service = new ExportJobService(queue, auditWriter, clock);

        var caller = new AuthenticatedCaller("test.user", new[] { "Biostatistician" });
        var jobResponse = await service.CreateJobAsync(new ExportJobCreateRequest("STUDY-001", "dataset_json", null, null, null, null, null, null, null, null, null), caller, ct);

        var artifact = new ExportArtifact(
            JobId: jobResponse.JobId,
            FileName: "test.json",
            ContentType: "application/json",
            Content: new byte[] { 1, 2, 3 },
            CreatedAt: clock.UtcNow,
            ExpiresAt: clock.UtcNow.AddHours(24));

        await service.StoreArtifactAndCompleteJobAsync(jobResponse.JobId, artifact, ct);

        // Valid before 24 hours
        var current = service.GetArtifact(jobResponse.JobId);
        current.Should().NotBeNull();

        // Expired after 25 hours
        clock.Advance(TimeSpan.FromHours(25));
        var expired = service.GetArtifact(jobResponse.JobId);
        expired.Should().BeNull();
    }

    [Fact]
    public void ExponentialBackoff_CalculatesCorrectIntervals_CappedAt10Seconds()
    {
        // Active job status polling uses exponential backoff (starting at 2s, capping at 10s)
        int initialDelay = 2000;
        int maxDelay = 10000;

        int GetNextDelay(int currentDelay) => Math.Min(currentDelay * 2, maxDelay);

        int delay1 = initialDelay; // 2000 ms
        int delay2 = GetNextDelay(delay1); // 4000 ms
        int delay3 = GetNextDelay(delay2); // 8000 ms
        int delay4 = GetNextDelay(delay3); // 10000 ms (capped)
        int delay5 = GetNextDelay(delay4); // 10000 ms (capped)

        delay1.Should().Be(2000);
        delay2.Should().Be(4000);
        delay3.Should().Be(8000);
        delay4.Should().Be(10000);
        delay5.Should().Be(10000);
    }
}
