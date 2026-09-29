using System;
using System.Collections.Generic;
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
/// `MIG-SUBJ-018` coverage for <see cref="SubjectApplicationService.RecordConsentAsync"/>: SCN-002's Rule 3
/// cross-row cascade-clearance assertion (plus the boundary and precision-of-condition variants around it),
/// the create-vs-merge branch, dual-lookup resolution, and the not-found error path. Uses the in-memory
/// <c>Cadence.Testing.Fakes</c> repositories — see <see cref="SubjectApplicationServiceCreateTests"/>'s doc
/// comment for why.
/// </summary>
public class SubjectApplicationServiceConsentTests
{
    private sealed record AuditCall(string TableName, string RecordId, string Action, string UserId, string ChangeReason);

    private sealed class RecordingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditCall> Calls { get; } = new();

        public Task WriteAsync(string tableName, string recordId, string action, string userId, string changeReason,
            IReadOnlyDictionary<string, object?>? oldValues, IReadOnlyDictionary<string, object?>? newValues, CancellationToken ct)
        {
            Calls.Add(new AuditCall(tableName, recordId, action, userId, changeReason));
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

    private sealed class FakeDemographicsEncryptionService : IDemographicsEncryptionService
    {
        public string Encrypt(IReadOnlyDictionary<string, object?> demographics) =>
            throw new NotSupportedException("Not exercised by these tests.");

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

    private static readonly StudyCode Study = new("STUDY-001");

    private static async Task<ClinicalSubject> SeedSubjectAsync(InMemorySubjectRepository subjects, string subjectCode)
    {
        var subject = ClinicalSubject.Create(new SubjectCode(subjectCode), Study, siteCode: null,
            enrollmentIndex: 0, encryptedDemographics: null, stratFactors: null);
        await subjects.AddAsync(subject, CancellationToken.None);
        return subject;
    }

    private static SubjectConsentRequest ConsentRequest(string versionTag, int versionIndex, bool icfSigned = true,
        bool requiresReconsent = false, bool isPaperOverride = false) =>
        new(new ProtocolVersionPayload(Study.Value, versionTag, versionIndex), icfSigned, IcfSignedDate: null,
            requiresReconsent, isPaperOverride);

    [Fact]
    public async Task RecordConsentAsync_GivenNoExistingRow_CreatesNewConsentNotAMerge()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var result = await harness.Service.RecordConsentAsync(subject.SubjectCode.Value,
            ConsentRequest("v1.0", 1, icfSigned: true), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.VersionIndex.Should().Be(1);
        result.Value.VersionTag.Should().Be("v1.0");

        var stored = await harness.Consents.GetAsync(subject.SubjectCode, Study, 1, TestContext.Current.CancellationToken);
        stored.Should().NotBeNull();
        stored!.Id.Should().Be(result.Value.Id);
    }

    [Fact]
    public async Task RecordConsentAsync_GivenExistingRowForSameVersionIndex_MergesIntoExistingRowRatherThanCreatingNew()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var initial = await harness.Service.RecordConsentAsync(subject.SubjectCode.Value,
            ConsentRequest("v1.0", 1, icfSigned: false), TestContext.Current.CancellationToken);
        var resubmission = await harness.Service.RecordConsentAsync(subject.SubjectCode.Value,
            ConsentRequest("v1.1", 1, icfSigned: true), TestContext.Current.CancellationToken);

        initial.IsSuccess.Should().BeTrue();
        resubmission.IsSuccess.Should().BeTrue();
        resubmission.Value!.Id.Should().Be(initial.Value!.Id,
            "a resubmission for the same (subjectCode, studyCode, versionIndex) must merge into the existing row, not create a second one");
        resubmission.Value.IcfSigned.Should().BeTrue();
        resubmission.Value.VersionTag.Should().Be("v1.1");
    }

    [Fact]
    public async Task RecordConsentAsync_GivenNewConsentSignedWithoutRequiringReconsent_CascadesClearanceToOtherRowsForSameSubject()
    {
        // SCN-002: consent version 1 requires reconsent; recording version 2 (signed, no longer requiring
        // reconsent) must clear version 1's flag in the same unit of work.
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var version1 = await harness.Service.RecordConsentAsync(subject.SubjectCode.Value,
            ConsentRequest("v1.0", 1, icfSigned: true, requiresReconsent: true), TestContext.Current.CancellationToken);
        version1.IsSuccess.Should().BeTrue();
        version1.Value!.RequiresReconsent.Should().BeTrue();

        var version2 = await harness.Service.RecordConsentAsync(subject.SubjectCode.Value,
            ConsentRequest("v2.0", 2, icfSigned: true, requiresReconsent: false), TestContext.Current.CancellationToken);
        version2.IsSuccess.Should().BeTrue();
        version2.Value!.RequiresReconsent.Should().BeFalse();

        var reloadedVersion1 = await harness.Consents.GetAsync(subject.SubjectCode, Study, 1, TestContext.Current.CancellationToken);
        reloadedVersion1.Should().NotBeNull();
        reloadedVersion1!.RequiresReconsent.Should().BeFalse("Rule 3's cascade must clear every other row still requiring reconsent (SCN-002)");
    }

    [Fact]
    public async Task RecordConsentAsync_CascadeClearance_DoesNotTouchADifferentSubjectsConsent()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");
        var otherSubject = await SeedSubjectAsync(harness.Subjects, "SUBJ-OTHER");

        var otherSubjectConsent = await harness.Service.RecordConsentAsync(otherSubject.SubjectCode.Value,
            ConsentRequest("v1.0", 1, icfSigned: true, requiresReconsent: true), TestContext.Current.CancellationToken);
        otherSubjectConsent.IsSuccess.Should().BeTrue();

        var thisSubjectConsent = await harness.Service.RecordConsentAsync(subject.SubjectCode.Value,
            ConsentRequest("v1.0", 1, icfSigned: true, requiresReconsent: false), TestContext.Current.CancellationToken);
        thisSubjectConsent.IsSuccess.Should().BeTrue();

        var reloadedOther = await harness.Consents.GetAsync(otherSubject.SubjectCode, Study, 1, TestContext.Current.CancellationToken);
        reloadedOther.Should().NotBeNull();
        reloadedOther!.RequiresReconsent.Should().BeTrue("cascade clearance must never touch a different subject's consent row");
    }

