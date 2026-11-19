using System;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using MassTransit;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Ctms;

public sealed class MonitoringVisitPublishEventTests
{
    private const string StudyId = "STUDY-2026-001";
    private const string SiteId = "SITE-101";
    private const string CraId = "cra_fderuiter";

    private readonly CtmsDbContext _db;
    private readonly FakeClock _clock;

    public MonitoringVisitPublishEventTests()
    {
        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CtmsDbContext(options);
        _clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero));
    }

    private static DefaultHttpContext Caller(string role = RoleNormalization.ROLE_SYSADMIN, string userId = "manager-1")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = userId,
            Roles = [role],
            AssignedStudies = [StudyId],
            AssignedSites = [SiteId],
        };
        return httpContext;
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-001")]
    public async Task SignOffVisit_PublishesClinicalVisitCompletedEvent()
    {
        var visit = new CtmsMonitoringVisit(Guid.NewGuid().ToString(), CtmsMonitoringVisitStatus.Completed)
        {
            StudyId = StudyId,
            SiteId = SiteId,
            CraId = CraId,
            VisitType = "IMV",
            ScheduledDate = _clock.UtcNow.AddDays(-2),
            ActualDate = _clock.UtcNow.AddDays(-1),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Completed"
        };
        _db.CtmsMonitoringVisits.Add(visit);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller("monitor", "manager-1");
        caller.Request.Headers["X-Change-Reason"] = "Trip report endorsed.";

        var publishEndpoint = Substitute.For<IPublishEndpoint>();

        var result = await MonitoringVisitEndpoints.SignOffMonitoringVisitAsync(
            visit.Id, _db, caller, _clock, TestContext.Current.CancellationToken, publishEndpoint);

        var ok = result.Should().BeOfType<Ok<MonitoringVisitResponseDto>>().Subject;
        ok.Value.Should().NotBeNull();
        ok.Value!.Status.Should().Be("SIGNED_OFF");

        // Verify visit updated
        var updated = await _db.CtmsMonitoringVisits.FindAsync([visit.Id], TestContext.Current.CancellationToken);
        updated!.Status.Should().Be(CtmsMonitoringVisitStatus.Signed_Off);

        // Verify publish was invoked with ClinicalVisitCompleted
        await publishEndpoint.Received(1).Publish(
            Arg.Is<ClinicalVisitCompleted>(e =>
                e.EventId == $"MVR-{visit.Id}" &&
                e.Study == StudyId &&
                e.Site == SiteId &&
                e.VisitCode == "IMV" &&
                e.CraId == CraId &&
                e.Signer == "manager-1" &&
                e.VisitId == visit.Id),
            TestContext.Current.CancellationToken);
    }
}
