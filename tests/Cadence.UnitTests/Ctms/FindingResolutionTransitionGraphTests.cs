using System;
using System.Collections.Generic;

using Cadence.Apps.Ctms.Domain;
using Cadence.Core.Domain;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Ctms;

/// <summary>
/// Comprehensive unit tests for FindingResolutionTransitionGraph.
/// Validates 100% of allowed and forbidden transitions across the full 4x4 permutation matrix.
/// </summary>
public sealed class FindingResolutionTransitionGraphTests
{
    private static readonly FindingResolutionStatus[] AllStatuses =
    [
        FindingResolutionStatus.Open,
        FindingResolutionStatus.UnderReview,
        FindingResolutionStatus.Resolved,
        FindingResolutionStatus.Closed
    ];

    public static TheoryData<FindingResolutionStatus, FindingResolutionStatus, bool> TransitionMatrixData()
    {
        var data = new TheoryData<FindingResolutionStatus, FindingResolutionStatus, bool>();

        var allowedTransitions = new HashSet<(FindingResolutionStatus, FindingResolutionStatus)>
        {
            // Open transitions
            (FindingResolutionStatus.Open, FindingResolutionStatus.UnderReview),
            (FindingResolutionStatus.Open, FindingResolutionStatus.Resolved),
            (FindingResolutionStatus.Open, FindingResolutionStatus.Closed),

            // UnderReview transitions
            (FindingResolutionStatus.UnderReview, FindingResolutionStatus.Resolved),
            (FindingResolutionStatus.UnderReview, FindingResolutionStatus.Open),
            (FindingResolutionStatus.UnderReview, FindingResolutionStatus.Closed),

            // Resolved transitions
            (FindingResolutionStatus.Resolved, FindingResolutionStatus.Closed),
            (FindingResolutionStatus.Resolved, FindingResolutionStatus.UnderReview),

            // Closed is terminal (no transitions)
        };

        foreach (var from in AllStatuses)
        {
            foreach (var to in AllStatuses)
            {
                if (from == to)
                {
                    data.Add(from, to, true); // same-state idempotent no-op
                }
                else
                {
                    bool isAllowed = allowedTransitions.Contains((from, to));
                    data.Add(from, to, isAllowed);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TransitionMatrixData))]
    public void Guard_TransitionMatrix_ValidatesAllowedAndRejectsForbidden(
        FindingResolutionStatus current,
        FindingResolutionStatus target,
        bool shouldSucceed)
    {
        var result = FindingResolutionTransitionGraph.Guard(current, target);

        if (shouldSucceed)
        {
            result.IsSuccess.Should().BeTrue($"transition from {current} to {target} should be allowed");
            result.Value.Should().Be(target);
            result.Error.Should().BeNull();
        }
        else
        {
            result.IsFailure.Should().BeTrue($"transition from {current} to {target} should be forbidden");
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("INVALID_FINDING_RESOLUTION_TRANSITION");
            result.Error.Type.Should().Be(ErrorType.Validation);
        }
    }

    [Fact]
    public void Guard_NullCurrent_TargetingOpen_Succeeds()
    {
        var result = FindingResolutionTransitionGraph.Guard(null, FindingResolutionStatus.Open);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(FindingResolutionStatus.Open);
    }

    [Theory]
    [InlineData(FindingResolutionStatus.UnderReview)]
    [InlineData(FindingResolutionStatus.Resolved)]
    [InlineData(FindingResolutionStatus.Closed)]
    public void Guard_NullCurrent_TargetingNonOpen_FailsWithDomainError(FindingResolutionStatus target)
    {
        var result = FindingResolutionTransitionGraph.Guard(null, target);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("INVALID_FINDING_RESOLUTION_TRANSITION");
    }

    [Theory]
    [InlineData(FindingResolutionStatus.Open)]
    [InlineData(FindingResolutionStatus.UnderReview)]
    [InlineData(FindingResolutionStatus.Resolved)]
    public void Guard_FromClosedTerminalState_ToAnyOtherState_IsRejected(FindingResolutionStatus target)
    {
        var result = FindingResolutionTransitionGraph.Guard(FindingResolutionStatus.Closed, target);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("INVALID_FINDING_RESOLUTION_TRANSITION");
    }

    [Theory]
    [InlineData(FindingResolutionStatus.Closed, true)]
    [InlineData(FindingResolutionStatus.Open, false)]
    [InlineData(FindingResolutionStatus.UnderReview, false)]
    [InlineData(FindingResolutionStatus.Resolved, false)]
    public void IsTerminal_IdentifiesClosedAsTerminal(FindingResolutionStatus status, bool expectedTerminal)
    {
        FindingResolutionTransitionGraph.IsTerminal(status).Should().Be(expectedTerminal);
    }
}
