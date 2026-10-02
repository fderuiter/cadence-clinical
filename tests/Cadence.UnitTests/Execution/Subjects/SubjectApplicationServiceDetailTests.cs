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
/// `MIG-SUBJ-019` coverage for <see cref="SubjectApplicationService.GetDetailAsync"/>: the RTSM-blinding
/// redaction matrix (blinded vs. unblinded caller), the single-lookup-by-domain-code-only contract (raise
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

    /// <summary>
    /// <see cref="ClinicalSubject"/> exposes no public way to populate
    /// <see cref="ClinicalSubject.TreatmentGroupCiphertext"/> yet -- randomization/kit-assignment is out of
    /// this ticket's (and every currently-implemented ticket's) scope, per <c>ClinicalSubject.Create</c>'s
    /// own remarks. Reflection over the private setter is the only way to construct a fixture that actually
    /// exercises the RTSM-blinding *redaction* (as opposed to a value that is trivially null either way).
    /// </summary>
    private static void SetTreatmentGroupCiphertext(ClinicalSubject subject, string value)
    {
        var property = typeof(ClinicalSubject).GetProperty(nameof(ClinicalSubject.TreatmentGroupCiphertext))
            ?? throw new InvalidOperationException($"{nameof(ClinicalSubject.TreatmentGroupCiphertext)} property not found via reflection.");
        property.SetValue(subject, value);
    }

    [Fact]
    public async Task GetDetailAsync_GivenSubjectNotFound_ReturnsNotFoundError()
    {
        var harness = new Harness();

        var result = await harness.Service.GetDetailAsync("SUBJ-DOES-NOT-EXIST", isBlindedCaller: false,
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

        var byId = await harness.Service.GetDetailAsync(subject.Id.ToString(), isBlindedCaller: false,
            TestContext.Current.CancellationToken);
        var byCode = await harness.Service.GetDetailAsync(subject.SubjectCode.Value, isBlindedCaller: false,
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

        var result = await harness.Service.GetDetailAsync(subject.SubjectCode.Value, isBlindedCaller: false,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SubjectId.Should().Be(subject.SubjectCode);
        result.Value.StudyId.Should().Be(subject.StudyCode);
        result.Value.Status.Should().Be("ENROLLED", "responses reflect the real current status");
        result.Value.SiteId.Should().Be(siteCode);
    }

    // -- RTSM blinding redaction matrix ----------------------------------------------------------

    [Fact]
    public async Task GetDetailAsync_GivenBlindedCallerTrue_RedactsAllThreeRtsmFieldsToNull()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");
        SetTreatmentGroupCiphertext(subject, "ARM-A");

        var result = await harness.Service.GetDetailAsync(subject.SubjectCode.Value, isBlindedCaller: true,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TreatmentGroup.Should().BeNull();
        result.Value.RandomizationSeed.Should().BeNull();
        result.Value.InvestigationalProductId.Should().BeNull();
    }

    [Fact]
    public async Task GetDetailAsync_GivenBlindedCallerFalse_ReturnsTheRealTreatmentGroup_NotRedacted()
    {
        // The meaningful half of the redaction matrix: prove the filter actually branches on
        // isBlindedCaller, rather than TreatmentGroup simply always being null regardless.
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");
        SetTreatmentGroupCiphertext(subject, "ARM-A");

        var result = await harness.Service.GetDetailAsync(subject.SubjectCode.Value, isBlindedCaller: false,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TreatmentGroup.Should().Be("ARM-A");
    }

    [Fact]
    public async Task GetDetailAsync_GivenBlindedCallerTrue_RedactsAnActuallyPopulatedTreatmentGroup_NotJustAnAlreadyNullOne()
    {
        // Belt-and-braces: same populated subject as the "false" case above, asserting the *same* real
        // value is nulled out when isBlindedCaller flips to true -- a true before/after redaction proof
        // rather than two independently-trivial assertions.
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");
        SetTreatmentGroupCiphertext(subject, "ARM-A");

        var unblinded = await harness.Service.GetDetailAsync(subject.SubjectCode.Value, isBlindedCaller: false,
            TestContext.Current.CancellationToken);
        var blinded = await harness.Service.GetDetailAsync(subject.SubjectCode.Value, isBlindedCaller: true,
            TestContext.Current.CancellationToken);

        unblinded.Value!.TreatmentGroup.Should().Be("ARM-A");
        blinded.Value!.TreatmentGroup.Should().BeNull();
    }

    [Fact]
    public async Task GetDetailAsync_RandomizationSeedAndInvestigationalProductId_AreAlwaysNull_RegardlessOfBlinding()
    {
        // ClinicalSubject exposes no data source for either field yet (the randomization slice that would
        // populate them is out of scope for this ticket) -- documented as a known limitation, not an
        // oversight: both fields have nothing to redact either way today.
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var unblinded = await harness.Service.GetDetailAsync(subject.SubjectCode.Value, isBlindedCaller: false,
            TestContext.Current.CancellationToken);

        unblinded.Value!.RandomizationSeed.Should().BeNull();
        unblinded.Value.InvestigationalProductId.Should().BeNull();
    }
}
