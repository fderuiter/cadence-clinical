using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Knowledge;

/// <summary>
/// Training answers from the module's own records only (#5400): no curriculum, assignment, site or person is invented
/// when the records are missing, and batch assignment reaches only staff on the study's delegation log.
/// </summary>
public class TrainingEndpointsTests
{
    private const string Study = "CARDIO-2026";
    private const string CurriculumId = "curriculum-v3-delta";

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static KnowledgeDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new KnowledgeDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task ListCurricula_WithNoCurricula_ReturnsAnEmptyListAndWritesNothing()
    {
        using var db = CreateDbContext(nameof(ListCurricula_WithNoCurricula_ReturnsAnEmptyListAndWritesNothing));

        var result = await Invoke("ListCurriculaAsync", null, null, db, Caller(), Ct);

        result.Should().BeOfType<Ok<List<TrainingCurriculumDto>>>().Which.Value.Should().BeEmpty();
        (await db.TrainingCurricula.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task GetCurriculum_ThatDoesNotExist_Returns404()
    {
        using var db = CreateDbContext(nameof(GetCurriculum_ThatDoesNotExist_Returns404));

        var result = await Invoke("GetCurriculumAsync", CurriculumId, db, Caller(), Ct);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task GetCurriculum_InAnotherStudy_Returns404()
    {
        using var db = CreateDbContext(nameof(GetCurriculum_InAnotherStudy_Returns404));
        await SeedCurriculumAsync(db);

        var result = await Invoke("GetCurriculumAsync", CurriculumId, db, Caller("ONCO-2027"), Ct);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task GetAssignmentDetail_ThatDoesNotExist_Returns404()
    {
        using var db = CreateDbContext(nameof(GetAssignmentDetail_ThatDoesNotExist_Returns404));

        var result = await Invoke("GetAssignmentDetailAsync", "asn-missing", db, Caller(), Ct);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task BatchAssign_AssignsTheDelegationLogStaffAndSkipsDuplicates()
    {
        using var db = CreateDbContext(nameof(BatchAssign_AssignsTheDelegationLogStaffAndSkipsDuplicates));
        await SeedCurriculumAsync(db);
        await SeedStaffAsync(db,
            ("user_1", "SITE-101", "Clinical Research Coordinator"),
            ("user_2", "SITE-101", "Clinical Research Coordinator"),
            ("user_3", "SITE-101", "Sub-Investigator"),
            ("user_4", "SITE-102", "Clinical Research Coordinator"));
        db.TrainingAssignments.Add(new TrainingAssignment
        {
            Id = "existing-asn-1",
            CurriculumId = CurriculumId,
            StudyId = Study,
            SiteId = "SITE-101",
            UserId = "user_1",
            UserName = "Staff user_1",
            UserRole = "Clinical Research Coordinator",
            Status = "PENDING",
            CreatedAt = _clock.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var result = await BatchAssign(db, Request(sites: ["SITE-101"], roles: ["Clinical Research Coordinator"]), Caller());

        var response = result.Should().BeOfType<Created<BatchAssignmentResponse>>().Which.Value!;
        response.TotalEligible.Should().Be(2);
        response.TotalAssigned.Should().Be(1);
        response.TotalSkipped.Should().Be(1);
        response.SkippedItems.Should().ContainSingle(s => s.UserId == "user_1");
        response.Assignments.Should().ContainSingle().Which.Should().Match<TrainingAssignmentDto>(a =>
            a.UserId == "user_2" && a.UserName == "Staff user_2" && a.SiteId == "SITE-101");

        (await db.TrainingAssignments.Select(a => a.UserId).ToListAsync(Ct)).Should().BeEquivalentTo("user_1", "user_2");
    }

    [Fact]
    public async Task BatchAssign_ForAStudyOutsideTheCallersStudies_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_ForAStudyOutsideTheCallersStudies_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        await SeedStaffAsync(db, ("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101"]), Caller("ONCO-2027"));

        await ShouldRefuseAsync(db, result, StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task BatchAssign_NamingASiteTheCallerDoesNotReach_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_NamingASiteTheCallerDoesNotReach_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        await SeedStaffAsync(db, ("user_1", "SITE-102", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-102"]), SiteCaller("SITE-101"));

        await ShouldRefuseAsync(db, result, StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task BatchAssign_WithNoAudience_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_WithNoAudience_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        await SeedStaffAsync(db, ("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task BatchAssign_ForACurriculumThatDoesNotExist_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_ForACurriculumThatDoesNotExist_IsRefusedAndAssignsNobody));
        await SeedStaffAsync(db, ("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task BatchAssign_ForAnotherStudysCurriculum_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_ForAnotherStudysCurriculum_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db, study: "ONCO-2027");
        await SeedStaffAsync(db, ("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task BatchAssign_ForAnInactiveCurriculum_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_ForAnInactiveCurriculum_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db, isActive: false);
        await SeedStaffAsync(db, ("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task BatchAssign_NamingASiteWithNoStaff_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_NamingASiteWithNoStaff_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        await SeedStaffAsync(db, ("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101", "SITE-999"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status422UnprocessableEntity);
        ((ProblemHttpResult)result).ProblemDetails.Detail.Should().Contain("SITE-999");
    }

    [Fact]
    public async Task BatchAssign_NamingSomeoneNotOnTheDelegationLog_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_NamingSomeoneNotOnTheDelegationLog_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        await SeedStaffAsync(db, ("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(people: ["user_1", "ghost"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status422UnprocessableEntity);
        ((ProblemHttpResult)result).ProblemDetails.Detail.Should().Contain("ghost");
    }

    [Fact]
    public async Task BatchAssign_WhenNoStaffMatchTheFilters_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_WhenNoStaffMatchTheFilters_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        await SeedStaffAsync(db, ("user_1", "SITE-101", "Clinical Research Coordinator"));

        var result = await BatchAssign(db, Request(sites: ["SITE-101"], roles: ["Pharmacist"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task BatchAssign_OverTheSynchronousLimit_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_OverTheSynchronousLimit_IsRefusedAndAssignsNobody));
        await SeedCurriculumAsync(db);
        await SeedStaffAsync(db, Enumerable.Range(1, 205)
            .Select(i => ($"user_{i}", "SITE-101", "Clinical Research Coordinator")).ToArray());

        var result = await BatchAssign(db, Request(sites: ["SITE-101"]), Caller());

        await ShouldRefuseAsync(db, result, StatusCodes.Status400BadRequest);
        ((ProblemHttpResult)result).ProblemDetails.Detail.Should().Contain("limit of 200 target personnel");
    }

    private static async Task<IResult> Invoke(string handler, params object?[] arguments)
    {
        var method = typeof(TrainingEndpoints).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static);
        return await (Task<IResult>)method!.Invoke(null, arguments)!;
    }

    private Task<IResult> BatchAssign(KnowledgeDbContext db, BatchAssignCurriculumRequest request, HttpContext caller) =>
        Invoke("BatchAssignCurriculumAsync", request, db, caller, _clock, Ct);

    private static BatchAssignCurriculumRequest Request(
        List<string>? sites = null, List<string>? roles = null, List<string>? people = null) =>
        new(CurriculumId, Study, sites, roles, people, "Protocol amendment deployment");

    private static async Task ShouldRefuseAsync(KnowledgeDbContext db, IResult result, int statusCode)
    {
        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(statusCode);
        (await db.TrainingAssignments.CountAsync(Ct)).Should().Be(0);
        (await db.OrgAuditLogs.CountAsync(Ct)).Should().Be(0);
    }

    private async Task SeedCurriculumAsync(KnowledgeDbContext db, string study = Study, bool isActive = true)
    {
        db.TrainingCurricula.Add(new TrainingCurriculum
        {
            Id = CurriculumId,
            StudyId = study,
            ProtocolVersion = "3.0",
            Title = "Protocol V3.0 Delta Amendment Curriculum",
            IsActive = isActive,
            CreatedAt = _clock.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    private async Task SeedStaffAsync(KnowledgeDbContext db, params (string UserId, string SiteId, string Role)[] staff)
    {
        foreach (var (userId, siteId, role) in staff)
        {
            db.DelegationRecords.Add(new DelegationRecord
            {
                Id = $"del-{userId}-{siteId}",
                StudyId = Study,
                SiteId = siteId,
                StaffUserId = userId,
                StaffName = $"Staff {userId}",
                Role = role,
                TaskCode = "EDC_DATA_ENTRY",
                Duties = "eCRF data entry",
                CreatedAt = _clock.UtcNow,
            });
        }

        await db.SaveChangesAsync(Ct);
    }

    private static DefaultHttpContext Caller(params string[] studies) => WithPrincipal(new Principal
    {
        UserId = "sponsor-dm-1",
        Roles = ["sponsor_dm"],
        AssignedStudies = studies.Length == 0 ? ["*"] : studies,
    });

    private static DefaultHttpContext SiteCaller(string site) => WithPrincipal(new Principal
    {
        UserId = "crc-1",
        Roles = [RoleNormalization.ROLE_CRC],
        AssignedStudies = [Study],
        AssignedSites = [site],
    });

    private static DefaultHttpContext WithPrincipal(Principal principal)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = principal;
        return httpContext;
    }
}
