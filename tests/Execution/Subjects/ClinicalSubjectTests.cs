using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel.Time;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// Coverage for `MIG-SUBJ-004`'s `ClinicalSubject` aggregate root: factory initial-state (SCN-001,
/// the field-value subset of SCN-007), the pre/post-randomization mutation lock matrix (SCN-004),
/// unblind/withdraw field capture with a fake `IClock` (SCN-008), `Withdraw`'s same-state no-op
/// idempotency, illegal-transition failure propagation, and `TransitionTo`'s thin delegation to
/// `SubjectTransitionGraph.Guard` (the full 81-permutation matrix is `MIG-SUBJ-002`'s
/// `SubjectTransitionGraphTests`, not duplicated here).
/// @req:PRD-SUB-001
/// </summary>
public class ClinicalSubjectTests
{
    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static ClinicalSubject CreateScreeningSubject(
        SiteCode? siteCode = null,
        int enrollmentIndex = 1,
        string? encryptedDemographics = "cipher-text",
        IReadOnlyDictionary<string, object?>? stratFactors = null) =>
        ClinicalSubject.Create(
            new SubjectCode("SUBJ-001"),
            new StudyCode("STUDY-001"),
            siteCode,
            enrollmentIndex,
            encryptedDemographics,
            stratFactors);

