using System;
using System.Collections.Generic;
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
/// Coverage for <see cref="SubjectApplicationService.GetDetailAsync"/>: the RTSM blinding
/// (the service never returns an arm, #5607), the shared <c>ResolveSubjectAsync</c> dual-lookup contract
/// (surrogate UUID PK first, then domain subject code), and the full-population contract
/// (the response reflects the aggregate's real current state including <c>Id</c>, <c>ActiveProtocolVersion</c>,
/// and <c>EnrollmentIndex</c>).
/// </summary>
public class SubjectApplicationServiceDetailTests
{
    private sealed class Harness
    {
        public InMemorySubjectRepository Subjects { get; } = new();

        public InMemoryConsentRepository Consents { get; } = new();

        public InMemorySiteComplianceRepository Compliance { get; } = new();

        public SubjectApplicationService Service { get; }

        public Harness() =>
            Service = new SubjectApplicationService(Subjects, Consents, Compliance, new FakeDemographicsEncryptionService(),
                new RecordingAuditLogWriter(), new FakeCurrentAuditContextAccessor());
    }

    private static readonly StudyCode Study = new("STUDY-001");

    private static async Task<ClinicalSubject> SeedSubjectAsync(InMemorySubjectRepository subjects, string subjectCode,
        SiteCode? siteCode = null, SubjectState status = SubjectState.Screening, int enrollmentIndex = 0)
    {
        var subject = ClinicalSubject.Create(new SubjectCode(subjectCode), Study, siteCode,
            enrollmentIndex: enrollmentIndex, encryptedDemographics: null, stratFactors: null);
        if (status != SubjectState.Screening)
        {
            subject.TransitionTo(status).IsSuccess.Should().BeTrue(
                "test setup must reach the target status through a legal transition path");
        }

        await subjects.AddAsync(subject, CancellationToken.None);
        return subject;
    }

    [Fact]
    public async Task GetDetailAsync_GivenSubjectNotFound_ReturnsNotFoundError()
    {
        var harness = new Harness();

        var byCode = await harness.Service.GetDetailAsync("SUBJ-DOES-NOT-EXIST",
            TestContext.Current.CancellationToken);
        var byUuid = await harness.Service.GetDetailAsync("0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d79",
            TestContext.Current.CancellationToken);

        byCode.IsFailure.Should().BeTrue();
        byCode.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
        byUuid.IsFailure.Should().BeTrue();
        byUuid.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
    }

    [Fact]
    public async Task GetDetailAsync_ResolvesSubjectBySurrogateIdAndDomainCode()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", enrollmentIndex: 4);

        var byId = await harness.Service.GetDetailAsync(subject.Id.ToString(),
            TestContext.Current.CancellationToken);
        var byCode = await harness.Service.GetDetailAsync(subject.SubjectCode.Value,
            TestContext.Current.CancellationToken);

        byId.IsSuccess.Should().BeTrue("participant deep links address subjects by surrogate UUID");
        byId.Value!.Id.Should().Be(subject.Id);
        byId.Value.SubjectId.Should().Be(subject.SubjectCode);
        byId.Value.ActiveProtocolVersion.Should().Be(subject.ActiveProtocolVersion);
        byId.Value.EnrollmentIndex.Should().Be(4);
        byCode.IsSuccess.Should().BeTrue();
        byCode.Value.Should().Be(byId.Value);
    }

    [Fact]
    public async Task GetDetailAsync_IsFullyPopulated_ReflectingTheAggregatesRealCurrentState_LikeEveryOtherSubjectResponse()
    {
        var siteCode = new SiteCode("SITE-042");
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode, SubjectState.Enrolled, enrollmentIndex: 7);

        var result = await harness.Service.GetDetailAsync(subject.SubjectCode.Value,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(subject.Id);
        result.Value.SubjectId.Should().Be(subject.SubjectCode);
        result.Value.StudyId.Should().Be(subject.StudyCode);
        result.Value.Status.Should().Be("ENROLLED", "responses reflect the real current status");
        result.Value.SiteId.Should().Be(siteCode);
        result.Value.ActiveProtocolVersion.Should().Be(subject.ActiveProtocolVersion);
        result.Value.EnrollmentIndex.Should().Be(7);
    }

    // -- RTSM blinding (#5607) ------------------------------------------------------------------

    [Fact]
    public async Task GetDetailAsync_NeverCarriesTheArm_EvenForARandomizedSubject()
    {
        // The arm lives only as ciphertext on the randomisation allocation; the subject service has no
        // way to read it, so the detail it returns is blinded for every caller. The endpoint reveals the
        // allocation to an unblinded caller through IRandomizationApplicationService.
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", status: SubjectState.Enrolled);
        subject.Randomize(Guid.NewGuid(), "KIT-001", new Dictionary<string, object?>()).IsSuccess.Should().BeTrue();

        var result = await harness.Service.GetDetailAsync(subject.SubjectCode.Value,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("RANDOMIZED");
        result.Value.TreatmentGroup.Should().BeNull();
        result.Value.RandomizationSeed.Should().BeNull();
        result.Value.InvestigationalProductId.Should().BeNull();
    }
}
