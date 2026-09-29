using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.UnitTests.Execution.Fakes;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// `MIG-SUBJ-018` coverage for <see cref="SubjectApplicationService.CreateAsync"/>: SCN-001's sequential
/// enrollment-index/audit-trail assertions, the demographics-encryption-on-write assertion, the
/// <c>FromAggregate</c> response shape, and <see cref="SubjectCreateRequest"/>'s documented empty-string-<see cref="SiteCode"/> boundary.
/// Uses the in-memory <c>Cadence.Testing.Fakes</c> repositories rather than EF Core/SQLite — persistence
/// correctness for <see cref="Cadence.Apps.Execution.Domain.Ports.ISubjectRepository.EnrollNextIndexAsync"/>
/// itself is already covered by `SubjectRepositoryTests` (MIG-SUBJ-013); this suite exercises the
/// application-service orchestration layered on top of it.
/// @req:PRD-SUB-001
/// </summary>
public class SubjectApplicationServiceCreateTests
{
    private sealed record AuditCall(string EntityName, string RecordId, string Action, string UserId, string ChangeReason);

    private sealed class RecordingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditCall> Calls { get; } = new();

        public Task WriteAsync(string entityName, string recordId, string action, string userId, string changeReason,
            IReadOnlyDictionary<string, object?>? oldValues, IReadOnlyDictionary<string, object?>? newValues, CancellationToken ct)
        {
            Calls.Add(new AuditCall(entityName, recordId, action, userId, changeReason));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeCurrentAuditContextAccessor : ICurrentAuditContextAccessor
    {
        public string UserId { get; init; } = "test-user";

        public string ChangeReason { get; init; } = "test-reason";
    }

    /// <summary>
    /// Deliberately not a plain pass-through: a naive fake that returned
    /// <c>JsonSerializer.Serialize(demographics)</c> unchanged would let a bug that skips encryption
    /// entirely go undetected. This prefixes and Base64-encodes instead, so a test asserting the persisted
    /// value is not the raw plaintext actually proves something.
    /// </summary>
    private sealed class FakeDemographicsEncryptionService : IDemographicsEncryptionService
    {
        private const string Prefix = "FAKE-CIPHER:";

        public int EncryptCallCount { get; private set; }

        public string Encrypt(IReadOnlyDictionary<string, object?> demographics)
        {
            EncryptCallCount++;
            return Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(demographics));
        }

        public IReadOnlyDictionary<string, object?> Decrypt(string ciphertext) =>
            throw new NotSupportedException("Not exercised by these tests.");
    }

    private sealed class Harness
    {
        public InMemorySubjectRepository Subjects { get; } = new();

        public InMemoryConsentRepository Consents { get; } = new();

        public InMemorySiteComplianceRepository Compliance { get; } = new();

        public FakeDemographicsEncryptionService Encryption { get; } = new();

        public RecordingAuditLogWriter AuditLog { get; } = new();

        public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        public FakeCurrentAuditContextAccessor AuditContext { get; } = new();

        public SubjectApplicationService Service { get; }

        public Harness() =>
            Service = new SubjectApplicationService(Subjects, Consents, Compliance, Encryption, AuditLog, Clock, AuditContext);
    }

    private static SubjectCreateRequest CreateRequest(string subjectId, string studyId = "STUDY-001",
        string? siteId = null, DemographicsPayload? demographics = null) =>
        new(subjectId, studyId, siteId, ArmId: null, demographics);

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CreateAsync_GivenEmptyStudy_EnrollsSequentialSubjectsWithZeroBasedIndexAndScreeningStatus()
    {
        var harness = new Harness();

        var first = await harness.Service.CreateAsync(CreateRequest("SUBJ-001"), TestContext.Current.CancellationToken);
        var second = await harness.Service.CreateAsync(CreateRequest("SUBJ-002"), TestContext.Current.CancellationToken);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value!.Status.Should().Be("SCREENING");
        second.Value!.Status.Should().Be("SCREENING");

        var persistedFirst = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        var persistedSecond = await harness.Subjects.GetByCodeAsync("SUBJ-002", TestContext.Current.CancellationToken);

        persistedFirst.Should().NotBeNull();
        persistedFirst!.EnrollmentIndex.Should().Be(0);
        persistedFirst.Status.Should().Be(SubjectState.Screening);

        persistedSecond.Should().NotBeNull();
        persistedSecond!.EnrollmentIndex.Should().Be(1);
        persistedSecond.Status.Should().Be(SubjectState.Screening);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CreateAsync_GivenEachEnrollment_WritesEnrollSubjectAuditEntryForThatSubject()
    {
        var harness = new Harness();

        var first = await harness.Service.CreateAsync(CreateRequest("SUBJ-001"), TestContext.Current.CancellationToken);
        var second = await harness.Service.CreateAsync(CreateRequest("SUBJ-002"), TestContext.Current.CancellationToken);

        harness.AuditLog.Calls.Should().HaveCount(2);
        harness.AuditLog.Calls.Should().OnlyContain(c => c.Action == "ENROLL_SUBJECT" && c.EntityName == "ClinicalSubject");
        harness.AuditLog.Calls[0].RecordId.Should().Be(first.Value!.Id.ToString());
        harness.AuditLog.Calls[1].RecordId.Should().Be(second.Value!.Id.ToString());
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CreateAsync_GivenPopulatedDemographics_PersistsEncryptedCiphertextNeverRawPlaintext()
    {
        var harness = new Harness();
        var demographics = new DemographicsPayload("Jane Doe", "1990-01-01", "F", "White");

        var result = await harness.Service.CreateAsync(
            CreateRequest("SUBJ-001", demographics: demographics), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        string? ciphertext = result.Value!.EncryptedDemographics;
        ciphertext.Should().NotBeNullOrEmpty();
        ciphertext.Should().StartWith("FAKE-CIPHER:", "CreateAsync must route Demographics through IDemographicsEncryptionService.Encrypt");
        ciphertext.Should().NotContain("Jane Doe",
            "the persisted value must be genuinely encrypted, never the plaintext payload serialized directly");
        harness.Encryption.EncryptCallCount.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CreateAsync_GivenNoDemographics_EncryptedDemographicsIsNullAndEncryptionIsNeverInvoked()
    {
        var harness = new Harness();

        var result = await harness.Service.CreateAsync(CreateRequest("SUBJ-001"), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EncryptedDemographics.Should().BeNull();
        harness.Encryption.EncryptCallCount.Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CreateAsync_GivenSiteId_ReturnsFromAggregateWithSiteIdPopulated()
    {
        // The response is built from the persisted aggregate, so the requested site comes back.
        var harness = new Harness();

        var result = await harness.Service.CreateAsync(
            CreateRequest("SUBJ-001", siteId: "SITE-042"), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SiteId.Should().Be(new SiteCode("SITE-042"));
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CreateAsync_GivenExplicitlyEmptySiteId_ThrowsRatherThanTreatingItAsAbsent()
    {
        // SubjectCreateRequest's own doc comment (MIG-SUBJ-009) resolves the empty-string-as-absent
        // ambiguity at SiteCode's constructor, not by pre-filtering it to null here.
        var harness = new Harness();

        var act = () => harness.Service.CreateAsync(
            CreateRequest("SUBJ-001", siteId: string.Empty), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
