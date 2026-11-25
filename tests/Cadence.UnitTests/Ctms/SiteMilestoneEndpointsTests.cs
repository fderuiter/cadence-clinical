using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application;
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

public sealed class SiteMilestoneEndpointsTests
{
    private const string StudyId = "STUDY-2026-001";
    private const string SiteId = "SITE-101";

    private readonly CtmsDbContext _db;
    private readonly FakeClock _clock;
    private readonly SiteMilestoneApplicationService _service;

    public SiteMilestoneEndpointsTests()
    {
        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CtmsDbContext(options);
        _clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero));
        _service = new SiteMilestoneApplicationService(_db, new CtmsUnitOfWork(_db), _clock);
    }

    private static DefaultHttpContext Caller(
        string role = RoleNormalization.ROLE_SYSADMIN,
        string userId = "manager-1",
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
    public async Task CreateMilestone_WithValidRequestAndHeaderReason_PersistsMilestoneAndAuditLog()
    {
        var caller = Caller();
        caller.Request.Headers["X-Change-Reason"] = "Initial activation milestone planning";

        var plannedDate = new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);
        var request = new SiteMilestoneCreateDto(
            Site: SiteId,
            Study: StudyId,
            MilestoneType: "SITE_ACTIVATION",
            PlannedDate: plannedDate
        );

        var result = await SiteMilestoneEndpoints.CreateSiteMilestoneAsync(
            request, _service, caller, TestContext.Current.CancellationToken);

        var created = result.Should().BeOfType<Created<SiteMilestoneResponseDto>>().Subject;
        created.Value.Should().NotBeNull();
        created.Value!.Status.Should().Be("PLANNED");
        created.Value.MilestoneType.Should().Be("SITE_ACTIVATION");
        created.Value.Site.Should().Be(SiteId);
        created.Value.Study.Should().Be(StudyId);
        created.Value.PlannedDate.Should().Be(plannedDate.ToString("o"));
        created.Value.ReasonForChange.Should().Be("Initial activation milestone planning");

        // Verify entity in DB
        var milestone = await _db.CtmsSiteMilestones.FindAsync([created.Value.Id], TestContext.Current.CancellationToken);
        milestone.Should().NotBeNull();
        milestone!.Status.Should().Be(CtmsSiteMilestoneStatus.Planned);

        // Verify audit log
        var audit = await _db.CtmsAuditLogs.SingleAsync(a => a.Action == "CREATE_SITE_MILESTONE", TestContext.Current.CancellationToken);
        audit.UserId.Should().Be("manager-1");
        audit.Details.Should().Contain("SITE_ACTIVATION");
        audit.ReasonForChange.Should().Be("Initial activation milestone planning");
    }

    [Fact]
    public async Task CreateMilestone_WithoutReason_ReturnsBadRequest()
    {
        var caller = Caller();
        var request = new SiteMilestoneCreateDto(
            Site: SiteId,
            Study: StudyId,
            MilestoneType: "SITE_ACTIVATION"
        );

        var result = await SiteMilestoneEndpoints.CreateSiteMilestoneAsync(
            request, _service, caller, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task GetSiteMilestones_ReturnsMilestonesFilteredByStudyAndSite()
    {
        var m1 = new CtmsSiteMilestone
        {
            SiteId = SiteId,
            StudyId = StudyId,
            MilestoneType = "IRB_APPROVAL",
            PlannedDate = _clock.UtcNow.AddDays(5),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Initial"
        };
        var m2 = new CtmsSiteMilestone
        {
            SiteId = "OTHER-SITE",
            StudyId = StudyId,
            MilestoneType = "IRB_APPROVAL",
            PlannedDate = _clock.UtcNow.AddDays(10),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Initial"
        };
        _db.CtmsSiteMilestones.AddRange(m1, m2);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await SiteMilestoneEndpoints.GetSiteMilestonesAsync(
            StudyId, SiteId, _service, Caller(), TestContext.Current.CancellationToken);

        var ok = result.Should().BeOfType<Ok<List<SiteMilestoneResponseDto>>>().Subject;
        ok.Value.Should().HaveCount(1);
        ok.Value![0].Id.Should().Be(m1.Id);
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-001")]
    public async Task UpdateMilestone_UpdatesStatusActualDateAndAuditLog()
    {
        var milestone = new CtmsSiteMilestone
        {
            SiteId = SiteId,
            StudyId = StudyId,
            MilestoneType = "SITE_ACTIVATION",
            PlannedDate = _clock.UtcNow.AddDays(-1),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Initial"
        };
        _db.CtmsSiteMilestones.Add(milestone);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var caller = Caller();
        caller.Request.Headers["X-Change-Reason"] = "Site conditions confirmed and greenlight certified";

        var actualDate = _clock.UtcNow;
        var request = new SiteMilestoneUpdateDto(
            Status: "ACHIEVED",
            ActualDate: actualDate
        );

        var result = await SiteMilestoneEndpoints.UpdateSiteMilestoneAsync(
            milestone.Id, request, _service, caller, TestContext.Current.CancellationToken);

        var ok = result.Should().BeOfType<Ok<SiteMilestoneResponseDto>>().Subject;
        ok.Value.Should().NotBeNull();
        ok.Value!.Status.Should().Be("ACHIEVED");
        ok.Value.ActualDate.Should().Be(actualDate.ToString("o"));
        ok.Value.VersionIndex.Should().Be(2);

        // Verify DB entity
        var updated = await _db.CtmsSiteMilestones.FindAsync([milestone.Id], TestContext.Current.CancellationToken);
        updated!.Status.Should().Be(CtmsSiteMilestoneStatus.Achieved);
        updated.ActualDate.Should().Be(actualDate);
        updated.ReasonForChange.Should().Be("Site conditions confirmed and greenlight certified");

        // Verify audit log
        var audit = await _db.CtmsAuditLogs.SingleAsync(a => a.Action == "UPDATE_SITE_MILESTONE", TestContext.Current.CancellationToken);
        audit.Details.Should().Contain("ACHIEVED");
        audit.ReasonForChange.Should().Be("Site conditions confirmed and greenlight certified");
    }

    [Fact]
    public async Task UpdateMilestone_NotFound_Returns404()
    {
        var caller = Caller();
        caller.Request.Headers["X-Change-Reason"] = "Update reason";
        var request = new SiteMilestoneUpdateDto(Status: "ACHIEVED");

        var result = await SiteMilestoneEndpoints.UpdateSiteMilestoneAsync(
            "NON-EXISTENT", request, _service, caller, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task SiteMilestones_EnforcesCallerScopeIsolation()
    {
        var ownMilestone = new CtmsSiteMilestone
        {
            SiteId = SiteId,
            StudyId = StudyId,
            MilestoneType = "IRB_APPROVAL",
            PlannedDate = _clock.UtcNow.AddDays(3),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Initial"
        };
        var foreignMilestone = new CtmsSiteMilestone
        {
            SiteId = "SITE-999",
            StudyId = StudyId,
            MilestoneType = "IRB_APPROVAL",
            PlannedDate = _clock.UtcNow.AddDays(5),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "setup",
            ReasonForChange = "Initial"
        };
        _db.CtmsSiteMilestones.AddRange(ownMilestone, foreignMilestone);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Site-scoped CRA omitting site_id sees only assigned site milestones
        var scopedCra = Caller("CRA", "cra-1", [StudyId], [SiteId]);
        var listResult = await SiteMilestoneEndpoints.GetSiteMilestonesAsync(
            StudyId, null, _service, scopedCra, TestContext.Current.CancellationToken);
        var okList = listResult.Should().BeOfType<Ok<List<SiteMilestoneResponseDto>>>().Subject;
        okList.Value!.Select(m => m.Site).Should().Equal(SiteId);

        // Cross-site create & update fail closed with 403 FORBIDDEN_SCOPE
        scopedCra.Request.Headers["X-Change-Reason"] = "Unauthorized site mutation";
        var createDenied = await SiteMilestoneEndpoints.CreateSiteMilestoneAsync(
            new SiteMilestoneCreateDto("SITE-999", StudyId, "SITE_ACTIVATION"),
            _service, scopedCra, TestContext.Current.CancellationToken);
        createDenied.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var updateDenied = await SiteMilestoneEndpoints.UpdateSiteMilestoneAsync(
            foreignMilestone.Id, new SiteMilestoneUpdateDto(Status: "ACHIEVED"),
            _service, scopedCra, TestContext.Current.CancellationToken);
        var deniedProblem = updateDenied.Should().BeOfType<ProblemHttpResult>().Subject;
        deniedProblem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        deniedProblem.ProblemDetails.Extensions.Should().ContainKey("code").WhoseValue.Should().Be(CtmsScopeGuard.ForbiddenCode);

        // Cross-study read fails closed with 403 FORBIDDEN_SCOPE
        var crossStudyCaller = Caller(RoleNormalization.ROLE_SPONSOR_DM, "dm-1", ["OTHER-STUDY"], []);
        var crossStudyRead = await SiteMilestoneEndpoints.GetSiteMilestonesAsync(
            StudyId, SiteId, _service, crossStudyCaller, TestContext.Current.CancellationToken);
        crossStudyRead.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
