using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

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
/// </summary>
public class SubjectApplicationServiceCreateTests
{
    private sealed class Harness
    {
        public InMemorySubjectRepository Subjects { get; } = new();

        public InMemoryConsentRepository Consents { get; } = new();

        public InMemorySiteComplianceRepository Compliance { get; } = new();

        public FakeDemographicsEncryptionService Encryption { get; } = new();

        public RecordingAuditLogWriter AuditLog { get; } = new();

        public FakeCurrentAuditContextAccessor AuditContext { get; } = new();

        public SubjectApplicationService Service { get; }

        public Harness() =>
            Service = new SubjectApplicationService(Subjects, Consents, Compliance, Encryption, AuditLog, AuditContext);
    }

    private static SubjectCreateRequest CreateRequest(string subjectId, string studyId = "STUDY-001",
        string? siteId = null, DemographicsPayload? demographics = null) =>
        new(subjectId, studyId, siteId, demographics);

    [Fact]
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
    public async Task CreateAsync_GivenNoDemographics_EncryptedDemographicsIsNullAndEncryptionIsNeverInvoked()
    {
        var harness = new Harness();

        var result = await harness.Service.CreateAsync(CreateRequest("SUBJ-001"), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EncryptedDemographics.Should().BeNull();
        harness.Encryption.EncryptCallCount.Should().Be(0);
    }

    [Fact]
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
    public async Task CreateAsync_GivenExplicitlyEmptySiteId_ThrowsRatherThanTreatingItAsAbsent()
    {
        // SubjectCreateRequest's own doc comment (MIG-SUBJ-009) resolves the empty-string-as-absent
        // ambiguity at SiteCode's constructor, not by pre-filtering it to null here.
        var harness = new Harness();

        var act = () => harness.Service.CreateAsync(
            CreateRequest("SUBJ-001", siteId: string.Empty), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_GivenKernelEnrollmentSynchronizerAndSite_ScreensParticipantBeforePersistingSubject()
    {
        var expectedEnrollmentId = Guid.Parse("0192f3c4-5e6a-7b8c-9d0e-00000000e909");
        var synchronizer = new RecordingKernelEnrollmentSynchronizer
        {
            NextResult = Cadence.Core.Domain.Result<Guid>.Success(expectedEnrollmentId),
        };
        var subjects = new InMemorySubjectRepository();
        var service = new SubjectApplicationService(
            subjects,
            new InMemoryConsentRepository(),
            new InMemorySiteComplianceRepository(),
            new FakeDemographicsEncryptionService(),
            new RecordingAuditLogWriter(),
            new FakeCurrentAuditContextAccessor(),
            kernelEnrollmentSynchronizer: synchronizer);

        var ct = TestContext.Current.CancellationToken;
        var result = await service.CreateAsync(
            CreateRequest("SUBJ-101-009", studyId: "CADENCE-101", siteId: "SITE-101"),
            ct);

        result.IsSuccess.Should().BeTrue();
        var expectedEnrollmentRef = $"enrollment:{expectedEnrollmentId:D}";
        result.Value!.EnrollmentRef.Should().Be(expectedEnrollmentRef);
        synchronizer.Calls.Should().ContainSingle().Which.Should().Match<(Guid ParticipantId, string StudyCode, string SiteCode, string SubjectCode)>(c =>
            c.ParticipantId == result.Value!.Id.Value
            && c.StudyCode == "CADENCE-101"
            && c.SiteCode == "SITE-101"
            && c.SubjectCode == "SUBJ-101-009");

        var persisted = await subjects.GetByCodeAsync("SUBJ-101-009", ct);
        persisted.Should().NotBeNull();
        persisted!.KernelEnrollmentId.Should().Be(expectedEnrollmentId);

        var list = await service.ListAsync("CADENCE-101", "SITE-101", null, 50, 0, ct);
        list.IsSuccess.Should().BeTrue();
        list.Value!.Items.Should().ContainSingle().Which.EnrollmentRef.Should().Be(expectedEnrollmentRef);

        var detailByEnrollmentRef = await service.GetDetailAsync(expectedEnrollmentRef, ct);
        detailByEnrollmentRef.IsSuccess.Should().BeTrue();
        detailByEnrollmentRef.Value!.SubjectId.Value.Should().Be("SUBJ-101-009");
        detailByEnrollmentRef.Value.EnrollmentRef.Should().Be(expectedEnrollmentRef);

        var detailByVersionedEnrollmentRef = await service.GetDetailAsync($"{expectedEnrollmentRef}@1", ct);
        detailByVersionedEnrollmentRef.IsSuccess.Should().BeTrue();
        detailByVersionedEnrollmentRef.Value!.EnrollmentRef.Should().Be(expectedEnrollmentRef);

        var detailByLegacyParticipantRef = await service.GetDetailAsync($"participant:{result.Value.Id.Value:D}", ct);
        detailByLegacyParticipantRef.IsSuccess.Should().BeTrue();
        detailByLegacyParticipantRef.Value!.EnrollmentRef.Should().Be(expectedEnrollmentRef);
    }

    [Fact]
    public async Task CreateAsync_WhenKernelEnrollmentSynchronizerFails_ReturnsFailureWithoutPersistingOrAuditingSubject()
    {
        var synchronizer = new RecordingKernelEnrollmentSynchronizer
        {
            NextResult = Cadence.Core.Domain.Result<Guid>.Failure(
                Cadence.Core.Domain.DomainError.Forbidden("K.Authorization.Denied", "Cross-site screening denied.")),
        };
        var subjects = new InMemorySubjectRepository();
        var auditLog = new RecordingAuditLogWriter();
        var service = new SubjectApplicationService(
            subjects,
            new InMemoryConsentRepository(),
            new InMemorySiteComplianceRepository(),
            new FakeDemographicsEncryptionService(),
            auditLog,
            new FakeCurrentAuditContextAccessor(),
            kernelEnrollmentSynchronizer: synchronizer);

        var result = await service.CreateAsync(
            CreateRequest("SUBJ-102-009", studyId: "CADENCE-101", siteId: "SITE-102"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("K.Authorization.Denied");
        (await subjects.GetByCodeAsync("SUBJ-102-009", TestContext.Current.CancellationToken)).Should().BeNull();
        auditLog.Calls.Should().BeEmpty();
    }

    private sealed class RecordingKernelEnrollmentSynchronizer : IKernelEnrollmentSynchronizer
    {
        public List<(Guid ParticipantId, string StudyCode, string SiteCode, string SubjectCode)> Calls { get; } = [];

        public Cadence.Core.Domain.Result<Guid> NextResult { get; set; } = Cadence.Core.Domain.Result<Guid>.Success(Guid.NewGuid());

        public Task<Cadence.Core.Domain.Result<Guid>> SynchronizeScreeningEnrollmentAsync(
            Guid participantId,
            string studyCode,
            string siteCode,
            string subjectCode,
            DateTimeOffset screenedAt,
            string changeReason,
            CancellationToken cancellationToken)
        {
            Calls.Add((participantId, studyCode, siteCode, subjectCode));
            return Task.FromResult(NextResult);
        }
    }
}
