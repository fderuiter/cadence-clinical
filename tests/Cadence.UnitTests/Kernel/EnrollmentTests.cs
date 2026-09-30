using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.References;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Participants, subject codes and the enrollment lifecycle in the domain (spec §7.4, #4776): the transition table,
/// the time rules, the bitemporal status history and the content hash attestations sign. The database repeats the
/// lifecycle rules; <c>KernelEnrollmentPostgresTests</c> covers that side and the handlers.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EnrollmentTests
{
    private static readonly DateTimeOffset T0 = FakeClock.DefaultStart;

    private static readonly HashSet<(EnrollmentStatus From, EnrollmentStatus To)> Allowed =
    [
        (EnrollmentStatus.Screening, EnrollmentStatus.ScreenFailed),
        (EnrollmentStatus.Screening, EnrollmentStatus.Enrolled),
        (EnrollmentStatus.Screening, EnrollmentStatus.Withdrawn),
        (EnrollmentStatus.Enrolled, EnrollmentStatus.Randomized),
        (EnrollmentStatus.Enrolled, EnrollmentStatus.Active),
        (EnrollmentStatus.Enrolled, EnrollmentStatus.Withdrawn),
        (EnrollmentStatus.Randomized, EnrollmentStatus.Active),
        (EnrollmentStatus.Randomized, EnrollmentStatus.Withdrawn),
        (EnrollmentStatus.Active, EnrollmentStatus.Completed),
        (EnrollmentStatus.Active, EnrollmentStatus.Withdrawn),
    ];

    public static TheoryData<EnrollmentStatus, EnrollmentStatus, bool> Transitions()
    {
        var data = new TheoryData<EnrollmentStatus, EnrollmentStatus, bool>();
        foreach (var from in Enum.GetValues<EnrollmentStatus>())
        {
            foreach (var to in Enum.GetValues<EnrollmentStatus>())
            {
                data.Add(from, to, Allowed.Contains((from, to)));
            }
        }

        return data;
    }

    /// <summary>Only the moves in spec §7.4 are allowed; ScreenFailed, Completed and Withdrawn are terminal.</summary>
    [Theory(DisplayName = "EnrollmentTransitions_FollowTheLifecycle")]
    [MemberData(nameof(Transitions))]
    [GxPRequirement("PRD-SUB-001")]
    public void EnrollmentTransitionsFollowTheLifecycle(EnrollmentStatus from, EnrollmentStatus to, bool allowed)
    {
        EnrollmentTransitions.IsAllowed(from, to).Should().Be(allowed);
        EnrollmentTransitions.IsTerminal(from).Should().Be(
            from is EnrollmentStatus.ScreenFailed or EnrollmentStatus.Completed or EnrollmentStatus.Withdrawn);
        EnrollmentTransitions.IsActive(from).Should().Be(
            from is EnrollmentStatus.Screening or EnrollmentStatus.Enrolled or EnrollmentStatus.Randomized or EnrollmentStatus.Active);
    }

    /// <summary>A subject code is 1 to 64 printable characters without surrounding whitespace.</summary>
    [Theory(DisplayName = "SubjectCode_Validation")]
    [InlineData("SUBJ-001", true)]
    [InlineData("101 002", true)]
    [InlineData("", false)]
    [InlineData(" SUBJ-001", false)]
    [InlineData("SUBJ-001 ", false)]
    [InlineData("SUBJ\n001", false)]
    [InlineData("SUBJ\u0000001", false)]
    [InlineData(null, false)]
    public void SubjectCodeValidation(string? value, bool valid)
    {
        SubjectCode.TryFrom(value, out var code).Should().Be(valid);
        code.IsValid.Should().Be(valid);
        if (valid)
        {
            code.Value.Should().Be(value);
            SubjectCode.From(value).Should().Be(code);
        }
        else
        {
            FluentActions.Invoking(() => SubjectCode.From(value)).Should().Throw<ArgumentException>();
        }
    }

    /// <summary>The longest code is 64 characters, and <c>default</c> is not a code.</summary>
    [Fact(DisplayName = "SubjectCode_LengthAndDefault")]
    public void SubjectCodeLengthAndDefault()
    {
        SubjectCode.TryFrom(new string('A', SubjectCode.MaxLength), out _).Should().BeTrue();
        SubjectCode.TryFrom(new string('A', SubjectCode.MaxLength + 1), out _).Should().BeFalse();
        default(SubjectCode).IsValid.Should().BeFalse();
        default(SubjectCode).ToString().Should().BeEmpty();
    }

    /// <summary>A subject code round-trips as a JSON string, and an invalid one is refused on read.</summary>
    [Fact(DisplayName = "SubjectCode_SerializesAsJsonString")]
    public void SubjectCodeSerializesAsJsonString()
    {
        var json = JsonSerializer.Serialize(SubjectCode.From("SUBJ-001"));

        json.Should().Be("\"SUBJ-001\"");
        JsonSerializer.Deserialize<SubjectCode>(json).Should().Be(SubjectCode.From("SUBJ-001"));
        FluentActions.Invoking(() => JsonSerializer.Deserialize<SubjectCode>("\" padded\"")).Should().Throw<JsonException>();
    }

    /// <summary>Enrollments are a registered node kind, so attestations and references can name them.</summary>
    [Fact(DisplayName = "NodeKinds_RegisterEnrollment")]
    public void NodeKindsRegisterEnrollment()
    {
        NodeKinds.Of<EnrollmentId>().Should().Be(NodeKinds.Enrollment);
        NodeKinds.IdentifierTypeOf(NodeKinds.Enrollment).Should().Be<EnrollmentId>();
    }

    /// <summary>Screening starts the enrollment with one open status row from the screening time.</summary>
    [Fact(DisplayName = "Screen_StartsInScreeningWithOneRow")]
    public void ScreenStartsInScreeningWithOneRow()
    {
        var enrollment = Screen(T0, T0.AddMinutes(5));

        enrollment.Status.Should().Be(EnrollmentStatus.Screening);
        enrollment.StatusSince.Should().Be(T0);
        enrollment.EnrollmentNumber.Should().BeNull();
        enrollment.WithdrawalKind.Should().BeNull();
        enrollment.WithdrawalAttestationId.Should().BeNull();
        enrollment.Periods.Should().ContainSingle();
        var row = enrollment.Periods[0];
        row.Status.Should().Be(EnrollmentStatus.Screening);
        row.EnrollmentId.Should().Be(enrollment.Id);
        row.ParticipantId.Should().Be(enrollment.ParticipantId);
        row.StudySiteId.Should().Be(enrollment.StudySiteId);
        row.TenantId.Should().Be(enrollment.TenantId);
        row.Period.Valid.Start.Should().Be(T0);
        row.Period.Valid.IsOpen.Should().BeTrue();
        row.Period.Recorded.Start.Should().Be(T0.AddMinutes(5));
    }

    /// <summary>A screening time after the recording time, or a missing code, is refused.</summary>
    [Fact(DisplayName = "Screen_RefusesFutureTimeAndMissingCode")]
    public void ScreenRefusesFutureTimeAndMissingCode()
    {
        Enrollment.Screen(
                EnrollmentId.New(), TenantId.New(), ParticipantId.New(), StudyId.New(), StudySiteId.New(),
                SubjectCode.From("S-1"), T0.AddMinutes(1), T0)
            .Error!.Code.Should().Be(ExecutionErrors.TransitionTimeInvalid.Code);

        Enrollment.Screen(
                EnrollmentId.New(), TenantId.New(), ParticipantId.New(), StudyId.New(), StudySiteId.New(),
                default, T0, T0)
            .Error!.Code.Should().Be(ExecutionErrors.SubjectCodeInvalid.Code);
    }

    /// <summary>
    /// A change supersedes the current row and records the old status up to the change and the new one after it, so
    /// the enrollment reads correctly as of any valid and known instant (ADR-2246).
    /// </summary>
    [Fact(DisplayName = "ChangeStatus_KeepsBitemporalHistory")]
    public void ChangeStatusKeepsBitemporalHistory()
    {
        var enrollment = Screen(T0, T0);
        var enrolledAt = T0.AddDays(7);
        var recordedAt = T0.AddDays(8);

        enrollment.ChangeStatus(EnrollmentStatus.Enrolled, enrolledAt, recordedAt, enrollmentNumber: 1).Should().BeNull();

        enrollment.Status.Should().Be(EnrollmentStatus.Enrolled);
        enrollment.StatusSince.Should().Be(enrolledAt);
        enrollment.EnrollmentNumber.Should().Be(1);
        enrollment.Periods.Should().HaveCount(3);
        enrollment.Periods.Count(p => p.Period.Recorded.IsOpen).Should().Be(2);

        // Before the change was recorded, the kernel believed the participant was still screening.
        enrollment.StatusAsOf(enrolledAt.AddHours(1), recordedAt.AddTicks(-10)).Should().Be(EnrollmentStatus.Screening);
        // Once recorded, the change applies from its effective time.
        enrollment.StatusAsOf(enrolledAt.AddHours(1), recordedAt).Should().Be(EnrollmentStatus.Enrolled);
        enrollment.StatusAsOf(enrolledAt.AddHours(-1), recordedAt).Should().Be(EnrollmentStatus.Screening);
        enrollment.StatusAsOf(T0.AddDays(-1), recordedAt).Should().BeNull();
    }

    /// <summary>
    /// A change must start after the current status and not after it is recorded, and two changes recorded at the
    /// same instant conflict.
    /// </summary>
    [Fact(DisplayName = "CheckTransition_EnforcesTimeRules")]
    public void CheckTransitionEnforcesTimeRules()
    {
        var enrollment = Screen(T0, T0.AddHours(1));

        enrollment.CheckTransition(EnrollmentStatus.Enrolled, T0, T0.AddDays(1))!.Code
            .Should().Be(ExecutionErrors.TransitionTimeInvalid.Code);
        enrollment.CheckTransition(EnrollmentStatus.Enrolled, T0.AddDays(2), T0.AddDays(1))!.Code
            .Should().Be(ExecutionErrors.TransitionTimeInvalid.Code);
        enrollment.CheckTransition(EnrollmentStatus.Enrolled, T0.AddMinutes(30), T0.AddHours(1))!.Code
            .Should().Be(KernelErrors.ConcurrencyConflict.Code);
        enrollment.CheckTransition(EnrollmentStatus.Completed, T0.AddDays(1), T0.AddDays(1))!.Code
            .Should().Be(ExecutionErrors.InvalidTransition.Code);
        enrollment.CheckTransition((EnrollmentStatus)99, T0.AddDays(1), T0.AddDays(1))!.Code
            .Should().Be(ExecutionErrors.InvalidTransition.Code);
        enrollment.CheckTransition(EnrollmentStatus.Enrolled, T0.AddDays(1), T0.AddDays(1)).Should().BeNull();
    }

    /// <summary>Only entering Enrolled takes a number, and withdrawal goes through its own command.</summary>
    [Fact(DisplayName = "ChangeStatus_NumberOnlyOnEnrolmentAndNoWithdrawal")]
    public void ChangeStatusNumberOnlyOnEnrolmentAndNoWithdrawal()
    {
        var enrollment = Screen(T0, T0);

        enrollment.ChangeStatus(EnrollmentStatus.Withdrawn, T0.AddDays(1), T0.AddDays(1))!.Code
            .Should().Be(ExecutionErrors.WithdrawalRequiresCommand.Code);
        FluentActions.Invoking(() => enrollment.ChangeStatus(EnrollmentStatus.Enrolled, T0.AddDays(1), T0.AddDays(1)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => enrollment.ChangeStatus(EnrollmentStatus.Enrolled, T0.AddDays(1), T0.AddDays(1), 0))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => enrollment.ChangeStatus(EnrollmentStatus.ScreenFailed, T0.AddDays(1), T0.AddDays(1), 3))
            .Should().Throw<ArgumentException>();
        enrollment.Status.Should().Be(EnrollmentStatus.Screening);
        enrollment.Periods.Should().ContainSingle();
    }

    /// <summary>
    /// Withdrawal records its kind and attestation and is terminal; the enrollment keeps its number and full history.
    /// </summary>
    [Fact(DisplayName = "Withdraw_IsTerminalAndRecordsAttestation")]
    public void WithdrawIsTerminalAndRecordsAttestation()
    {
        var enrollment = Screen(T0, T0);
        enrollment.ChangeStatus(EnrollmentStatus.Enrolled, T0.AddDays(1), T0.AddDays(1), 4).Should().BeNull();
        enrollment.ChangeStatus(EnrollmentStatus.Active, T0.AddDays(2), T0.AddDays(2)).Should().BeNull();
        var attestation = AttestationId.New();

        enrollment.Withdraw((WithdrawalKind)99, attestation, T0.AddDays(3), T0.AddDays(3))!.Code
            .Should().Be(ExecutionErrors.WithdrawalKindInvalid.Code);
        enrollment.Withdraw(WithdrawalKind.AdverseEvent, attestation, T0.AddDays(3), T0.AddDays(3)).Should().BeNull();

        enrollment.Status.Should().Be(EnrollmentStatus.Withdrawn);
        enrollment.WithdrawalKind.Should().Be(WithdrawalKind.AdverseEvent);
        enrollment.WithdrawalAttestationId.Should().Be(attestation);
        enrollment.EnrollmentNumber.Should().Be(4);
        enrollment.StatusAsOf(T0.AddDays(2).AddHours(1), T0.AddDays(3)).Should().Be(EnrollmentStatus.Active);
        enrollment.CheckTransition(EnrollmentStatus.Active, T0.AddDays(4), T0.AddDays(4))!.Code
            .Should().Be(ExecutionErrors.InvalidTransition.Code);
    }

    /// <summary>
    /// The content hash names the enrollment's identity and current state, so an attestation signed before a change of
    /// status no longer matches after it.
    /// </summary>
    [Fact(DisplayName = "ContentHash_ChangesWithStatus")]
    public void ContentHashChangesWithStatus()
    {
        var enrollment = Screen(T0, T0);
        var screeningHash = enrollment.ContentHash;

        screeningHash.Should().MatchRegex(Cadence.Kernel.Trust.ContentHash.Pattern);
        enrollment.ContentHash.Should().Be(screeningHash, "the hash is deterministic");
        enrollment.SignableTarget.Kind.Should().Be(NodeKinds.Enrollment);
        enrollment.SignableTarget.Id.Should().Be(enrollment.Id.Value);
        enrollment.SignableTarget.ContentHash.Should().Be(screeningHash);

        enrollment.ChangeStatus(EnrollmentStatus.Enrolled, T0.AddDays(1), T0.AddDays(1), 1).Should().BeNull();

        enrollment.ContentHash.Should().NotBe(screeningHash);
    }

    private static Enrollment Screen(DateTimeOffset screenedAt, DateTimeOffset recordedAt) =>
        Enrollment.Screen(
                EnrollmentId.New(), TenantId.New(), ParticipantId.New(), StudyId.New(), StudySiteId.New(),
                SubjectCode.From("SUBJ-001"), screenedAt, recordedAt)
            .Value!;
}