    // -- Factory initial-state (SCN-001, SCN-007 field-value subset) --------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Create_ProducesSubjectInScreeningWithSuppliedFields()
    {
        var siteCode = new SiteCode("SITE-042");
        var stratFactors = new Dictionary<string, object?> { ["age_band"] = "18-40" };

        var subject = ClinicalSubject.Create(
            new SubjectCode("SUBJ-001"),
            new StudyCode("STUDY-001"),
            siteCode,
            enrollmentIndex: 7,
            encryptedDemographics: "cipher-text",
            stratFactors: stratFactors);

        subject.Status.Should().Be(SubjectState.Screening);
        subject.SubjectCode.Should().Be(new SubjectCode("SUBJ-001"));
        subject.StudyCode.Should().Be(new StudyCode("STUDY-001"));
        subject.SiteCode.Should().Be(siteCode);
        subject.EnrollmentIndex.Should().Be(7);
        subject.EncryptedDemographics.Should().Be("cipher-text");
        subject.StratFactors.Should().ContainKey("age_band").WhoseValue.Should().Be("18-40");
        subject.ActiveProtocolVersion.Should().Be("1.0.0");
        subject.IsUnblinded.Should().BeFalse();
        subject.Id.Should().NotBe(default(SubjectId));
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Create_WithNullSiteCodeAndNullStratFactors_LeavesThemEmpty()
    {
        var subject = ClinicalSubject.Create(
            new SubjectCode("SUBJ-002"),
            new StudyCode("STUDY-001"),
            siteCode: null,
            enrollmentIndex: 1,
            encryptedDemographics: null,
            stratFactors: null);

        subject.SiteCode.Should().BeNull();
        subject.StratFactors.Should().BeEmpty();
        subject.EncryptedDemographics.Should().BeNull();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Create_TwoSubjects_ProduceDistinctIds()
    {
        var first = CreateScreeningSubject();
        var second = CreateScreeningSubject();

        first.Id.Should().NotBe(second.Id);
    }

    // -- Pre/post-randomization mutation lock matrix (SCN-004) ---------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void UpdateStratFactors_PreRandomization_Succeeds()
    {
        var subject = CreateScreeningSubject();
        var newFactors = new Dictionary<string, object?> { ["region"] = "EU" };

        var result = subject.UpdateStratFactors(newFactors);

        result.IsSuccess.Should().BeTrue();
        subject.StratFactors.Should().ContainKey("region").WhoseValue.Should().Be("EU");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void UpdateDemographics_PreRandomization_Succeeds()
    {
        var subject = CreateScreeningSubject();

        var result = subject.UpdateDemographics("new-cipher-text");

        result.IsSuccess.Should().BeTrue();
        subject.EncryptedDemographics.Should().Be("new-cipher-text");
    }

    public static IEnumerable<object[]> PostRandomizationStates()
    {
        yield return [SubjectState.Randomized];
        yield return [SubjectState.Active];
        yield return [SubjectState.Completed];
        yield return [SubjectState.Unblinded];
        yield return [SubjectState.Withdrawn];
    }

    [Theory]
    [MemberData(nameof(PostRandomizationStates))]
    [GxPRequirement("PRD-SUB-001")]
    public void IsPostRandomization_ForLockedStates_ReturnsTrue(SubjectState state)
    {
        var subject = DriveToState(state);

        subject.IsPostRandomization().Should().BeTrue();
    }

    [Theory]
    [InlineData(SubjectState.Screening)]
    [InlineData(SubjectState.Enrolled)]
    [GxPRequirement("PRD-SUB-001")]
    public void IsPostRandomization_ForUnlockedStates_ReturnsFalse(SubjectState state)
    {
        var subject = DriveToState(state);

        subject.IsPostRandomization().Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(PostRandomizationStates))]
    [GxPRequirement("PRD-SUB-001")]
    public void UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation(SubjectState state)
    {
        var subject = DriveToState(state);
        var original = new Dictionary<string, object?>(subject.StratFactors);

        var result = subject.UpdateStratFactors(new Dictionary<string, object?> { ["region"] = "APAC" });

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("LOCKED_FACTOR_MUTATION");
        subject.StratFactors.Should().BeEquivalentTo(original);
    }

    [Theory]
    [MemberData(nameof(PostRandomizationStates))]
    [GxPRequirement("PRD-SUB-001")]
    public void UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation(SubjectState state)
    {
        var subject = DriveToState(state);
        var original = subject.EncryptedDemographics;

        var result = subject.UpdateDemographics("attempted-overwrite");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("LOCKED_FACTOR_MUTATION");
        subject.EncryptedDemographics.Should().Be(original);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void ClearDemographics_PreRandomization_Succeeds()
    {
        var subject = CreateScreeningSubject();

        var result = subject.ClearDemographics();

        result.IsSuccess.Should().BeTrue();
        subject.EncryptedDemographics.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(PostRandomizationStates))]
    [GxPRequirement("PRD-SUB-001")]
    public void ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked(SubjectState state)
    {
        var subject = DriveToState(state);

        var result = subject.ClearDemographics();

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SOFT_DELETE_BLOCKED");
    }

    [Theory]
    [MemberData(nameof(PostRandomizationStates))]
    [GxPRequirement("PRD-SUB-001")]
    public void ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes(SubjectState state)
    {
        var clearSubject = DriveToState(state);
        var mutateSubject = DriveToState(state);

        var clearResult = clearSubject.ClearDemographics();
        var mutateResult = mutateSubject.UpdateStratFactors(new Dictionary<string, object?>());

        clearResult.Error!.Code.Should().NotBe(mutateResult.Error!.Code);
        clearResult.Error!.Code.Should().Be("SOFT_DELETE_BLOCKED");
        mutateResult.Error!.Code.Should().Be("LOCKED_FACTOR_MUTATION");
    }

    // -- TransitionTo delegates to SubjectTransitionGraph.Guard ---------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void TransitionTo_LegalTransition_UpdatesStatusAndReturnsSuccess()
    {
        var subject = CreateScreeningSubject();

        var result = subject.TransitionTo(SubjectState.Enrolled);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(SubjectState.Enrolled);
        subject.Status.Should().Be(SubjectState.Enrolled);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void TransitionTo_IllegalTransition_LeavesStatusUnchangedAndReturnsGuardFailure()
    {
        var subject = CreateScreeningSubject();

        var result = subject.TransitionTo(SubjectState.Active);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
        subject.Status.Should().Be(SubjectState.Screening);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void TransitionTo_SameState_NoOpSucceedsViaGuard()
    {
        var subject = CreateScreeningSubject();

        var result = subject.TransitionTo(SubjectState.Screening);

        result.IsSuccess.Should().BeTrue();
        subject.Status.Should().Be(SubjectState.Screening);
    }

    // -- Unblind/Withdraw field capture (SCN-008) using a fake IClock --------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Unblind_OnRandomizedSubject_SetsAllUnblindFieldsFromClock()
    {
        var subject = DriveToState(SubjectState.Randomized);
        var clockTime = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(clockTime);

        var result = subject.Unblind("doctor_smith", "SAE-Life-Threatening-Event", clock);

        result.IsSuccess.Should().BeTrue();
        subject.Status.Should().Be(SubjectState.Unblinded);
        subject.IsUnblinded.Should().BeTrue();
        subject.UnblindedBy.Should().Be("doctor_smith");
        subject.UnblindedReason.Should().Be("SAE-Life-Threatening-Event");
        subject.UnblindedAt.Should().Be(clockTime);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Withdraw_AfterUnblinding_SetsWithdrawalFieldsFromClock()
    {
        var subject = DriveToState(SubjectState.Randomized);
        subject.Unblind("doctor_smith", "SAE-Life-Threatening-Event", new FakeClock(DateTimeOffset.UtcNow));
        var withdrawTime = new DateTimeOffset(2026, 3, 2, 9, 30, 0, TimeSpan.Zero);
        var clock = new FakeClock(withdrawTime);

        var result = subject.Withdraw("Subject withdrew consent", clock);

        result.IsSuccess.Should().BeTrue();
        subject.Status.Should().Be(SubjectState.Withdrawn);
        subject.WithdrawalReason.Should().Be("Subject withdrew consent");
        subject.WithdrawnAt.Should().Be(withdrawTime);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Withdraw_CalledTwiceOnAlreadyWithdrawnSubject_SucceedsSilentlyAsNoOp()
    {
        var subject = DriveToState(SubjectState.Withdrawn);

        var result = subject.Withdraw("second withdrawal attempt", new FakeClock(DateTimeOffset.UtcNow));

        result.IsSuccess.Should().BeTrue();
        subject.Status.Should().Be(SubjectState.Withdrawn);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Unblind_OnScreeningSubject_ReturnsFailureWithoutThrowing()
    {
        var subject = CreateScreeningSubject();

        // No try/catch: a throw here would fail the test on its own, proving the failure
        // propagates through the Result<bool> return rather than as an exception.
        var result = subject.Unblind("doctor_smith", "attempted-early-unblind", new FakeClock(DateTimeOffset.UtcNow));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
        subject.Status.Should().Be(SubjectState.Screening);
        subject.IsUnblinded.Should().BeFalse();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Withdraw_OnScreenFailedSubject_ReturnsFailureWithoutThrowing()
    {
        var subject = DriveToState(SubjectState.ScreenFailed);

        // No try/catch: a throw here would fail the test on its own, proving the failure
        // propagates through the Result<bool> return rather than as an exception.
        var result = subject.Withdraw("attempted-withdraw-from-terminal-state", new FakeClock(DateTimeOffset.UtcNow));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
        subject.Status.Should().Be(SubjectState.ScreenFailed);
        subject.WithdrawnAt.Should().BeNull();
    }

    /// <summary>Drives a fresh Screening subject through the shortest legal path to `target` via
    /// `TransitionTo`, so post-randomization-lock and unblind/withdraw tests exercise a realistic
    /// state history rather than an aggregate teleported into an unreachable configuration.</summary>
    private static ClinicalSubject DriveToState(SubjectState target)
    {
        var subject = CreateScreeningSubject();
        if (target == SubjectState.Screening) return subject;

        if (target == SubjectState.ScreenFailed)
        {
            subject.TransitionTo(SubjectState.ScreenFailed);
            return subject;
        }

        subject.TransitionTo(SubjectState.Enrolled);
        if (target == SubjectState.Enrolled) return subject;

        subject.TransitionTo(SubjectState.Randomized);
        if (target == SubjectState.Randomized) return subject;

        if (target == SubjectState.Unblinded)
        {
            subject.TransitionTo(SubjectState.Unblinded);
            return subject;
        }

        subject.TransitionTo(SubjectState.Active);
        if (target == SubjectState.Active) return subject;

        if (target == SubjectState.Completed)
        {
            subject.TransitionTo(SubjectState.Completed);
            return subject;
        }

        if (target == SubjectState.Withdrawn)
        {
            subject.TransitionTo(SubjectState.Withdrawn);
            return subject;
        }

        throw new ArgumentOutOfRangeException(nameof(target), target, "Unreachable state in DriveToState helper.");
    }
}
