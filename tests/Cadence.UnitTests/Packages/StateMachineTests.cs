using System;
using System.Collections.Generic;

using Cadence.Core.Domain;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class StateMachineTests
{
    private enum TestState
    {
        Draft,
        Submitted,
        Approved,
        Rejected,
        Archived
    }

    private enum TestTrigger
    {
        Submit,
        Approve,
        Reject,
        Archive
    }

    private sealed class TestAggregate : StatefulAggregateRoot<string, TestState, TestTrigger>
    {
        private static readonly StateMachineDefinition<TestState, TestTrigger> Definition =
            new StateMachineBuilder<TestState, TestTrigger>(TestState.Draft)
                .WithTerminalStates(TestState.Archived)
                .OnInvalidTransition(TestState.Draft, TestState.Archived, "Approval is required before archive.")
                .In(TestState.Draft)
                    .Permit(TestTrigger.Submit, TestState.Submitted)
                .In(TestState.Submitted)
                    .PermitIf(TestTrigger.Approve, TestState.Approved, ctx => ctx.UserRole == "Reviewer", "Only reviewers can approve.")
                    .Permit(TestTrigger.Reject, TestState.Rejected)
                .In(TestState.Approved)
                    .Permit(TestTrigger.Archive, TestState.Archived)
                .In(TestState.Rejected)
                    .Permit(TestTrigger.Submit, TestState.Submitted)
                .Build();

        protected override StateMachineDefinition<TestState, TestTrigger> StateMachine => Definition;

        public TestAggregate() : base(Guid.NewGuid().ToString(), TestState.Draft) { }
        public TestAggregate(string id, TestState initial = TestState.Draft) : base(id, initial) { }
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void ValidTransition_AdvancesState_AndGeneratesAuditAndOutbox()
    {
        var aggregate = new TestAggregate("agg-1");
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        var result = aggregate.Transition(TestTrigger.Submit, "user-1", now, "Submitting for review", "Author");

        result.IsSuccess.Should().BeTrue();
        aggregate.Status.Should().Be(TestState.Submitted);

        // Audit Trail check
        aggregate.TransitionHistory.Should().HaveCount(1);
        var record = aggregate.TransitionHistory[0];
        record.FromState.Should().Be("Draft");
        record.ToState.Should().Be("Submitted");
        record.Trigger.Should().Be("Submit");
        record.UserId.Should().Be("user-1");
        record.UserRole.Should().Be("Author");
        record.ReasonForChange.Should().Be("Submitting for review");
        record.PredecessorHash.Should().BeNull();
        record.Sha256Hash.Should().NotBeNullOrWhiteSpace();

        // Domain event check
        aggregate.DomainEvents.Should().HaveCount(1);
        var domainEvent = aggregate.DomainEvents[0].Should().BeOfType<DomainStateTransitionEvent>().Subject;
        domainEvent.AggregateType.Should().Be(nameof(TestAggregate));
        domainEvent.AggregateId.Should().Be("agg-1");
        domainEvent.FromState.Should().Be("Draft");
        domainEvent.ToState.Should().Be("Submitted");
        domainEvent.Trigger.Should().Be("Submit");
        domainEvent.ActorId.Should().Be("user-1");
        domainEvent.ReasonForChange.Should().Be("Submitting for review");

        // Outbox check
        aggregate.OutboxMessages.Should().HaveCount(1);
        var outbox = aggregate.OutboxMessages[0];
        outbox.AggregateType.Should().Be(nameof(TestAggregate));
        outbox.AggregateId.Should().Be("agg-1");
        outbox.EventType.Should().Be("TestAggregateStateTransitioned");
        outbox.Payload.Should().Contain("\"FromState\":\"Draft\"");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void ChainedTransitions_MaintainCryptographicSha256HashChain()
    {
        var aggregate = new TestAggregate("agg-2");
        var t1 = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 11, 14, 12, 5, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 11, 14, 12, 10, 0, TimeSpan.Zero);

        aggregate.Transition(TestTrigger.Submit, "user-1", t1, "Submit", "Author").IsSuccess.Should().BeTrue();
        aggregate.Transition(TestTrigger.Approve, "user-reviewer", t2, "Approve", "Reviewer").IsSuccess.Should().BeTrue();
        aggregate.Transition(TestTrigger.Archive, "user-admin", t3, "Archive", "Admin").IsSuccess.Should().BeTrue();

        aggregate.TransitionHistory.Should().HaveCount(3);
        aggregate.TransitionHistory[0].PredecessorHash.Should().BeNull();
        aggregate.TransitionHistory[1].PredecessorHash.Should().Be(aggregate.TransitionHistory[0].Sha256Hash);
        aggregate.TransitionHistory[2].PredecessorHash.Should().Be(aggregate.TransitionHistory[1].Sha256Hash);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void InvalidTransition_ReturnsStructuredDomainError_AndLeavesStateIntact()
    {
        var aggregate = new TestAggregate("agg-3");
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        // Cannot approve directly from Draft
        var result = aggregate.Transition(TestTrigger.Approve, "user-1", now, "Direct approve attempt");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
        aggregate.Status.Should().Be(TestState.Draft);
        aggregate.TransitionHistory.Should().BeEmpty();
        aggregate.DomainEvents.Should().BeEmpty();
        aggregate.OutboxMessages.Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void CustomInvalidTransitionMessage_ReturnedWhenConfigured()
    {
        var aggregate = new TestAggregate("agg-4");
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        // TransitionTo Draft -> Archived
        var result = aggregate.TransitionTo(TestState.Archived, "user-1", now, "Skip to archive");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
        result.Error.Description.Should().Be("Approval is required before archive.");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void GuardFailure_BlocksTransition_WithActionableDetail()
    {
        var aggregate = new TestAggregate("agg-5");
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        aggregate.Transition(TestTrigger.Submit, "user-1", now, "Submit", "Author").IsSuccess.Should().BeTrue();

        // User is not Reviewer
        var result = aggregate.Transition(TestTrigger.Approve, "user-2", now, "Approve without role", "Author");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("STATE_TRANSITION_GUARD_FAILED");
        result.Error.Description.Should().Be("Only reviewers can approve.");
        aggregate.Status.Should().Be(TestState.Submitted);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void TerminalState_BlocksAllSubsequentTransitions()
    {
        var aggregate = new TestAggregate("agg-6");
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        aggregate.Transition(TestTrigger.Submit, "user-1", now, "Submit", "Author");
        aggregate.Transition(TestTrigger.Approve, "user-reviewer", now, "Approve", "Reviewer");
        aggregate.Transition(TestTrigger.Archive, "user-admin", now, "Archive", "Admin");

        aggregate.Status.Should().Be(TestState.Archived);

        var result = aggregate.Transition(TestTrigger.Submit, "user-1", now, "Try unarchiving");
        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("ILLEGAL_STATE_TRANSITION");
        result.Error.Description.Should().Contain("is terminal and cannot transition to any subsequent state");
    }

    [Fact]
    [GxPRequirement("PRD-EVT-001")]
    public void ClearDomainEvents_ClearsEventsAndOutboxMessages()
    {
        var aggregate = new TestAggregate("agg-7");
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        aggregate.Transition(TestTrigger.Submit, "user-1", now, "Submit", "Author");
        aggregate.DomainEvents.Should().HaveCount(1);
        aggregate.OutboxMessages.Should().HaveCount(1);

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.Should().BeEmpty();
        aggregate.OutboxMessages.Should().BeEmpty();
    }
}
