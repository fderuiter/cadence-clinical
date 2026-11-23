using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// Unit coverage for <see cref="SubjectApplicationService.AssignSiteAsync"/>: assigning a clinical site to a siteless
/// participant, triggering <c>IKernelEnrollmentSynchronizer.SynchronizeScreeningEnrollmentAsync</c> with deterministic
/// <see cref="FakeClock"/> time and <c>X-Change-Reason</c>, recording <c>ASSIGN_SITE</c> audit entries, idempotent
/// same-site assignment, and refusing cross-site reassignment (<c>SUBJECT_SITE_ALREADY_ASSIGNED</c>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class SubjectApplicationServiceAssignSiteTests
{
    private static readonly DateTimeOffset T0 = FakeClock.DefaultStart;

    private sealed class Harness
    {
        public InMemorySubjectRepository Subjects { get; } = new();

        public InMemoryConsentRepository Consents { get; } = new();

        public InMemorySiteComplianceRepository Compliance { get; } = new();

        public FakeDemographicsEncryptionService Encryption { get; } = new();

        public RecordingAuditLogWriter AuditLog { get; } = new();

        public FakeCurrentAuditContextAccessor AuditContext { get; } = new();

        public RecordingKernelEnrollmentSynchronizer Synchronizer { get; } = new();

        public FakeClock Clock { get; } = new(T0);

        public SubjectApplicationService Service { get; }

        public Harness() =>
            Service = new SubjectApplicationService(
                Subjects,
                Consents,
                Compliance,
                Encryption,
                AuditLog,
                AuditContext,
                clock: Clock,
                kernelEnrollmentSynchronizer: Synchronizer);

        public async Task<ClinicalSubject> SeedLegacySitelessSubjectAsync(string subjectCode, string studyCode, CancellationToken ct)
        {
            int nextIndex = await Subjects.EnrollNextIndexAsync(new StudyCode(studyCode), ct).ConfigureAwait(false);
            var subject = ClinicalSubject.Create(
                new SubjectCode(subjectCode),
                new StudyCode(studyCode),
                siteCode: null,
                enrollmentIndex: nextIndex,
                encryptedDemographics: null,
                stratFactors: null);
            await Subjects.AddAsync(subject, ct).ConfigureAwait(false);
            await Subjects.SaveChangesAsync(ct).ConfigureAwait(false);
            return subject;
        }
    }

    [Fact]
    public async Task AssignSiteAsync_GivenSitelessSubject_AssignsSiteSynchronizesScreeningAndWritesAuditLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = new Harness();

        var created = await harness.SeedLegacySitelessSubjectAsync("SUBJ-201", "CADENCE-101", ct);
        created.SiteCode.Should().BeNull();
        harness.Synchronizer.Calls.Should().BeEmpty("legacy siteless subject has not been synchronized yet");
        harness.AuditLog.Calls.Clear();

        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        const string reason = "Assign screening site SITE-101 after central eligibility triage";

        var assigned = await harness.Service.AssignSiteAsync(
            created.Id.ToString(),
            new SubjectSiteAssignmentRequest("SITE-101"),
            reason,
            ct);

        assigned.IsSuccess.Should().BeTrue();
        assigned.Value!.SiteId.Should().Be(new SiteCode("SITE-101"));

        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-201", ct);
        persisted.Should().NotBeNull();
        persisted!.SiteCode.Should().Be(new SiteCode("SITE-101"));
        persisted.ReasonForChange.Should().Be(reason);

        harness.Synchronizer.Calls.Should().ContainSingle().Which.Should().Match<(Guid ParticipantId, string StudyCode, string SiteCode, string SubjectCode, DateTimeOffset ScreenedAt, string ChangeReason)>(call =>
            call.ParticipantId == created.Id.Value
            && call.StudyCode == "CADENCE-101"
            && call.SiteCode == "SITE-101"
            && call.SubjectCode == "SUBJ-201"
            && call.ScreenedAt == T0.AddMinutes(5)
            && call.ChangeReason == reason);

        harness.AuditLog.Calls.Should().ContainSingle();
        var audit = harness.AuditLog.Calls[0];
        audit.EntityName.Should().Be(nameof(ClinicalSubject));
        audit.RecordId.Should().Be(created.Id.ToString());
        audit.Action.Should().Be("ASSIGN_SITE");
        audit.ChangeReason.Should().Be(reason);
        audit.OldValues.Should().ContainKey("site_id").WhoseValue.Should().BeNull();
        audit.NewValues.Should().ContainKey("site_id").WhoseValue.Should().Be("SITE-101");
    }

    [Fact]
    public async Task AssignSiteAsync_GivenSameSiteAlreadyAssigned_IsIdempotentAndReSynchronizesEnrollment()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = new Harness();

        var created = await harness.Service.CreateAsync(
            new SubjectCreateRequest("SUBJ-202", "CADENCE-101", SiteId: "SITE-101", Demographics: null),
            ct);
        created.IsSuccess.Should().BeTrue();
        harness.Synchronizer.Calls.Clear();
        harness.AuditLog.Calls.Clear();

        var reAssigned = await harness.Service.AssignSiteAsync(
            "SUBJ-202",
            new SubjectSiteAssignmentRequest("SITE-101"),
            "Retry kernel screening synchronization for SITE-101",
            ct);

        reAssigned.IsSuccess.Should().BeTrue();
        reAssigned.Value!.SiteId.Should().Be(new SiteCode("SITE-101"));
        harness.Synchronizer.Calls.Should().ContainSingle();
        harness.AuditLog.Calls.Should().ContainSingle(c => c.Action == "ASSIGN_SITE");
    }

    [Fact]
    public async Task AssignSiteAsync_GivenDifferentAlreadyAssignedSite_RefusesConflictWithoutSynchronizingOrAuditing()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = new Harness();

        var created = await harness.Service.CreateAsync(
            new SubjectCreateRequest("SUBJ-203", "CADENCE-101", SiteId: "SITE-101", Demographics: null),
            ct);
        created.IsSuccess.Should().BeTrue();
        harness.Synchronizer.Calls.Clear();
        harness.AuditLog.Calls.Clear();

        var conflict = await harness.Service.AssignSiteAsync(
            created.Value!.Id.ToString(),
            new SubjectSiteAssignmentRequest("SITE-102"),
            "Attempt cross-site reassignment",
            ct);

        conflict.IsFailure.Should().BeTrue();
        conflict.Error!.Code.Should().Be("SITE_ALREADY_ASSIGNED");
        conflict.Error.ToProblemResult().StatusCode.Should().Be(409);
        harness.Synchronizer.Calls.Should().BeEmpty();
        harness.AuditLog.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AssignSiteAsync_WhenKernelEnrollmentSynchronizerFails_ReturnsErrorWithoutWritingAuditLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = new Harness();

        var created = await harness.SeedLegacySitelessSubjectAsync("SUBJ-204", "CADENCE-101", ct);
        harness.AuditLog.Calls.Clear();

        harness.Synchronizer.NextResult = Result<Guid>.Failure(
            DomainError.Conflict("K.Enrollment.SiteNotActive", "Study site SITE-102 is not active."));

        var failed = await harness.Service.AssignSiteAsync(
            created.Id.ToString(),
            new SubjectSiteAssignmentRequest("SITE-102"),
            "Assign inactive site",
            ct);

        failed.IsFailure.Should().BeTrue();
        failed.Error!.Code.Should().Be("K.Enrollment.SiteNotActive");
        harness.AuditLog.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AssignSiteAsync_ValidatesSubjectExistenceAndRequiredSiteCode()
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = new Harness();

        var missingSubject = await harness.Service.AssignSiteAsync(
            "SUBJ-MISSING",
            new SubjectSiteAssignmentRequest("SITE-101"),
            "Assign site",
            ct);
        missingSubject.IsFailure.Should().BeTrue();
        missingSubject.Error!.ToProblemResult().StatusCode.Should().Be(404);

        var created = await harness.SeedLegacySitelessSubjectAsync("SUBJ-205", "CADENCE-101", ct);

        var blankSite = await harness.Service.AssignSiteAsync(
            created.Id.ToString(),
            new SubjectSiteAssignmentRequest("   "),
            "Assign blank site",
            ct);
        blankSite.IsFailure.Should().BeTrue();
        blankSite.Error!.Code.Should().Be("SITE_ID_REQUIRED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_WhenKernelEnrollmentSynchronizerConfigured_RequiresActiveSiteId(string? missingSiteId)
    {
        var ct = TestContext.Current.CancellationToken;
        var harness = new Harness();

        var created = await harness.Service.CreateAsync(
            new SubjectCreateRequest("SUBJ-206", "CADENCE-101", SiteId: missingSiteId, Demographics: null),
            ct);

        created.IsFailure.Should().BeTrue();
        created.Error!.Code.Should().Be("SITE_ID_REQUIRED");
        created.Error.ToProblemResult().StatusCode.Should().Be(400);
        (await harness.Subjects.GetByCodeAsync("SUBJ-206", ct)).Should().BeNull();
        harness.Synchronizer.Calls.Should().BeEmpty();
    }

    private sealed class RecordingKernelEnrollmentSynchronizer : IKernelEnrollmentSynchronizer
    {
        public List<(Guid ParticipantId, string StudyCode, string SiteCode, string SubjectCode, DateTimeOffset ScreenedAt, string ChangeReason)> Calls { get; } = [];

        public Result<Guid> NextResult { get; set; } = Result<Guid>.Success(Guid.NewGuid());

        public Task<Result<Guid>> SynchronizeScreeningEnrollmentAsync(
            Guid participantId,
            string studyCode,
            string siteCode,
            string subjectCode,
            DateTimeOffset screenedAt,
            string changeReason,
            CancellationToken cancellationToken)
        {
            Calls.Add((participantId, studyCode, siteCode, subjectCode, screenedAt, changeReason));
            return Task.FromResult(NextResult);
        }
    }
}