    [Fact]
    public async Task RecordConsentAsync_GivenIncomingRequiresReconsentTrue_DoesNotCascadeClearOtherRows()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        await harness.Service.RecordConsentAsync(subject.SubjectCode.Value,
            ConsentRequest("v1.0", 1, icfSigned: true, requiresReconsent: true), TestContext.Current.CancellationToken);

        // Incoming consent itself still requires reconsent -> the cascade condition
        // (!requiresReconsent && (icfSigned || isPaperOverride)) must not fire even though it is signed.
        await harness.Service.RecordConsentAsync(subject.SubjectCode.Value,
            ConsentRequest("v2.0", 2, icfSigned: true, requiresReconsent: true), TestContext.Current.CancellationToken);

        var reloadedVersion1 = await harness.Consents.GetAsync(subject.SubjectCode, Study, 1, TestContext.Current.CancellationToken);
        reloadedVersion1!.RequiresReconsent.Should().BeTrue();
    }

    [Fact]
    public async Task RecordConsentAsync_GivenIncomingNeitherSignedNorPaperOverride_DoesNotCascadeClearOtherRows()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        await harness.Service.RecordConsentAsync(subject.SubjectCode.Value,
            ConsentRequest("v1.0", 1, icfSigned: true, requiresReconsent: true), TestContext.Current.CancellationToken);

        // requiresReconsent is false on the incoming consent, but it is neither signed nor paper-overridden,
        // so the cascade's "&&" (not "||") condition must still not fire.
        await harness.Service.RecordConsentAsync(subject.SubjectCode.Value,
            ConsentRequest("v2.0", 2, icfSigned: false, requiresReconsent: false, isPaperOverride: false),
            TestContext.Current.CancellationToken);

        var reloadedVersion1 = await harness.Consents.GetAsync(subject.SubjectCode, Study, 1, TestContext.Current.CancellationToken);
        reloadedVersion1!.RequiresReconsent.Should().BeTrue();
    }

    [Fact]
    public async Task RecordConsentAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError()
    {
        var harness = new Harness();

        var result = await harness.Service.RecordConsentAsync("NO-SUCH-SUBJECT",
            ConsentRequest("v1.0", 1), TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
    }

    [Fact]
    public async Task RecordConsentAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var byId = await harness.Service.RecordConsentAsync(subject.Id.ToString(),
            ConsentRequest("v1.0", 1), TestContext.Current.CancellationToken);

        byId.IsSuccess.Should().BeTrue();
        byId.Value!.SubjectId.Should().Be(subject.SubjectCode);
    }

    [Fact]
    public async Task RecordConsentAsync_GivenNewConsent_WritesRecordConsentAuditEntry()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var result = await harness.Service.RecordConsentAsync(subject.SubjectCode.Value,
            ConsentRequest("v1.0", 1), TestContext.Current.CancellationToken);

        harness.AuditLog.Calls.Should().ContainSingle();
        var call = harness.AuditLog.Calls[0];
        call.TableName.Should().Be("subject_consents");
        call.Action.Should().Be("RECORD_CONSENT");
        call.RecordId.Should().Be(result.Value!.Id.ToString());
    }
}
