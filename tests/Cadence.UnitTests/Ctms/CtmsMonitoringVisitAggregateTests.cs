using System;
using System.Linq;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Domain.Events;
using Cadence.Core.Domain;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Ctms;

/// <summary>
/// Unit tests for CtmsMonitoringVisit and CtmsMonitoringVisitFinding aggregate roots.
/// Verifies private setter encapsulation, domain event generation, and state machine guards.
/// </summary>
public sealed class CtmsMonitoringVisitAggregateTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void NewMonitoringVisit_DefaultsToScheduledStatus_AndEmptyDomainEvents()
    {
        var visit = new CtmsMonitoringVisit("VISIT-001");

        visit.Status.Should().Be(MonitoringVisitStatus.Scheduled);
        visit.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void StartVisit_FromScheduled_TransitionsToInProgress_AndRecordsDomainEvents()
    {
        var visit = new CtmsMonitoringVisit("VISIT-001");
        var now = _clock.UtcNow;

        var result = visit.StartVisit(now, "cra-1", "CRA arrived on site");

        result.IsSuccess.Should().BeTrue();
        visit.Status.Should().Be(MonitoringVisitStatus.InProgress);
        visit.ReasonForChange.Should().Be("CRA arrived on site");

        visit.DomainEvents.Should().HaveCount(2);
        visit.DomainEvents.Should().ContainSingle(e => e is MonitoringVisitStartedDomainEvent);
        visit.DomainEvents.Should().ContainSingle(e => e is MonitoringVisitStatusChangedDomainEvent);

        var startedEvent = visit.DomainEvents.OfType<MonitoringVisitStartedDomainEvent>().Single();
        startedEvent.VisitId.Should().Be("VISIT-001");
        startedEvent.UserId.Should().Be("cra-1");
        startedEvent.Timestamp.Should().Be(now);
        startedEvent.ChangeReason.Should().Be("CRA arrived on site");
    }

    [Fact]
    public void CompleteVisit_FromInProgress_TransitionsToCompleted_SetsActualDate_AndRecordsEvent()
    {
        var visit = new CtmsMonitoringVisit("VISIT-001");
        var startTime = _clock.UtcNow;
        visit.StartVisit(startTime, "cra-1", "Started");
        visit.ClearDomainEvents();

        var actualDate = startTime.AddHours(8);
        var completeTime = startTime.AddHours(8);

        var result = visit.CompleteVisit(actualDate, completeTime, "cra-1", "Monitoring completed with findings");

        result.IsSuccess.Should().BeTrue();
        visit.Status.Should().Be(MonitoringVisitStatus.Completed);
        visit.ActualDate.Should().Be(actualDate);
        visit.ReasonForChange.Should().Be("Monitoring completed with findings");

        visit.DomainEvents.Should().ContainSingle(e => e is MonitoringVisitCompletedDomainEvent);
        var completedEvent = visit.DomainEvents.OfType<MonitoringVisitCompletedDomainEvent>().Single();
        completedEvent.VisitId.Should().Be("VISIT-001");
        completedEvent.ActualDate.Should().Be(actualDate);
        completedEvent.Timestamp.Should().Be(completeTime);
        completedEvent.UserId.Should().Be("cra-1");
    }

    [Fact]
    public void DraftReport_FromCompleted_TransitionsToReportDrafted()
    {
        var visit = new CtmsMonitoringVisit("VISIT-001");
        visit.StartVisit(_clock.UtcNow, "cra-1", "Started");
        visit.CompleteVisit(_clock.UtcNow, _clock.UtcNow, "cra-1", "Completed");
        visit.ClearDomainEvents();

        var draftTime = _clock.UtcNow.AddDays(1);
        var result = visit.DraftReport(draftTime, "cra-1", "Drafted monitoring trip report");

        result.IsSuccess.Should().BeTrue();
        visit.Status.Should().Be(MonitoringVisitStatus.ReportDrafted);
        visit.DomainEvents.Should().ContainSingle(e => e is MonitoringVisitReportDraftedDomainEvent);
    }

    [Fact]
    public void Approve_FromReportDrafted_TransitionsToApproved_SetsManifestation_AndRecordsEvent()
    {
        var visit = new CtmsMonitoringVisit("VISIT-001");
        visit.StartVisit(_clock.UtcNow, "cra-1", "Started");
        visit.CompleteVisit(_clock.UtcNow, _clock.UtcNow, "cra-1", "Completed");
        visit.DraftReport(_clock.UtcNow.AddDays(1), "cra-1", "Drafted");
        visit.ClearDomainEvents();

        var signingTime = _clock.UtcNow.AddDays(2);
        string sigJson = "{\"signer\":\"supervisor-1\",\"type\":\"SUPERVISORY_APPROVAL\"}";

        var result = visit.Approve("supervisor-1", sigJson, signingTime, signingTime, "supervisor-1", "Endorsed trip report");

        result.IsSuccess.Should().BeTrue();
        visit.Status.Should().Be(MonitoringVisitStatus.Approved);
        visit.Signer.Should().Be("supervisor-1");
        visit.SignatureManifestation.Should().Be(sigJson);
        visit.SigningTimestamp.Should().Be(signingTime);

        visit.DomainEvents.Should().ContainSingle(e => e is MonitoringVisitApprovedDomainEvent);
        var approvedEvent = visit.DomainEvents.OfType<MonitoringVisitApprovedDomainEvent>().Single();
        approvedEvent.Signer.Should().Be("supervisor-1");
        approvedEvent.SigningTimestamp.Should().Be(signingTime);
    }

    [Fact]
    public void ApprovedVisit_IsTerminalAndImmutable_RejectsReopeningToScheduled()
    {
        var visit = new CtmsMonitoringVisit("VISIT-001", MonitoringVisitStatus.Approved);
        var attemptTime = _clock.UtcNow.AddDays(5);

        var result = visit.TransitionTo(MonitoringVisitStatus.Scheduled, attemptTime, "malicious-user", "Attempt reopen");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_VISIT_STATUS_TRANSITION");
        visit.Status.Should().Be(MonitoringVisitStatus.Approved);
    }

    [Fact]
    public void CancelVisit_FromScheduled_TransitionsToCancelled_AndRecordsEvent()
    {
        var visit = new CtmsMonitoringVisit("VISIT-001");
        var cancelTime = _clock.UtcNow;

        var result = visit.Cancel(cancelTime, "cra-1", "Site requested postponement");

        result.IsSuccess.Should().BeTrue();
        visit.Status.Should().Be(MonitoringVisitStatus.Cancelled);
        visit.DomainEvents.Should().ContainSingle(e => e is MonitoringVisitCancelledDomainEvent);
    }

    [Fact]
    public void CancelledVisit_IsTerminal_RejectsTransitionToInProgress()
    {
        var visit = new CtmsMonitoringVisit("VISIT-001", MonitoringVisitStatus.Cancelled);

        var result = visit.StartVisit(_clock.UtcNow, "cra-1", "Try to restart");

        result.IsFailure.Should().BeTrue();
        visit.Status.Should().Be(MonitoringVisitStatus.Cancelled);
    }

    [Fact]
    public void FindingAggregateRoot_LifecycleTransitions_AndDomainEventGeneration()
    {
        var finding = new CtmsMonitoringVisitFinding("FIND-001")
        {
            VisitId = "VISIT-001",
            Text = "Drug accountability log missing signature",
            Severity = "MAJOR"
        };

        finding.ResolutionStatus.Should().Be(FindingResolutionStatus.Open);
        finding.DomainEvents.Should().BeEmpty();

        // 1. Submit for review
        var t1 = _clock.UtcNow;
        var r1 = finding.SubmitForReview(t1, "cra-1", "Corrective action submitted by site");
        r1.IsSuccess.Should().BeTrue();
        finding.ResolutionStatus.Should().Be(FindingResolutionStatus.UnderReview);
        finding.DomainEvents.Should().HaveCount(1);

        // 2. Resolve
        var t2 = t1.AddDays(1);
        var r2 = finding.Resolve(t2, "lead-cra", "Reviewed and accepted corrective action");
        r2.IsSuccess.Should().BeTrue();
        finding.ResolutionStatus.Should().Be(FindingResolutionStatus.Resolved);
        finding.DomainEvents.Should().HaveCount(2);

        // 3. Close
        var t3 = t2.AddDays(1);
        var r3 = finding.Close(t3, "qa-auditor", "Audit verification complete, finding closed");
        r3.IsSuccess.Should().BeTrue();
        finding.ResolutionStatus.Should().Be(FindingResolutionStatus.Closed);
        finding.DomainEvents.Should().HaveCount(3);

        // 4. Closed finding cannot transition back to Open
        var t4 = t3.AddDays(1);
        var r4 = finding.TransitionTo(FindingResolutionStatus.Open, t4, "user", "Attempt reopen");
        r4.IsFailure.Should().BeTrue();
        r4.Error!.Code.Should().Be("INVALID_FINDING_RESOLUTION_TRANSITION");
        finding.ResolutionStatus.Should().Be(FindingResolutionStatus.Closed);
    }
}
