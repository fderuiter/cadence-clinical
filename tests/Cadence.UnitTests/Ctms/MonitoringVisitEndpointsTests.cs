using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Ctms;

public sealed class MonitoringVisitEndpointsTests
{
    private const string StudyId = "STUDY-2026-001";
    private const string SiteId = "SITE-101";
    private const string CraId = "cra_fderuiter";

    private readonly CtmsDbContext _db;
    private readonly FakeClock _clock;

    public MonitoringVisitEndpointsTests()
    {
        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CtmsDbContext(options);
        _clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero));
    }

    private static DefaultHttpContext Caller(string role = RoleNormalization.ROLE_SYSADMIN, string userId = "user-1")
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
    public async Task ScheduleVisit_WithValidRequestAndHeaderReason_CreatesScheduledVisitAndAuditLog()
    {
        var caller = Caller("CRA", "cra-user-1");
        caller.Request.Headers["X-Change-Reason"] = "Initial protocol routine monitoring visit scheduling";

        var scheduledDate = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        var request = new MonitoringVisitCreateDto(
            Study: StudyId,
            Site: SiteId,
            CraId: CraId,
            VisitType: "IMV",
            ScheduledDate: scheduledDate
        );

        var result = await MonitoringVisitEndpoints.ScheduleMonitoringVisitAsync(
            request, _db, caller, _clock, TestContext.Current.CancellationToken);

        var created = result.Should().BeOfType<Created<MonitoringVisitResponseDto>>().Subject;
        created.Value.Should().NotBeNull();
        created.Value!.Status.Should().Be("SCHEDULED");
        created.Value.Study.Should().Be(StudyId);
        created.Value.Site.Should().Be(SiteId);
        created.Value.CraId.Should().Be(CraId);
        created.Value.VisitType.Should().Be("IMV");
        created.Value.ReasonForChange.Should().Be("Initial protocol routine monitoring visit scheduling");
        created.Value.CreatedBy.Should().Be("cra-user-1");

        // Verify entity in DB
        var visit = await _db.CtmsMonitoringVisits.FindAsync([created.Value.Id], TestContext.Current.CancellationToken);
        visit.Should().NotBeNull();
        visit!.Status.Should().Be(MonitoringVisitStatus.Scheduled);

        // Verify audit log
        var audit = await _db.CtmsAuditLogs.SingleAsync(a => a.Action == "SCHEDULE_MONITORING_VISIT", TestContext.Current.CancellationToken);
        audit.UserId.Should().Be("cra-user-1");
        audit.Details.Should().Contain(visit.Id);
        audit.ReasonForChange.Should().Be("Initial protocol routine monitoring visit scheduling");
    }

    [Fact]
    public async Task ScheduleVisit_WithoutReasonForChange_ReturnsBadRequest()
    {
        var caller = Caller();
        var request = new MonitoringVisitCreateDto(
            Study: StudyId,
            Site: SiteId,
            CraId: CraId,
            VisitType: "IMV",
            ScheduledDate: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero)
        );

        var result = await MonitoringVisitEndpoints.ScheduleMonitoringVisitAsync(
            request, _db, caller, _clock, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ScheduleVisit_WithWhitespaceReasonForChange_ReturnsBadRequest()
    {
        var caller = Caller();
        caller.Request.Headers["X-Change-Reason"] = "   ";
        var request = new MonitoringVisitCreateDto(
            Study: StudyId,
            Site: SiteId,
            CraId: CraId,
            VisitType: "IMV",
            ScheduledDate: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero)
        );

        var result = await MonitoringVisitEndpoints.ScheduleMonitoringVisitAsync(
            request, _db, caller, _clock, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task GetMonitoringVisits_ReturnsVisitsFilteredByStudyAndSite()
    {
        var v1 = new CtmsMonitoringVisit
        {
            StudyId = StudyId,
            SiteId = SiteId,
            CraId = CraId,
            VisitType = "SIV",
            ScheduledDate = _clock.UtcNow.AddDays(1),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Initial"
        };
        var v2 = new CtmsMonitoringVisit
        {
            StudyId = "OTHER-STUDY",
            SiteId = SiteId,
            CraId = CraId,
            VisitType = "IMV",
            ScheduledDate = _clock.UtcNow.AddDays(2),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Initial"
        };
        _db.CtmsMonitoringVisits.AddRange(v1, v2);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await MonitoringVisitEndpoints.GetMonitoringVisitsAsync(
            StudyId, SiteId, null, null, _db, TestContext.Current.CancellationToken);

        var ok = result.Should().BeOfType<Ok<List<MonitoringVisitResponseDto>>>().Subject;
        ok.Value.Should().HaveCount(1);
        ok.Value![0].Id.Should().Be(v1.Id);
        ok.Value[0].Study.Should().Be(StudyId);
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-001")]
    public async Task CompleteVisit_WithFindings_UpdatesStatusAndPersistsFindingsAndAuditLog()
    {
        var visit = new CtmsMonitoringVisit
        {
            StudyId = StudyId,
            SiteId = SiteId,
            CraId = CraId,
            VisitType = "IMV",
            ScheduledDate = _clock.UtcNow.AddDays(-1),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Initial"
        };
        visit.StartVisit(_clock.UtcNow.AddHours(-4), "cra-user-1", "Starting visit on-site");
        _db.CtmsMonitoringVisits.Add(visit);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller("CRA", "cra-user-1");
        caller.Request.Headers["X-Change-Reason"] = "On-site monitoring visit completed with 2 findings";

        var actualDate = _clock.UtcNow;
        var request = new MonitoringVisitCompleteDto(
            ActualDate: actualDate,
            Findings:
            [
                new FindingCreateDto("GCP training records expired for CRC", "MAJOR", "OPEN"),
                new FindingCreateDto("IP storage temp log missing one day entry", "MINOR", "RESOLVED")
            ]
        );

        var result = await MonitoringVisitEndpoints.CompleteMonitoringVisitAsync(
            visit.Id, request, _db, caller, _clock, TestContext.Current.CancellationToken);

        var ok = result.Should().BeOfType<Ok<MonitoringVisitResponseDto>>().Subject;
        ok.Value.Should().NotBeNull();
        ok.Value!.Status.Should().Be("COMPLETED");
        ok.Value.ActualDate.Should().Be(actualDate.ToString("o"));

        // Verify findings in DB
        var findings = await _db.CtmsMonitoringVisitFindings.Where(f => f.VisitId == visit.Id).ToListAsync(TestContext.Current.CancellationToken);
        findings.Should().HaveCount(2);
        findings.Should().Contain(f => f.Severity == "MAJOR" && f.ResolutionStatus == FindingResolutionStatus.Open);
        findings.Should().Contain(f => f.Severity == "MINOR" && f.ResolutionStatus == FindingResolutionStatus.Resolved);

        // Verify audit log
        var audit = await _db.CtmsAuditLogs.SingleAsync(a => a.Action == "COMPLETE_MONITORING_VISIT", TestContext.Current.CancellationToken);
        audit.UserId.Should().Be("cra-user-1");
        audit.Details.Should().Contain("2 findings");
    }

    [Fact]
    public async Task CompleteVisit_NotFound_Returns404()
    {
        var caller = Caller();
        caller.Request.Headers["X-Change-Reason"] = "Completion reason";
        var request = new MonitoringVisitCompleteDto(ActualDate: _clock.UtcNow);

        var result = await MonitoringVisitEndpoints.CompleteMonitoringVisitAsync(
            "NON-EXISTENT", request, _db, caller, _clock, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-001")]
    public async Task SignOffVisit_OnCompletedVisit_StoresPart11SignatureAndAuditLog()
    {
        var visit = new CtmsMonitoringVisit(Guid.NewGuid().ToString(), MonitoringVisitStatus.Completed)
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
        caller.Request.Headers["X-Change-Reason"] = "Trip report reviewed and endorsed in full compliance with protocol.";

        var result = await MonitoringVisitEndpoints.SignOffMonitoringVisitAsync(
            visit.Id, _db, caller, _clock, TestContext.Current.CancellationToken);

        var ok = result.Should().BeOfType<Ok<MonitoringVisitResponseDto>>().Subject;
        ok.Value.Should().NotBeNull();
        ok.Value!.Status.Should().Be("SIGNED_OFF");
        ok.Value.Signer.Should().Be("manager-1");
        ok.Value.SignatureManifestation.Should().NotBeNull();
        ok.Value.SignatureManifestation.Should().Contain("ELECTRONIC_RECORD_SIGN_OFF");

        // Verify in DB
        var updated = await _db.CtmsMonitoringVisits.FindAsync([visit.Id], TestContext.Current.CancellationToken);
        updated!.Status.Should().Be(MonitoringVisitStatus.Signed_Off);
        updated.Signer.Should().Be("manager-1");
        updated.SigningTimestamp.Should().Be(_clock.UtcNow);

        // Verify audit log
        var audit = await _db.CtmsAuditLogs.SingleAsync(a => a.Action == "SIGN_OFF_MONITORING_VISIT", TestContext.Current.CancellationToken);
        audit.UserId.Should().Be("manager-1");
        audit.Details.Should().Contain(visit.Id);
    }

    [Fact]
    public async Task SignOffVisit_OnScheduledVisit_ReturnsBadRequest()
    {
        var visit = new CtmsMonitoringVisit(Guid.NewGuid().ToString(), CtmsMonitoringVisitStatus.Scheduled)
        {
            StudyId = StudyId,
            SiteId = SiteId,
            CraId = CraId,
            VisitType = "IMV",
            ScheduledDate = _clock.UtcNow.AddDays(1),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Initial"
        };
        _db.CtmsMonitoringVisits.Add(visit);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller("monitor", "manager-1");
        caller.Request.Headers["X-Change-Reason"] = "Attempt sign off";

        var result = await MonitoringVisitEndpoints.SignOffMonitoringVisitAsync(
            visit.Id, _db, caller, _clock, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SignOffVisit_WithoutReason_ReturnsBadRequest()
    {
        var visit = new CtmsMonitoringVisit(Guid.NewGuid().ToString(), MonitoringVisitStatus.Completed)
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

        var result = await MonitoringVisitEndpoints.SignOffMonitoringVisitAsync(
            visit.Id, _db, caller, _clock, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task MonitoringVisitLetters_ReturnsLettersForVisit()
    {
        var letter = new CtmsGeneratedLetter("LTR-001")
        {
            VisitId = "VISIT-001",
            LetterType = "confirmation_letter",
            RenderedContent = "<html>Confirmation Letter</html>",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "cra-1",
            ReasonForChange = "Generated letter"
        };
        _db.CtmsGeneratedLetters.Add(letter);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var listResult = await MonitoringVisitEndpoints.GetMonitoringVisitLettersAsync(
            "VISIT-001", _db, TestContext.Current.CancellationToken);
        var okList = listResult.Should().BeOfType<Ok<List<GeneratedLetterResponseDto>>>().Subject;
        okList.Value.Should().HaveCount(1);
        okList.Value![0].LetterType.Should().Be("confirmation_letter");

        var itemResult = await MonitoringVisitEndpoints.GetMonitoringVisitLetterByTypeAsync(
            "VISIT-001", "confirmation_letter", _db, TestContext.Current.CancellationToken);
        var okItem = itemResult.Should().BeOfType<Ok<GeneratedLetterResponseDto>>().Subject;
        okItem.Value!.Id.Should().Be("LTR-001");

        var missingResult = await MonitoringVisitEndpoints.GetMonitoringVisitLetterByTypeAsync(
            "VISIT-001", "follow_up_letter", _db, TestContext.Current.CancellationToken);
        missingResult.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-001")]
    public async Task StartVisit_OnScheduledVisit_TransitionsToInProgressAndAudits()
    {
        var visit = new CtmsMonitoringVisit
        {
            StudyId = StudyId,
            SiteId = SiteId,
            CraId = CraId,
            VisitType = "IMV",
            ScheduledDate = _clock.UtcNow.AddDays(1),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Scheduled"
        };
        _db.CtmsMonitoringVisits.Add(visit);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller("CRA", "cra-user-1");
        caller.Request.Headers["X-Change-Reason"] = "Starting on-site monitoring visit";

        var result = await MonitoringVisitEndpoints.StartMonitoringVisitAsync(
            visit.Id, _db, caller, _clock, TestContext.Current.CancellationToken);

        var ok = result.Should().BeOfType<Ok<MonitoringVisitResponseDto>>().Subject;
        ok.Value!.Status.Should().Be("IN_PROGRESS");

        var updated = await _db.CtmsMonitoringVisits.FindAsync([visit.Id], TestContext.Current.CancellationToken);
        updated!.Status.Should().Be(MonitoringVisitStatus.InProgress);

        var audit = await _db.CtmsAuditLogs.SingleAsync(a => a.Action == "START_MONITORING_VISIT", TestContext.Current.CancellationToken);
        audit.UserId.Should().Be("cra-user-1");
        audit.Details.Should().Contain(visit.Id);
    }

    [Fact]
    public async Task StartVisit_OnCompletedVisit_ReturnsBadRequest()
    {
        var visit = new CtmsMonitoringVisit(Guid.NewGuid().ToString(), MonitoringVisitStatus.Completed)
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

        var caller = Caller("CRA", "cra-user-1");
        caller.Request.Headers["X-Change-Reason"] = "Attempt restarting completed visit";

        var result = await MonitoringVisitEndpoints.StartMonitoringVisitAsync(
            visit.Id, _db, caller, _clock, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task CompleteVisit_OnApprovedVisit_ReturnsBadRequest()
    {
        var visit = new CtmsMonitoringVisit(Guid.NewGuid().ToString(), MonitoringVisitStatus.Approved)
        {
            StudyId = StudyId,
            SiteId = SiteId,
            CraId = CraId,
            VisitType = "IMV",
            ScheduledDate = _clock.UtcNow.AddDays(-5),
            ActualDate = _clock.UtcNow.AddDays(-4),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Approved"
        };
        _db.CtmsMonitoringVisits.Add(visit);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller("CRA", "cra-user-1");
        caller.Request.Headers["X-Change-Reason"] = "Attempt edit approved visit";

        var request = new MonitoringVisitCompleteDto(ActualDate: _clock.UtcNow);

        var result = await MonitoringVisitEndpoints.CompleteMonitoringVisitAsync(
            visit.Id, request, _db, caller, _clock, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
