using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// `MIG-SUBJ-019` coverage for <see cref="SubjectApplicationService.AdvanceStateAsync"/> and
/// <see cref="SubjectApplicationService.EvaluateAndTransitionScreeningAsync"/>: SCN-003's state-machine
/// integration through the service (not just <see cref="SubjectTransitionGraph"/> in isolation), SCN-005's
/// Rule 2 site-compliance gate on both the blocked and unblocked paths — asserted identically through both
/// entry points — and that a successful transition's response reports the real post-transition status (D8, #4771).
/// </summary>
public class SubjectApplicationServiceStateTests
{
    private sealed class Harness
    {
        public InMemorySubjectRepository Subjects { get; } = new();

        public InMemoryConsentRepository Consents { get; } = new();

        public InMemorySiteComplianceRepository Compliance { get; } = new();

        public FakeDemographicsEncryptionService Encryption { get; } = new();

        public RecordingAuditLogWriter AuditLog { get; } = new();

        public FakeCurrentAuditContextAccessor AuditContext { get; set; } = new();

        public Func<ClinicalSubject, CancellationToken, Task<bool>>? ScreeningEligibility { get; set; }

        public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 9, 30, 6, 0, 0, TimeSpan.Zero));

        public FakeKernelSynchronizer? KernelSync { get; set; }

        public IWithdrawalSignatureVerifier? SignatureVerifier { get; set; }

        public SubjectApplicationService Service =>
            new(Subjects, Consents, Compliance, Encryption, AuditLog, AuditContext, ScreeningEligibility, Clock, KernelSync, SignatureVerifier);
    }

    private sealed class FakeKernelSynchronizer : IKernelEnrollmentSynchronizer
    {
        public List<(string Study, string Subject, string TargetStatus, string Reason)> StatusCalls { get; } = [];

        public List<(string Study, string Subject, string WithdrawalKind, string Reason)> WithdrawalCalls { get; } = [];

        public DomainError? FailWith { get; set; }

        public Task<Result<Guid>> SynchronizeScreeningEnrollmentAsync(
            Guid participantId, string studyCode, string siteCode, string subjectCode, DateTimeOffset screenedAt,
            string changeReason, CancellationToken cancellationToken, string? fallbackTenantId = null, string? fallbackActorId = null) =>
            Task.FromResult(FailWith is not null ? Result<Guid>.Failure(FailWith) : Result<Guid>.Success(Guid.NewGuid()));

        public Task<Result<KernelEnrollmentSyncResult>> SynchronizeStatusChangeAsync(
            string studyCode, string subjectCode, string targetStatus, DateTimeOffset effectiveAt,
            string changeReason, CancellationToken cancellationToken, string? idempotencyToken = null)
        {
            StatusCalls.Add((studyCode, subjectCode, targetStatus, changeReason));
            int? enrollmentNumber = targetStatus == "Enrolled" ? 1 : null;
            return Task.FromResult(FailWith is not null
                ? Result<KernelEnrollmentSyncResult>.Failure(FailWith)
                : Result<KernelEnrollmentSyncResult>.Success(new KernelEnrollmentSyncResult(Guid.NewGuid(), targetStatus, enrollmentNumber)));
        }

        public Task<Result<KernelWithdrawalSyncResult>> SynchronizeWithdrawalAsync(
            string studyCode, string subjectCode, string withdrawalKind, DateTimeOffset effectiveAt,
            string changeReason, CancellationToken cancellationToken)
        {
            WithdrawalCalls.Add((studyCode, subjectCode, withdrawalKind, changeReason));
            return Task.FromResult(FailWith is not null
                ? Result<KernelWithdrawalSyncResult>.Failure(FailWith)
                : Result<KernelWithdrawalSyncResult>.Success(new KernelWithdrawalSyncResult(Guid.NewGuid(), Guid.NewGuid(), "Withdrawn", withdrawalKind)));
        }
    }

    private sealed class StubWithdrawalSignatureVerifier(Result<bool> outcome) : IWithdrawalSignatureVerifier
    {
        public int Calls { get; private set; }

        public Task<Result<bool>> VerifyAndConsumeIfHttpAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(outcome);
        }
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
    public async Task AdvanceStateAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError()
    {
        var harness = new Harness();

        var result = await harness.Service.AdvanceStateAsync("NO-SUCH-SUBJECT",
            new SubjectStateUpdateRequest(Status: "ENROLLED", State: null), "reason",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
    }

    // -- AdvanceStateAsync: unblinding (#4951, #4982) ---------------------------------------------

    private const string UnblindingReason =
        "SAE: anaphylaxis after dose 3; the ER team needs the allocation to choose treatment.";

    [Fact]
    public async Task AdvanceStateAsync_ToUnblinded_RecordsWhoUnblindedWhyAndWhen()
    {
        var harness = new Harness { AuditContext = new FakeCurrentAuditContextAccessor { UserId = "unblinder-1" } };
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-UB1", status: SubjectState.Enrolled);
        subject.TransitionTo(SubjectState.Randomized).IsSuccess.Should().BeTrue();

        var result = await harness.Service.AdvanceStateAsync("SUBJ-UB1",
            new SubjectStateUpdateRequest(Status: "UNBLINDED", State: null), UnblindingReason,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("UNBLINDED");
        subject.Status.Should().Be(SubjectState.Unblinded);
        subject.IsUnblinded.Should().BeTrue();
        subject.UnblindedBy.Should().Be("unblinder-1");
        subject.UnblindedReason.Should().Be(UnblindingReason);
        subject.UnblindedAt.Should().Be(harness.Clock.UtcNow);
        harness.AuditLog.Calls.Should().ContainSingle(c => c.Action == "TRANSITION_UNBLINDED" && c.UserId == "unblinder-1");
    }

    [Fact]
    public async Task AdvanceStateAsync_ToUnblindedFromScreening_IsRefused_AndRecordsNothing()
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-UB2");

        var result = await harness.Service.AdvanceStateAsync("SUBJ-UB2",
            new SubjectStateUpdateRequest(Status: "UNBLINDED", State: null), UnblindingReason,
            TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
        subject.IsUnblinded.Should().BeFalse();
        subject.UnblindedBy.Should().BeNull();
        harness.AuditLog.Calls.Should().BeEmpty();
    }

    /// <summary>
    /// PRD-SUB-005: an emergency unblinding needs a clinical justification of at least 50 characters, so a
    /// shorter reason leaves the participant blinded and writes nothing.
    /// </summary>
    [Theory]
    [InlineData("SAE")]
    [InlineData("Life-threatening event; allocation needed now.")]
    [InlineData("   Life-threatening event; allocation needed now.      ")]
    public async Task AdvanceStateAsync_ToUnblinded_WithAReasonUnder50Characters_IsRefused(string reason)
    {
        var harness = new Harness();
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-UB3", status: SubjectState.Enrolled);
        subject.TransitionTo(SubjectState.Randomized).IsSuccess.Should().BeTrue();

        var result = await harness.Service.AdvanceStateAsync("SUBJ-UB3",
            new SubjectStateUpdateRequest(Status: "UNBLINDED", State: null), reason,
            TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be("UNBLINDING_REASON_TOO_SHORT");
        subject.Status.Should().Be(SubjectState.Randomized);
        subject.IsUnblinded.Should().BeFalse();
        harness.AuditLog.Calls.Should().BeEmpty();
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
        call.EntityName.Should().Be("ClinicalSubject");
        call.Action.Should().Be("TRANSITION_SCREEN_FAILED");
        call.RecordId.Should().Be(subject.Id.ToString());
        call.ChangeReason.Should().Be("Failed eligibility");
    }

    // -- Transition response (D8) -----------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseAndStoredStatusAreEnrolled()
    {
        // D8 (#4771): the response reports the real post-transition status.
        var harness = new Harness();
        var siteCode = new SiteCode("SITE-COMPLIANT");
        harness.Compliance.Seed(SiteComplianceCache.Create(Study, siteCode, "SITE_ACTIVATION",
            isComplete: true, missingDocuments: []));
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode);

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "ENROLLED", State: null), "Site compliance verified",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("ENROLLED");
        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        persisted!.Status.Should().Be(SubjectState.Enrolled, "the DB row must genuinely reflect the real transition");
    }

    // -- SCN-005: Rule 2 sovereign site-compliance gate ------------------------------------------

    [Fact]
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
        harness.AuditLog.Calls[0].EntityName.Should().Be("ClinicalSubject");

        var persisted = await harness.Subjects.GetByCodeAsync("SUBJ-001", TestContext.Current.CancellationToken);
        persisted!.Status.Should().Be(SubjectState.Screening, "a blocked enrollment must never mutate status");
    }

    [Fact]
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
    public async Task AdvanceStateAsync_TargetingStateOtherThanEnrolled_NeverConsultsComplianceGate()
    {
        // The Rule 2 gate is scoped to `target == Enrolled` only -- a non-compliant (or entirely unknown)
        // site must never block any other transition.
        var harness = new Harness();
        var siteCode = new SiteCode("SITE-NONCOMPLIANT");
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001", siteCode);

        var result = await harness.Service.AdvanceStateAsync(subject.SubjectCode.Value,
            new SubjectStateUpdateRequest(Status: "WITHDRAWN", State: null, WithdrawalKind: "WithdrawalBySubject"), "Subject withdrew consent",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        subject.WithdrawalReason.Should().Be("Subject withdrew consent");
        subject.WithdrawnAt.Should().Be(harness.Clock.UtcNow);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceStateAsync_EnrolledToActive_SucceedsAndSynchronizesKernelStatus()
    {
        var sync = new FakeKernelSynchronizer();
        var harness = new Harness { KernelSync = sync };
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-SINGLE-ARM", status: SubjectState.Enrolled);

        var result = await harness.Service.AdvanceStateAsync("SUBJ-SINGLE-ARM",
            new SubjectStateUpdateRequest(Status: "ACTIVE", State: null), "Single-arm protocol treatment started",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("ACTIVE");
        subject.Status.Should().Be(SubjectState.Active);
        sync.StatusCalls.Should().ContainSingle().Which.Should().Be(("STUDY-001", "SUBJ-SINGLE-ARM", "Active", "Single-arm protocol treatment started"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NotARealDisposition")]
    public async Task AdvanceStateAsync_ToWithdrawn_WithoutValidWithdrawalKind_ReturnsInvalidWithdrawalKindAndLeavesStateUnchanged(string? withdrawalKind)
    {
        var sync = new FakeKernelSynchronizer();
        var harness = new Harness { KernelSync = sync };
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-WD-BAD");

        var result = await harness.Service.AdvanceStateAsync("SUBJ-WD-BAD",
            new SubjectStateUpdateRequest(Status: "WITHDRAWN", State: null, WithdrawalKind: withdrawalKind), "Withdrew consent",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_WITHDRAWAL_KIND");
        subject.Status.Should().Be(SubjectState.Screening);
        subject.WithdrawnAt.Should().BeNull();
        sync.WithdrawalCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AdvanceStateAsync_ToWithdrawn_WithoutStepUpSignatureWhenKernelSkipped_ReturnsUnauthorized()
    {
        var verifier = new StubWithdrawalSignatureVerifier(
            Result<bool>.Failure(DomainError.Unauthorized("Step-up electronic signature required.", "Step-up electronic signature required.")));
        var harness = new Harness { SignatureVerifier = verifier };
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-WD-NOSIG");

        var result = await harness.Service.AdvanceStateAsync("SUBJ-WD-NOSIG",
            new SubjectStateUpdateRequest(Status: "WITHDRAWN", State: null, WithdrawalKind: "AdverseEvent"), "Discontinued due to AE",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        verifier.Calls.Should().Be(1);
        subject.Status.Should().Be(SubjectState.Screening);
        subject.WithdrawnAt.Should().BeNull();
    }

    [Fact]
    public async Task AdvanceStateAsync_WhenKernelRefusesTransition_DoesNotMutateLocalSubjectOrAudit()
    {
        var sync = new FakeKernelSynchronizer
        {
            FailWith = DomainError.Forbidden("K.Authorization.Denied", "Caller lacks enrollment.change_status."),
        };
        var harness = new Harness { KernelSync = sync };
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-KSYNC-FAIL", status: SubjectState.Enrolled);

        var result = await harness.Service.AdvanceStateAsync("SUBJ-KSYNC-FAIL",
            new SubjectStateUpdateRequest(Status: "ACTIVE", State: null), "Attempt active",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("K.Authorization.Denied");
        subject.Status.Should().Be(SubjectState.Enrolled);
        harness.AuditLog.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AdvanceStateAsync_ExecutionOnlyStates_DoNotMutateKernelEnrollmentStatus()
    {
        var sync = new FakeKernelSynchronizer();
        var harness = new Harness
        {
            KernelSync = sync,
            AuditContext = new FakeCurrentAuditContextAccessor { UserId = "pi-1" },
        };
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-EXEC-ONLY", status: SubjectState.Enrolled);
        subject.TransitionTo(SubjectState.Randomized).IsSuccess.Should().BeTrue();

        var reconsent = await harness.Service.AdvanceStateAsync("SUBJ-EXEC-ONLY",
            new SubjectStateUpdateRequest(Status: "RECONSENT_REQUIRED", State: null), "Protocol amendment requires re-consent",
            TestContext.Current.CancellationToken);
        reconsent.IsSuccess.Should().BeTrue();

        var unblind = await harness.Service.AdvanceStateAsync("SUBJ-EXEC-ONLY",
            new SubjectStateUpdateRequest(Status: "UNBLINDED", State: null), UnblindingReason,
            TestContext.Current.CancellationToken);
        unblind.IsSuccess.Should().BeTrue();

        sync.StatusCalls.Should().BeEmpty();
        sync.WithdrawalCalls.Should().BeEmpty();
    }

    // -- EvaluateAndTransitionScreeningAsync: not-found ------------------------------------------

    [Fact]
    public async Task EvaluateAndTransitionScreeningAsync_GivenSubjectNotFound_ReturnsNotFoundError()
    {
        var harness = new Harness { ScreeningEligibility = (_, _) => Task.FromResult(true) };

        var result = await harness.Service.EvaluateAndTransitionScreeningAsync(
            "SUBJ-DOES-NOT-EXIST", TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SUBJECT_NOT_FOUND");
    }

    [Fact]
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
    public async Task EvaluateAndTransitionScreeningAsync_GivenNoEvaluatorConfigured_ThrowsRatherThanGuessingAVerdict()
    {
        var harness = new Harness(); // ScreeningEligibility left null
        var subject = await SeedSubjectAsync(harness.Subjects, "SUBJ-001");

        var act = () => harness.Service.EvaluateAndTransitionScreeningAsync(
            subject.SubjectCode.Value, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
