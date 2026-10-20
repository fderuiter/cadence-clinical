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
/// `MIG-SUBJ-019` coverage for <see cref="SubjectApplicationService.GetDetailAsync"/>: the RTSM blinding
/// (the service never returns an arm, #5607), the single-lookup-by-domain-code-only contract (raise
/// site #13's not-found, deliberately not the shared dual-lookup helper), and the full-population contract
/// (the response reflects the aggregate's real current state).
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
        SiteCode? siteCode = null, SubjectState status = SubjectState.Screening)
    {
        var subject = ClinicalSubject.Create(new SubjectCode(subjectCode), Study, siteCode,
            enrollmentIndex: 0, encryptedDemographics: null, stratFactors: null);
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

        var result = await harness.Service.GetDetailAsync("SUBJ-DOES-NOT-EXIST",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
    }

    [Fact]
    public async Task GetDetailAsync_ResolvesSubjectByDomainCode_NotSurrogateId()
    {
        // Deliberately not the dual-lookup helper -- every fixture route for this endpoint passes a domain
        // code (e.g. "SUBJ-DOES-NOT-EXIST"), so a surrogate GUID must NOT resolve here.
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var byId = await harness.Service.GetDetailAsync(subject.Id.ToString(),
            TestContext.Current.CancellationToken);
        var byCode = await harness.Service.GetDetailAsync(subject.SubjectCode.Value,
            TestContext.Current.CancellationToken);

        byId.IsFailure.Should().BeTrue("this method's single lookup is by domain code only, not the surrogate id");
        byId.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
        byCode.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetDetailAsync_IsFullyPopulated_ReflectingTheAggregatesRealCurrentState_LikeEveryOtherSubjectResponse()
    {
        var siteCode = new SiteCode("SITE-042");
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode, SubjectState.Enrolled);

        var result = await harness.Service.GetDetailAsync(subject.SubjectCode.Value,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SubjectId.Should().Be(subject.SubjectCode);
        result.Value.StudyId.Should().Be(subject.StudyCode);
        result.Value.Status.Should().Be("ENROLLED", "responses reflect the real current status");
        result.Value.SiteId.Should().Be(siteCode);
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
