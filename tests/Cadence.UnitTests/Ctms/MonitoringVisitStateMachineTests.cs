using System;
using System.Linq;
using System.Threading.Tasks;
using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Core.Domain;
using Cadence.Database;
using Cadence.Testing;
using Cadence.Testing.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cadence.UnitTests.Ctms;

public class MonitoringVisitStateMachineTests
{
    private readonly CtmsDbContext _db;
    private readonly FakeClock _clock;

    public MonitoringVisitStateMachineTests()
    {
        _clock = new FakeClock(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
        var interceptor = new GxPAuditInterceptor(() => "cra-user-1", _clock);

        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        _db = new CtmsDbContext(options);
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-001")]
    public async Task CompleteAndSignOff_TransitionsVisit_AndAtomicallyWritesOutbox()
    {
        var visit = new CtmsMonitoringVisit("visit-001")
        {
            StudyId = "STUDY-100",
            SiteId = "SITE-200",
            CraId = "CRA-01",
            VisitType = "IMV",
            ScheduledDate = _clock.UtcNow.AddDays(7),
        };
        _db.CtmsMonitoringVisits.Add(visit);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Transition: Scheduled -> Completed via Complete()
        var actualDate = _clock.UtcNow;
        var completeResult = visit.Complete(actualDate, "Visit conducted per protocol", "cra-user-1", _clock.UtcNow, "CRA");
        completeResult.IsSuccess.Should().BeTrue();
        visit.Status.Should().Be(CtmsMonitoringVisitStatus.Completed);
        visit.ActualDate.Should().Be(actualDate);

        // SaveChanges should persist Outbox row
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outboxEntries = await _db.TransactionalOutbox.ToListAsync(TestContext.Current.CancellationToken);
        outboxEntries.Should().HaveCount(1);
        outboxEntries[0].AggregateId.Should().Be("visit-001");
        outboxEntries[0].AggregateType.Should().Be(nameof(CtmsMonitoringVisit));
        outboxEntries[0].Payload.Should().Contain("\"ToState\":\"Completed\"");

        // Transition: Completed -> Signed_Off via SignOff()
        var signOffResult = visit.SignOff("Dr. Jane Reviewer", _clock.UtcNow.AddHours(2), "Digitally Signed", "Visit signoff approval", "sup-1", "LeadCRA");
        signOffResult.IsSuccess.Should().BeTrue();
        visit.Status.Should().Be(CtmsMonitoringVisitStatus.Signed_Off);
        visit.Signer.Should().Be("Dr. Jane Reviewer");
        visit.SignatureManifestation.Should().Be("Digitally Signed");

        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var allOutbox = await _db.TransactionalOutbox.ToListAsync(TestContext.Current.CancellationToken);
        allOutbox.Should().HaveCount(2);
        allOutbox[1].Payload.Should().Contain("\"ToState\":\"Signed_Off\"");
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-001")]
    public void InvalidSignOffFromScheduled_IsRejected()
    {
        var visit = new CtmsMonitoringVisit("visit-002");
        visit.Status.Should().Be(CtmsMonitoringVisitStatus.Scheduled);

        var result = visit.SignOff("Dr. Reviewer", _clock.UtcNow, "Signature", "Premature signoff", "user-1");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("INVALID_STATE_TRANSITION");
        visit.Status.Should().Be(CtmsMonitoringVisitStatus.Scheduled);
        visit.Signer.Should().BeNull();
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-001")]
    public void TerminalStateSignedOff_CannotTransitionFurther()
    {
        var visit = new CtmsMonitoringVisit("visit-003");
        visit.Complete(_clock.UtcNow, "Conducted", "user-1", _clock.UtcNow);
        visit.SignOff("Signer", _clock.UtcNow, "Sig", "Approved", "user-1");

        visit.Status.Should().Be(CtmsMonitoringVisitStatus.Signed_Off);

        var result = visit.Transition(CtmsMonitoringVisitTrigger.Cancel, "user-1", _clock.UtcNow, "Try cancel signed off");
        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("ILLEGAL_STATE_TRANSITION");
    }
}
