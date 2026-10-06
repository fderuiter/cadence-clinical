using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Background;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

public class SubjectBatchIngestionTests
{
    private sealed class InMemorySubjectBatchRepository : ISubjectBatchRepository
    {
        private readonly Dictionary<Guid, SubjectBatchImport> _storeById = new();
        private readonly Dictionary<string, SubjectBatchImport> _storeByBatchId = new();

        public Task AddAsync(SubjectBatchImport batch, CancellationToken cancellationToken)
        {
            _storeById[batch.Id] = batch;
            _storeByBatchId[batch.BatchId] = batch;
            return Task.CompletedTask;
        }

        public Task<SubjectBatchImport?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            _storeById.TryGetValue(id, out var batch);
            return Task.FromResult(batch);
        }

        public Task<SubjectBatchImport?> GetByBatchIdAsync(string batchId, CancellationToken cancellationToken)
        {
            _storeByBatchId.TryGetValue(batchId, out var batch);
            return Task.FromResult(batch);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public InMemorySubjectRepository Subjects { get; } = new();
        public InMemorySubjectBatchRepository Batches { get; } = new();
        public InMemorySiteComplianceRepository Compliance { get; } = new();
        public FakeDemographicsEncryptionService Encryption { get; } = new();
        public RecordingAuditLogWriter AuditLog { get; } = new();
        public FakeCurrentAuditContextAccessor AuditContext { get; } = new();
        public SubjectBatchQueue Queue { get; } = new();
        public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

        public SubjectBatchValidationService ValidationService { get; }
        public SubjectBatchApplicationService ApplicationService { get; }

        public Harness()
        {
            ValidationService = new SubjectBatchValidationService(Batches, Subjects, Compliance, Clock);
            ApplicationService = new SubjectBatchApplicationService(
                Batches,
                Subjects,
                Encryption,
                AuditLog,
                AuditContext,
                Queue,
                Clock);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task ParseCsv_WithValidRoster_ParsesAllFieldsCorrectly()
    {
        string csvContent = "subject_id,study_id,site_id,arm_id,name,birthdate,gender,race\n" +
                            "SUBJ-101,STUDY-001,SITE-01,ARM-A,\"Doe, Jane\",1990-05-15,Female,White\n" +
                            "SUBJ-102,STUDY-001,SITE-02,ARM-B,\"Smith, John\",1985-08-20,Male,Asian\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var rows = await SubjectCsvParser.ParseAsync(stream, CancellationToken.None);

        rows.Should().HaveCount(2);
        rows[0].SubjectId.Should().Be("SUBJ-101");
        rows[0].Name.Should().Be("Doe, Jane");
        rows[0].Birthdate.Should().Be("1990-05-15");
        rows[1].SubjectId.Should().Be("SUBJ-102");
        rows[1].Name.Should().Be("Smith, John");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task ParseCsv_ExceedingMaxRows_ThrowsInvalidOperationException()
    {
        var sb = new StringBuilder();
        sb.AppendLine("subject_id,study_id,site_id");
        for (int i = 1; i <= 1001; i++)
        {
            sb.AppendLine($"SUBJ-{i},STUDY-001,SITE-01");
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));

        var act = () => SubjectCsvParser.ParseAsync(stream, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeds maximum row limit of 1000*");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task UploadBatchAsync_ValidCsv_EnqueuesBatchAndReturnsPendingStatus()
    {
        var harness = new Harness();
        string csvContent = "subject_id,site_id,arm_id,name,birthdate,gender,race\n" +
                            "SUBJ-201,SITE-01,ARM-A,Alice,1992-01-01,Female,White\n" +
                            "SUBJ-202,SITE-01,ARM-A,Bob,1988-03-04,Male,Black\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var result = await harness.ApplicationService.UploadBatchAsync(
            "STUDY-001",
            stream,
            "roster.csv",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var uploadResponse = result.Value;
        uploadResponse.Should().NotBeNull();
        uploadResponse.TotalRows.Should().Be(2);
        uploadResponse.Status.Should().Be("Pending");

        var batchDetail = await harness.ApplicationService.GetBatchDetailAsync(uploadResponse.BatchId, CancellationToken.None);
        batchDetail.IsSuccess.Should().BeTrue();
        batchDetail.Value!.Rows.Should().HaveCount(2);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task ValidateBatchAsync_ValidAndInvalidRows_CorrectlyIdentifiesRowStatusAndErrors()
    {
        var harness = new Harness();

        // Activate SITE-01 for STUDY-001, leave SITE-02 inactive
        harness.Compliance.Seed(SiteComplianceCache.Create(new StudyCode("STUDY-001"), new SiteCode("SITE-01"), "SITE_ACTIVATION", isComplete: true, Array.Empty<string>()));

        // Seed existing subject SUBJ-EXISTS in STUDY-001
        var existingSubject = ClinicalSubject.Create(
            new SubjectCode("SUBJ-EXISTS"),
            new StudyCode("STUDY-001"),
            new SiteCode("SITE-01"),
            enrollmentIndex: 0,
            encryptedDemographics: null,
            stratFactors: null);
        await harness.Subjects.AddAsync(existingSubject, CancellationToken.None);

        string csvContent = "subject_id,site_id,arm_id,name,birthdate,gender,race\n" +
                            "SUBJ-301,SITE-01,ARM-A,Valid Subject,1995-10-10,Female,White\n" + // Valid
                            "SUBJ-EXISTS,SITE-01,ARM-A,Duplicate Code,1990-01-01,Male,White\n" + // Duplicate subject code in DB
                            "SUBJ-302,SITE-02,ARM-B,Inactive Site,1991-02-02,Female,Asian\n" + // Inactive site
                            "SUBJ-303,SITE-01,ARM-A,Bad Date,INVALID-DATE,Male,Other\n"; // Bad birthdate format

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var uploadResult = await harness.ApplicationService.UploadBatchAsync("STUDY-001", stream, "roster.csv", CancellationToken.None);
        uploadResult.IsSuccess.Should().BeTrue();

        var batch = await harness.Batches.GetByBatchIdAsync(uploadResult.Value!.BatchId, CancellationToken.None);
        batch.Should().NotBeNull();

        await harness.ValidationService.ValidateBatchAsync(batch!.Id, CancellationToken.None);

        var detail = await harness.ApplicationService.GetBatchDetailAsync(batch.BatchId, CancellationToken.None);
        detail.IsSuccess.Should().BeTrue();
        var detailValue = detail.Value!;

        detailValue.Status.Should().Be("Validated");
        detailValue.ValidRows.Should().Be(1);
        detailValue.InvalidRows.Should().Be(3);

        detailValue.Rows[0].Status.Should().Be("Valid");
        detailValue.Rows[1].Status.Should().Be("Invalid");
        detailValue.Rows[1].ErrorMessage.Should().Contain("already exists in study");
        detailValue.Rows[2].Status.Should().Be("Invalid");
        detailValue.Rows[2].ErrorMessage.Should().Contain("not active for study");
        detailValue.Rows[3].Status.Should().Be("Invalid");
        detailValue.Rows[3].ErrorMessage.Should().Contain("Invalid birthdate format");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CommitBatchAsync_PartialBatch_CommitsValidRowsWithSequentialIndicesAndAudit()
    {
        var harness = new Harness();
        harness.Compliance.Seed(SiteComplianceCache.Create(new StudyCode("STUDY-001"), new SiteCode("SITE-01"), "SITE_ACTIVATION", isComplete: true, Array.Empty<string>()));

        string csvContent = "subject_id,site_id,arm_id,name,birthdate,gender,race\n" +
                            "SUBJ-401,SITE-01,ARM-A,User 1,1990-01-01,Female,White\n" +
                            "SUBJ-402,SITE-01,ARM-A,User 2,1991-02-02,Male,White\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var uploadResult = await harness.ApplicationService.UploadBatchAsync("STUDY-001", stream, "roster.csv", CancellationToken.None);
        var batch = await harness.Batches.GetByBatchIdAsync(uploadResult.Value!.BatchId, CancellationToken.None);

        await harness.ValidationService.ValidateBatchAsync(batch!.Id, CancellationToken.None);

        var commitResult = await harness.ApplicationService.CommitBatchAsync(batch.BatchId, "Batch Import Reason", CancellationToken.None);
        commitResult.IsSuccess.Should().BeTrue();
        var commitResponse = commitResult.Value!;

        commitResponse.CommittedCount.Should().Be(2);
        commitResponse.SubjectCodes.Should().Contain("SUBJ-401");
        commitResponse.SubjectCodes.Should().Contain("SUBJ-402");

        // Verify Subjects created with sequential indices
        var s1 = await harness.Subjects.GetByCodeAsync("SUBJ-401", CancellationToken.None);
        s1.Should().NotBeNull();
        s1!.EnrollmentIndex.Should().Be(0);
        s1.EncryptedDemographics.Should().NotBeNull();

        var s2 = await harness.Subjects.GetByCodeAsync("SUBJ-402", CancellationToken.None);
        s2.Should().NotBeNull();
        s2!.EnrollmentIndex.Should().Be(1);

        // Verify Audit Log entries
        harness.AuditLog.Calls.Should().Contain(e => e.Action == "ENROLL_SUBJECT" && e.ChangeReason == "Batch Import Reason");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task ValidationPerformance_100Subjects_CompletesUnder5Seconds()
    {
        var harness = new Harness();
        harness.Compliance.Seed(SiteComplianceCache.Create(new StudyCode("STUDY-PERF"), new SiteCode("SITE-01"), "SITE_ACTIVATION", isComplete: true, Array.Empty<string>()));

        var sb = new StringBuilder();
        sb.AppendLine("subject_id,site_id,arm_id,name,birthdate,gender,race");
        for (int i = 1; i <= 100; i++)
        {
            sb.AppendLine($"SUBJ-PERF-{i:D3},SITE-01,ARM-A,Subject {i},1990-01-01,Female,White");
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        var uploadResult = await harness.ApplicationService.UploadBatchAsync("STUDY-PERF", stream, "roster_100.csv", CancellationToken.None);
        var batch = await harness.Batches.GetByBatchIdAsync(uploadResult.Value!.BatchId, CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await harness.ValidationService.ValidateBatchAsync(batch!.Id, CancellationToken.None);
        stopwatch.Stop();

        stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(5.0, "Validation of 100 subjects must complete in under 5 seconds.");

        var detail = await harness.ApplicationService.GetBatchDetailAsync(batch.BatchId, CancellationToken.None);
        detail.Value!.ValidRows.Should().Be(100);
    }
}
