using System;
using System.Collections.Generic;

using Cadence.Apps.Ctms.Domain;
using Cadence.Core.Domain;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Ctms;

/// <summary>
/// Comprehensive unit tests for MonitoringVisitTransitionGraph.
/// Validates 100% of allowed and forbidden transitions across the full 6x6 permutation matrix.
/// </summary>
public sealed class MonitoringVisitTransitionGraphTests
{
    private static readonly MonitoringVisitStatus[] AllStatuses =
    [
        MonitoringVisitStatus.Scheduled,
        MonitoringVisitStatus.InProgress,
        MonitoringVisitStatus.Completed,
        MonitoringVisitStatus.ReportDrafted,
        MonitoringVisitStatus.Approved,
        MonitoringVisitStatus.Cancelled
    ];

    public static TheoryData<MonitoringVisitStatus, MonitoringVisitStatus, bool> TransitionMatrixData()
    {
        var data = new TheoryData<MonitoringVisitStatus, MonitoringVisitStatus, bool>();

        // Defined allowed transitions (excluding same-state, which is always allowed)
        var allowedTransitions = new HashSet<(MonitoringVisitStatus, MonitoringVisitStatus)>
        {
            // Scheduled transitions
            (MonitoringVisitStatus.Scheduled, MonitoringVisitStatus.InProgress),
            (MonitoringVisitStatus.Scheduled, MonitoringVisitStatus.Completed),
            (MonitoringVisitStatus.Scheduled, MonitoringVisitStatus.Cancelled),

            // InProgress transitions
            (MonitoringVisitStatus.InProgress, MonitoringVisitStatus.Completed),
            (MonitoringVisitStatus.InProgress, MonitoringVisitStatus.Cancelled),

            // Completed transitions
            (MonitoringVisitStatus.Completed, MonitoringVisitStatus.ReportDrafted),
            (MonitoringVisitStatus.Completed, MonitoringVisitStatus.Approved),

            // ReportDrafted transitions
            (MonitoringVisitStatus.ReportDrafted, MonitoringVisitStatus.Approved),

            // Approved is terminal (no transitions)
            // Cancelled is terminal (no transitions)
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
        MonitoringVisitStatus current,
        MonitoringVisitStatus target,
        bool shouldSucceed)
    {
        var result = MonitoringVisitTransitionGraph.Guard(current, target);

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
            result.Error!.Code.Should().Be("INVALID_VISIT_STATUS_TRANSITION");
            result.Error.Type.Should().Be(ErrorType.Validation);
            result.Error.Description.Should().Contain(current.ToString()).And.Contain(target.ToString());
        }
    }

    [Fact]
    public void Guard_NullCurrent_TargetingScheduled_Succeeds()
    {
        var result = MonitoringVisitTransitionGraph.Guard(null, MonitoringVisitStatus.Scheduled);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(MonitoringVisitStatus.Scheduled);
    }

    [Theory]
    [InlineData(MonitoringVisitStatus.InProgress)]
    [InlineData(MonitoringVisitStatus.Completed)]
    [InlineData(MonitoringVisitStatus.ReportDrafted)]
    [InlineData(MonitoringVisitStatus.Approved)]
    [InlineData(MonitoringVisitStatus.Cancelled)]
    public void Guard_NullCurrent_TargetingNonScheduled_FailsWithDomainError(MonitoringVisitStatus target)
    {
        var result = MonitoringVisitTransitionGraph.Guard(null, target);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("INVALID_VISIT_STATUS_TRANSITION");
    }

    [Theory]
    [InlineData(MonitoringVisitStatus.Scheduled)]
    [InlineData(MonitoringVisitStatus.InProgress)]
    [InlineData(MonitoringVisitStatus.Completed)]
    [InlineData(MonitoringVisitStatus.ReportDrafted)]
    [InlineData(MonitoringVisitStatus.Cancelled)]
    public void Guard_FromApprovedTerminalState_ToAnyOtherState_IsRejected(MonitoringVisitStatus target)
    {
        var result = MonitoringVisitTransitionGraph.Guard(MonitoringVisitStatus.Approved, target);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("INVALID_VISIT_STATUS_TRANSITION");
        result.Error.Description.Should().Contain("Approved");
    }

    [Theory]
    [InlineData(MonitoringVisitStatus.Scheduled)]
    [InlineData(MonitoringVisitStatus.InProgress)]
    [InlineData(MonitoringVisitStatus.Completed)]
    [InlineData(MonitoringVisitStatus.ReportDrafted)]
    [InlineData(MonitoringVisitStatus.Approved)]
    public void Guard_FromCancelledTerminalState_ToAnyOtherState_IsRejected(MonitoringVisitStatus target)
    {
        var result = MonitoringVisitTransitionGraph.Guard(MonitoringVisitStatus.Cancelled, target);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("INVALID_VISIT_STATUS_TRANSITION");
        result.Error.Description.Should().Contain("Cancelled");
    }

    [Theory]
    [InlineData(MonitoringVisitStatus.Approved, true)]
    [InlineData(MonitoringVisitStatus.Cancelled, true)]
    [InlineData(MonitoringVisitStatus.Scheduled, false)]
    [InlineData(MonitoringVisitStatus.InProgress, false)]
    [InlineData(MonitoringVisitStatus.Completed, false)]
    [InlineData(MonitoringVisitStatus.ReportDrafted, false)]
    public void IsTerminal_IdentifiesTerminalStatesCorrectly(MonitoringVisitStatus status, bool expectedTerminal)
    {
        MonitoringVisitTransitionGraph.IsTerminal(status).Should().Be(expectedTerminal);
    }
}
