using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Apps.Execution.Domain.Enums;
using Cadence.Apps.Execution.Domain.Exceptions;
using Cadence.Apps.Execution.Domain.Rules;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests;

/// <summary>
/// Parity port of `test_pure_python_transition_guard` (test_subject_randomization_lifecycle.py):
/// exhaustively validates all 9x9 = 81 state transition permutations against the regulated
/// subject state machine.
/// @req:PRD-SYS-042
/// @req:PRD-SUB-001
/// </summary>
public class SubjectStateMachineTests
{
    private static readonly SubjectState[] AllStates = Enum.GetValues<SubjectState>();

    private static readonly IReadOnlyDictionary<SubjectState, IReadOnlySet<SubjectState>> ExpectedAllowedTargets =
        new Dictionary<SubjectState, IReadOnlySet<SubjectState>>
        {
            [SubjectState.Screening] = new HashSet<SubjectState>
            {
                SubjectState.ScreenFailed, SubjectState.Enrolled, SubjectState.Withdrawn, SubjectState.ReconsentRequired,
            },
            [SubjectState.Enrolled] = new HashSet<SubjectState>
            {
                SubjectState.Randomized, SubjectState.Withdrawn, SubjectState.ReconsentRequired,
            },
            [SubjectState.Randomized] = new HashSet<SubjectState>
            {
                SubjectState.Active, SubjectState.Withdrawn, SubjectState.Unblinded, SubjectState.ReconsentRequired,
            },
            [SubjectState.Active] = new HashSet<SubjectState>
            {
                SubjectState.Completed, SubjectState.Withdrawn, SubjectState.Unblinded, SubjectState.ReconsentRequired,
            },
            [SubjectState.Unblinded] = new HashSet<SubjectState>
            {
                SubjectState.Completed, SubjectState.Withdrawn, SubjectState.ReconsentRequired,
            },
            [SubjectState.ReconsentRequired] = new HashSet<SubjectState>
            {
                SubjectState.Screening, SubjectState.Enrolled, SubjectState.Randomized,
                SubjectState.Active, SubjectState.Unblinded, SubjectState.Completed, SubjectState.Withdrawn,
            },
            [SubjectState.ScreenFailed] = new HashSet<SubjectState>(),
            [SubjectState.Completed] = new HashSet<SubjectState>(),
            [SubjectState.Withdrawn] = new HashSet<SubjectState>(),
        };

    public static IEnumerable<object[]> AllTransitionPermutations()
    {
        foreach (var current in AllStates)
        {
            foreach (var target in AllStates)
            {
                yield return [current, target];
            }
        }
    }

    [Theory(DisplayName = "CanTransition_ValidatesAllNineByNinePermutations_AgainstExpectedGraph")]
    [MemberData(nameof(AllTransitionPermutations))]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-042")]
    [GxPRequirement("PRD-SUB-001")]
    public void CanTransitionValidatesAllNineByNinePermutationsAgainstExpectedGraph(SubjectState current, SubjectState target)
    {
        // Arrange
        bool expected = current == target || ExpectedAllowedTargets[current].Contains(target);

        // Act
        bool actual = SubjectStateMachine.CanTransition(current, target);

        // Assert
        actual.Should().Be(expected, $"transition {current} -> {target} should evaluate to {expected}");
    }

    [Fact(DisplayName = "AllTransitionPermutations_TotalEightyOne_MatchingNineStateStateMachine")]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-042")]
    public void AllTransitionPermutationsTotalEightyOneMatchingNineStateStateMachine()
    {
        AllStates.Should().HaveCount(9);
        AllTransitionPermutations().Should().HaveCount(81);
    }

    [Theory(DisplayName = "CanTransition_SelfTransition_IsAlwaysIdempotentlyTrue")]
    [MemberData(nameof(SelfTransitions))]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-042")]
    public void CanTransitionSelfTransitionIsAlwaysIdempotentlyTrue(SubjectState state)
    {
        SubjectStateMachine.CanTransition(state, state).Should().BeTrue();
    }

