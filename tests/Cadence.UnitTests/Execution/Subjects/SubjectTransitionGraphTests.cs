using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// Full 9x9 = 81-permutation coverage for `MIG-SUBJ-002`'s `SubjectTransitionGraph.Guard`
/// (SCN-003), plus the null-`current` and `ReconsentRequired` fan-out boundary checks.
/// </summary>
public class SubjectTransitionGraphTests
{
    private static readonly SubjectState[] AllStates =
    [
        SubjectState.Screening,
        SubjectState.ScreenFailed,
        SubjectState.Enrolled,
        SubjectState.Randomized,
        SubjectState.Active,
        SubjectState.Unblinded,
        SubjectState.Completed,
        SubjectState.Withdrawn,
        SubjectState.ReconsentRequired,
    ];

    private static readonly IReadOnlyDictionary<SubjectState, HashSet<SubjectState>> Allowed = new Dictionary<SubjectState, HashSet<SubjectState>>
    {
        [SubjectState.Screening] = [SubjectState.ScreenFailed, SubjectState.Enrolled, SubjectState.Withdrawn, SubjectState.ReconsentRequired],
        [SubjectState.Enrolled] = [SubjectState.Randomized, SubjectState.Active, SubjectState.Withdrawn, SubjectState.ReconsentRequired],
        [SubjectState.Randomized] = [SubjectState.Active, SubjectState.Withdrawn, SubjectState.Unblinded, SubjectState.ReconsentRequired],
        [SubjectState.Active] = [SubjectState.Completed, SubjectState.Withdrawn, SubjectState.Unblinded, SubjectState.ReconsentRequired],
        [SubjectState.Unblinded] = [SubjectState.Withdrawn, SubjectState.Completed, SubjectState.ReconsentRequired],
        [SubjectState.ReconsentRequired] =
        [
            SubjectState.Screening, SubjectState.Enrolled, SubjectState.Randomized,
            SubjectState.Active, SubjectState.Unblinded, SubjectState.Completed, SubjectState.Withdrawn,
        ],
        [SubjectState.ScreenFailed] = [],
        [SubjectState.Completed] = [],
        [SubjectState.Withdrawn] = [],
    };

    public static IEnumerable<object[]> AllTransitionPermutations()
    {
        foreach (var current in AllStates)
        {
            foreach (var target in AllStates)
            {
                bool expectedSuccess = current == target || Allowed[current].Contains(target);
                yield return [current, target, expectedSuccess];
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllTransitionPermutations))]
    [GxPRequirement("PRD-SUB-001")]
    public void Guard_FullTransitionMatrix_MatchesSpecGraph(SubjectState current, SubjectState target, bool expectedSuccess)
    {
        var result = SubjectTransitionGraph.Guard(current, target);

        result.IsSuccess.Should().Be(expectedSuccess);
        if (expectedSuccess)
        {
            result.Value.Should().Be(target);
        }
        else
        {
            result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
        }
    }

    [Theory]
    [InlineData(SubjectState.Screening)]
    [InlineData(SubjectState.ScreenFailed)]
    [InlineData(SubjectState.Enrolled)]
    [InlineData(SubjectState.Randomized)]
    [InlineData(SubjectState.Active)]
    [InlineData(SubjectState.Unblinded)]
    [InlineData(SubjectState.Completed)]
    [InlineData(SubjectState.Withdrawn)]
    [InlineData(SubjectState.ReconsentRequired)]
    [GxPRequirement("PRD-SUB-001")]
    public void Guard_SameStateNoOp_AlwaysSucceeds(SubjectState state)
    {
        var result = SubjectTransitionGraph.Guard(state, state);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(state);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Guard_NullCurrentTargetingScreening_Succeeds()
    {
        var result = SubjectTransitionGraph.Guard(null, SubjectState.Screening);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(SubjectState.Screening);
    }

    [Theory]
    [InlineData(SubjectState.ScreenFailed)]
    [InlineData(SubjectState.Enrolled)]
    [InlineData(SubjectState.Randomized)]
    [InlineData(SubjectState.Active)]
    [InlineData(SubjectState.Unblinded)]
    [InlineData(SubjectState.Completed)]
    [InlineData(SubjectState.Withdrawn)]
    [InlineData(SubjectState.ReconsentRequired)]
    [GxPRequirement("PRD-SUB-001")]
    public void Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(SubjectState target)
    {
        var result = SubjectTransitionGraph.Guard(null, target);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Guard_ReconsentRequiredFanOut_CoversEveryOtherState()
    {
        var reachable = new HashSet<SubjectState>();
        foreach (var target in AllStates)
        {
            if (target == SubjectState.ReconsentRequired) continue;
            if (SubjectTransitionGraph.Guard(SubjectState.ReconsentRequired, target).IsSuccess)
            {
                reachable.Add(target);
            }
        }

        reachable.Should().HaveCount(7);
        reachable.Should().BeEquivalentTo(
        [
            SubjectState.Screening,
            SubjectState.Enrolled,
            SubjectState.Randomized,
            SubjectState.Active,
            SubjectState.Unblinded,
            SubjectState.Completed,
            SubjectState.Withdrawn,
        ]);
    }
}
