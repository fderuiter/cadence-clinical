using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Testing;
using Cadence.UnitTests.Execution.Fakes;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// `MIG-SUBJ-019` coverage for <see cref="SubjectApplicationService.AdvanceStateAsync"/> and
/// <see cref="SubjectApplicationService.EvaluateAndTransitionScreeningAsync"/>: SCN-003's state-machine
/// integration through the service (not just <see cref="SubjectTransitionGraph"/> in isolation), SCN-005's
/// Rule 2 site-compliance gate on both the blocked and unblocked paths — asserted identically through both
/// entry points — and the §2.6 under-population assertion for a successful transition response.
/// @req:PRD-SUB-001
/// </summary>
public class SubjectApplicationServiceStateTests
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

        public FakeCurrentAuditContextAccessor AuditContext { get; set; } = new();

        public Func<ClinicalSubject, CancellationToken, Task<bool>>? ScreeningEligibility { get; set; }

        public SubjectApplicationService Service =>
            new(Subjects, Consents, Compliance, Encryption, AuditLog, Clock, AuditContext, ScreeningEligibility);
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

    // -- AdvanceStateAsync: not-found ------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError()
    {
        var harness = new Harness();

        var result = await harness.Service.AdvanceStateAsync("NO-SUCH-SUBJECT",
            new SubjectStateUpdateRequest(Status: "ENROLLED", State: null), "reason",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
    }

    // -- AdvanceStateAsync: SCN-003 state-machine integration ------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenLegalTransition_SucceedsAndPersistsNewStatus()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "SCREEN_FAILED", State: null), "Failed eligibility",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        persisted!.Status.Should().Be(SubjectState.ScreenFailed);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenForbiddenTransition_ReturnsInvalidStateTransitionAndDoesNotMutate()
    {
        // SCN-003: SCREENING -> ACTIVE is forbidden (must go through ENROLLED/RANDOMIZED first).
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "ACTIVE", State: null), "reason",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        persisted!.Status.Should().Be(SubjectState.Screening, "a rejected transition must never mutate the aggregate");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenUnmappedStateString_ReturnsInvalidStateTransition_SameCodeAsForbiddenTransition()
    {
        // Raise site #6: a typo'd state name is indistinguishable from a legal-but-forbidden transition.
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "ENROLED", State: null), "reason",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenNeitherStatusNorState_ReturnsInvalidStateTransitionRatherThanThrowing()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: null, State: null), "reason",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var result = await harness.Service.AdvanceStateAsync(subject.Id.ToString(),
            new SubjectStateUpdateRequest(Status: "SCREEN_FAILED", State: null), "reason",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenLegalTransition_WritesTransitionAuditEntryNamedForTargetState()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "SCREEN_FAILED", State: null), "Failed eligibility",
            TestContext.Current.CancellationToken);

        harness.AuditLog.Calls.Should().ContainSingle();
        var call = harness.AuditLog.Calls[0];
        call.TableName.Should().Be("clinical_subjects");
        call.Action.Should().Be("TRANSITION_SCREEN_FAILED");
        call.RecordId.Should().Be(subject.Id.ToString());
        call.ChangeReason.Should().Be("Failed eligibility");
    }

    // -- §2.6 under-population --------------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseStatusIsHardcodedScreening_NotTheRealValue()
    {
        // The §2.6 quirk: assert this explicitly as correct behavior for this handler, not a bug to fix.
        var harness = new Harness();
        var siteCode = new SiteCode("SITE-COMPLIANT");
        harness.Compliance.Seed(SiteComplianceCache.Create(Study, siteCode, "SITE_ACTIVATION",
            isComplete: true, missingDocuments: []));
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode);

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "ENROLLED", State: null), "Site compliance verified",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("SCREENING", "Underpopulated hardcodes Status regardless of the real post-transition value");
        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        persisted!.Status.Should().Be(SubjectState.Enrolled, "the DB row must genuinely reflect the real transition");
    }

    // -- SCN-005: Rule 2 sovereign site-compliance gate ------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenNonCompliantSite_TargetingEnrolled_FailsWithBlockedEnrollmentAndWritesBlockedAudit()
    {
        var harness = new Harness();
        var siteCode = new SiteCode("SITE-NONCOMPLIANT");
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode);

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "ENROLLED", State: null), "Attempted enrollment",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("BLOCKED_ENROLLMENT");
        harness.AuditLog.Calls.Should().ContainSingle();
        harness.AuditLog.Calls[0].Action.Should().Be("BLOCKED_ENROLLMENT");
        harness.AuditLog.Calls[0].TableName.Should().Be("clinical_subjects");

        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        persisted!.Status.Should().Be(SubjectState.Screening, "a blocked enrollment must never mutate status");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenNoComplianceRowAtAll_TargetingEnrolled_IsTreatedAsNonCompliant()
    {
        var harness = new Harness();
        var siteCode = new SiteCode("SITE-UNKNOWN");
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode);

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "ENROLLED", State: null), "reason",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("BLOCKED_ENROLLMENT");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenCompliantSite_TargetingEnrolled_Succeeds()
    {
        var harness = new Harness();
        var siteCode = new SiteCode("SITE-COMPLIANT");
        harness.Compliance.Seed(SiteComplianceCache.Create(Study, siteCode, "SITE_ACTIVATION",
            isComplete: true, missingDocuments: []));
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode);

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "ENROLLED", State: null), "Site compliance verified",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        persisted!.Status.Should().Be(SubjectState.Enrolled);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenNoSiteId_TargetingEnrolled_IsBlockedRatherThanAutoCompliant()
    {
        // fixtures/input_valid_nominal.json's SCN-003-A describes the *legacy* router's own
        // `check_site_compliance_for_enrollment`'s `if not site_id: return True` short-circuit (a falsy-
        // empty-string trap 01_spec.md §3's dynamic-traps table explicitly flags and recommends fixing, not
        // reproducing: "treat whitespace/absence as invalid or non-compliant"). ISiteComplianceRepository
        // (MIG-SUBJ-013, out of this ticket's scope) already made that exact fix deliberately -- its own
        // GetAsync doc comment states a null siteCode "can never match any row... a subject with no site
        // cannot satisfy a site-specific compliance gate" -- so this port's AdvanceStateAsync, which merely
        // calls that port and treats a null result as non-compliant like any other miss, blocks here rather
        // than reproducing SCN-003-A's literal legacy auto-compliant outcome. This is a deliberate,
        // documented divergence from that one fixture scenario's literal text, not an oversight; flagged for
        // whoever implements MIG-SUBJ-026's fixture replay to reconcile.
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-NOSITE", siteCode: null);

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "ENROLLED", State: null), "Subject passed screening eligibility",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("BLOCKED_ENROLLMENT");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_TargetingStateOtherThanEnrolled_NeverConsultsComplianceGate()
    {
        // The Rule 2 gate is scoped to `target == Enrolled` only -- a non-compliant (or entirely unknown)
        // site must never block any other transition.
        var harness = new Harness();
        var siteCode = new SiteCode("SITE-NONCOMPLIANT");
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode);

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "WITHDRAWN", State: null), "Subject withdrew consent",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
    }

    // -- EvaluateAndTransitionScreeningAsync: not-found ------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EvaluateAndTransitionScreeningAsync_GivenSubjectNotFound_ReturnsNotFoundError()
    {
        var harness = new Harness { ScreeningEligibility = (_, _) => Task.FromResult(true) };

        var result = await harness.Service.EvaluateAndTransitionScreeningAsync(
            "SUBJ-DOES-NOT-EXIST", TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EvaluateAndTransitionScreeningAsync_ResolvesSubjectByDomainCode_NotSurrogateId()
    {
        // Deliberately not the dual-lookup helper -- every fixture route for this endpoint passes a domain
        // code (e.g. "SUBJ-009"), so a surrogate GUID must NOT resolve here.
        var harness = new Harness { ScreeningEligibility = (_, _) => Task.FromResult(true) };
        var siteCode = new SiteCode("SITE-COMPLIANT");
        harness.Compliance.Seed(SiteComplianceCache.Create(Study, siteCode, "SITE_ACTIVATION",
            isComplete: true, missingDocuments: []));
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode);

        var byId = await harness.Service.EvaluateAndTransitionScreeningAsync(
            subject.Id.ToString(), TestContext.Current.CancellationToken);
        var byCode = await harness.Service.EvaluateAndTransitionScreeningAsync(
            subject.SubjectCode.Value, TestContext.Current.CancellationToken);

        byId.IsFailure.Should().BeTrue("this method's single lookup is by domain code only, not the surrogate id");
        byId.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
        byCode.IsSuccess.Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EvaluateAndTransitionScreeningAsync_GivenEligible_TransitionsToEnrolled()
    {
        var harness = new Harness { ScreeningEligibility = (_, _) => Task.FromResult(true) };
        var siteCode = new SiteCode("SITE-COMPLIANT");
        harness.Compliance.Seed(SiteComplianceCache.Create(Study, siteCode, "SITE_ACTIVATION",
            isComplete: true, missingDocuments: []));
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode);

        var result = await harness.Service.EvaluateAndTransitionScreeningAsync(
            subject.SubjectCode.Value, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        persisted!.Status.Should().Be(SubjectState.Enrolled);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EvaluateAndTransitionScreeningAsync_GivenIneligible_TransitionsToScreenFailed()
    {
        var harness = new Harness { ScreeningEligibility = (_, _) => Task.FromResult(false) };
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var result = await harness.Service.EvaluateAndTransitionScreeningAsync(
            subject.SubjectCode.Value, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        persisted!.Status.Should().Be(SubjectState.ScreenFailed);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EvaluateAndTransitionScreeningAsync_GivenEligibleAtNonCompliantSite_FailsIdenticallyToAdvanceStateAsync()
    {
        // SCN-005: the Rule 2 gate must hold identically whether reached via AdvanceStateAsync or
        // EvaluateAndTransitionScreeningAsync.
        var harness = new Harness { ScreeningEligibility = (_, _) => Task.FromResult(true) };
        var siteCode = new SiteCode("SITE-NONCOMPLIANT");
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode);

        var result = await harness.Service.EvaluateAndTransitionScreeningAsync(
            subject.SubjectCode.Value, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("BLOCKED_ENROLLMENT");
        harness.AuditLog.Calls.Should().ContainSingle();
        harness.AuditLog.Calls[0].Action.Should().Be("BLOCKED_ENROLLMENT");

        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        persisted!.Status.Should().Be(SubjectState.Screening);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EvaluateAndTransitionScreeningAsync_UsesAuditContextUserIdAndChangeReason_NotALiteralFallback()
    {
        var harness = new Harness
        {
            ScreeningEligibility = (_, _) => Task.FromResult(false),
            AuditContext = new FakeCurrentAuditContextAccessor { UserId = "investigator_1", ChangeReason = "Screening evaluation" },
        };
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        await harness.Service.EvaluateAndTransitionScreeningAsync(
            subject.SubjectCode.Value, TestContext.Current.CancellationToken);

        harness.AuditLog.Calls.Should().ContainSingle();
        harness.AuditLog.Calls[0].UserId.Should().Be("investigator_1");
        harness.AuditLog.Calls[0].ChangeReason.Should().Be("Screening evaluation");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EvaluateAndTransitionScreeningAsync_GivenNoEvaluatorConfigured_ThrowsRatherThanGuessingAVerdict()
    {
        var harness = new Harness(); // ScreeningEligibility left null
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var act = () => harness.Service.EvaluateAndTransitionScreeningAsync(
            subject.SubjectCode.Value, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