    public static IEnumerable<object[]> SelfTransitions() => AllStates.Select(s => new object[] { s });

    [Theory(DisplayName = "CanTransition_TerminalStates_ForbidAllOutgoingNonSelfTransitions")]
    [InlineData(SubjectState.ScreenFailed)]
    [InlineData(SubjectState.Completed)]
    [InlineData(SubjectState.Withdrawn)]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-042")]
    [GxPRequirement("PRD-SUB-001")]
    public void CanTransitionTerminalStatesForbidAllOutgoingNonSelfTransitions(SubjectState terminalState)
    {
        foreach (var target in AllStates.Where(s => s != terminalState))
        {
            SubjectStateMachine.CanTransition(terminalState, target).Should().BeFalse(
                $"terminal state {terminalState} must forbid transition to {target}");
        }
    }

    [Fact(DisplayName = "EnsureValidTransition_LegalTransition_DoesNotThrow")]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-042")]
    public void EnsureValidTransitionLegalTransitionDoesNotThrow()
    {
        var act = () => SubjectStateMachine.EnsureValidTransition(SubjectState.Screening, SubjectState.Enrolled);
        act.Should().NotThrow();
    }

    [Fact(DisplayName = "EnsureValidTransition_IllegalTransition_ThrowsInvalidStateTransitionExceptionWithStates")]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-042")]
    [GxPRequirement("PRD-SUB-001")]
    public void EnsureValidTransitionIllegalTransitionThrowsInvalidStateTransitionExceptionWithStates()
    {
        var act = () => SubjectStateMachine.EnsureValidTransition(SubjectState.Screening, SubjectState.Active);

        act.Should().Throw<InvalidStateTransitionException>()
            .Which.Should().Match<InvalidStateTransitionException>(
                ex => ex.CurrentState == SubjectState.Screening && ex.TargetState == SubjectState.Active);
    }

    [Fact(DisplayName = "EnsureValidTransition_FromTerminalState_ThrowsInvalidStateTransitionException")]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-042")]
    public void EnsureValidTransitionFromTerminalStateThrowsInvalidStateTransitionException()
    {
        var act = () => SubjectStateMachine.EnsureValidTransition(SubjectState.Completed, SubjectState.Active);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    /// <summary>
    /// Validates post-randomization lock classification for all nine subject lifecycle states.
    /// @req:PRD-SYS-001
    /// @req:PRD-SYS-042
    /// </summary>
    [Theory(DisplayName = "IsLockedPostRandomization_ReturnsTrue_OnlyForTheFiveLockedStates")]
    [InlineData(SubjectState.Randomized, true)]
    [InlineData(SubjectState.Active, true)]
    [InlineData(SubjectState.Completed, true)]
    [InlineData(SubjectState.Unblinded, true)]
    [InlineData(SubjectState.Withdrawn, true)]
    [InlineData(SubjectState.Screening, false)]
    [InlineData(SubjectState.ScreenFailed, false)]
    [InlineData(SubjectState.Enrolled, false)]
    [InlineData(SubjectState.ReconsentRequired, false)]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-SYS-042")]
    public void IsLockedPostRandomizationReturnsTrueOnlyForTheFiveLockedStates(
        SubjectState state, bool expectedLocked)
    {
        SubjectStateMachine.IsLockedPostRandomization(state).Should().Be(expectedLocked);
    }

    /// <summary>
    /// Validates the default locked-factor exception emits a non-empty regulatory safety message.
    /// @req:PRD-SYS-001
    /// </summary>
    [Fact(DisplayName = "LockedFactorMutationException_DefaultConstructor_CarriesRegulatoryMessage")]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-001")]
    public void LockedFactorMutationExceptionDefaultConstructorCarriesRegulatoryMessage()
    {
        var exception = new LockedFactorMutationException();
        exception.Message.Should().NotBeNullOrWhiteSpace();
    }
}
