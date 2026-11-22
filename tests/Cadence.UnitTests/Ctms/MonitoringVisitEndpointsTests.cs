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

    private static DefaultHttpContext Caller(
        string role = RoleNormalization.ROLE_SYSADMIN,
        string userId = "user-1",
        string[]? studies = null,
        string[]? sites = null)
    {
        var normalized = RoleNormalization.GetNormalizedRoles([role]);
        bool siteScoped = normalized.Any(SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains);
        var httpContext = new DefaultHttpContext();
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = userId,
            Roles = normalized,
            RawRoles = [role],
            AssignedStudies = studies ?? [StudyId],
            AssignedSites = sites ?? (siteScoped ? [SiteId] : []),
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
        visit!.Status.Should().Be(CtmsMonitoringVisitStatus.Scheduled);

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
            StudyId, SiteId, null, null, _db, Caller(), TestContext.Current.CancellationToken);

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
        findings.Should().Contain(f => f.Severity == "MAJOR" && f.ResolutionStatus == "OPEN");
        findings.Should().Contain(f => f.Severity == "MINOR" && f.ResolutionStatus == "RESOLVED");

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
        updated!.Status.Should().Be(CtmsMonitoringVisitStatus.Signed_Off);
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

        var result = await MonitoringVisitEndpoints.SignOffMonitoringVisitAsync(
            visit.Id, _db, caller, _clock, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task MonitoringVisitLetters_ReturnsLettersForVisit()
    {
        var visit = new CtmsMonitoringVisit("VISIT-001", CtmsMonitoringVisitStatus.Completed)
        {
            StudyId = StudyId,
            SiteId = SiteId,
            CraId = CraId,
            VisitType = "IMV",
            ScheduledDate = _clock.UtcNow.AddDays(-1),
            ActualDate = _clock.UtcNow,
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "cra-1",
            ReasonForChange = "Completed"
        };
        var letter = new CtmsGeneratedLetter("LTR-001")
        {
            VisitId = "VISIT-001",
            LetterType = "confirmation_letter",
            RenderedContent = "<html>Confirmation Letter</html>",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "cra-1",
            ReasonForChange = "Generated letter"
        };
        _db.CtmsMonitoringVisits.Add(visit);
        _db.CtmsGeneratedLetters.Add(letter);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller();
        var listResult = await MonitoringVisitEndpoints.GetMonitoringVisitLettersAsync(
            "VISIT-001", _db, caller, TestContext.Current.CancellationToken);
        var okList = listResult.Should().BeOfType<Ok<List<GeneratedLetterResponseDto>>>().Subject;
        okList.Value.Should().HaveCount(1);
        okList.Value![0].LetterType.Should().Be("confirmation_letter");
        okList.Value[0].Subject.Should().Contain("Confirmation of Clinical Monitoring Visit");
        okList.Value[0].BodyMarkdown.Should().Contain("Confirmation Letter");

        var itemResult = await MonitoringVisitEndpoints.GetMonitoringVisitLetterByTypeAsync(
            "VISIT-001", "confirmation_letter", _db, caller, TestContext.Current.CancellationToken);
        var okItem = itemResult.Should().BeOfType<Ok<GeneratedLetterResponseDto>>().Subject;
        okItem.Value!.Id.Should().Be("LTR-001");
        okItem.Value.StudyCode.Should().Be(StudyId);
        okItem.Value.SiteCode.Should().Be(SiteId);

        _db.CtmsMonitoringVisitFindings.Add(new CtmsMonitoringVisitFinding
        {
            VisitId = "VISIT-001",
            Text = "ICF version mismatch on Subject 004",
            Severity = "MAJOR",
            ResolutionStatus = "OPEN",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "cra-1",
            ReasonForChange = "Recorded during IMV",
            VersionIndex = 1
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visitsResult = await MonitoringVisitEndpoints.GetMonitoringVisitsAsync(
            StudyId, SiteId, null, null, _db, caller, TestContext.Current.CancellationToken);
        var visitsOk = visitsResult.Should().BeOfType<Ok<List<MonitoringVisitResponseDto>>>().Subject;
        visitsOk.Value.Should().ContainSingle(v => v.Id == "VISIT-001")
            .Which.Findings.Should().ContainSingle(f => f.Text == "ICF version mismatch on Subject 004" && f.Severity == "MAJOR");

        var missingResult = await MonitoringVisitEndpoints.GetMonitoringVisitLetterByTypeAsync(
            "VISIT-001", "follow_up_letter", _db, caller, TestContext.Current.CancellationToken);
        missingResult.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task MonitoringVisits_EnforcesCallerScopeIsolation()
    {
        var foreignVisit = new CtmsMonitoringVisit("VISIT-FOREIGN", CtmsMonitoringVisitStatus.Completed)
        {
            StudyId = StudyId,
            SiteId = "SITE-999",
            CraId = CraId,
            VisitType = "IMV",
            ScheduledDate = _clock.UtcNow.AddDays(-2),
            ActualDate = _clock.UtcNow.AddDays(-1),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "cra-2",
            ReasonForChange = "Completed"
        };
        _db.CtmsMonitoringVisits.Add(foreignVisit);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scopedCra = Caller("CRA", "cra-1", [StudyId], [SiteId]);
        scopedCra.Request.Headers["X-Change-Reason"] = "Attempt cross-site action";

        var schedDenied = await MonitoringVisitEndpoints.ScheduleMonitoringVisitAsync(
            new MonitoringVisitCreateDto(StudyId, "SITE-999", CraId, "IMV", _clock.UtcNow.AddDays(5)),
            _db, scopedCra, _clock, TestContext.Current.CancellationToken);
        schedDenied.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var completeDenied = await MonitoringVisitEndpoints.CompleteMonitoringVisitAsync(
            foreignVisit.Id, new MonitoringVisitCompleteDto(_clock.UtcNow), _db, scopedCra, _clock, TestContext.Current.CancellationToken);
        completeDenied.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var signDenied = await MonitoringVisitEndpoints.SignOffMonitoringVisitAsync(
            foreignVisit.Id, _db, Caller("monitor", "mon-1", [StudyId], [SiteId]), _clock, TestContext.Current.CancellationToken);
        signDenied.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var lettersDenied = await MonitoringVisitEndpoints.GetMonitoringVisitLettersAsync(
            foreignVisit.Id, _db, scopedCra, TestContext.Current.CancellationToken);
        var problem = lettersDenied.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions.Should().ContainKey("code").WhoseValue.Should().Be(CtmsScopeGuard.ForbiddenCode);
    }
}
