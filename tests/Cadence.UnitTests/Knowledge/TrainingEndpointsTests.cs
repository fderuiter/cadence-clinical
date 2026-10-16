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

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Knowledge;

public class TrainingEndpointsTests
{
    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly TestTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));

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
    public async Task BatchAssign_EnforcesMaxBatchLimitGuardrail()
    {
        // Arrange
        using var db = CreateDbContext(nameof(BatchAssign_EnforcesMaxBatchLimitGuardrail));
        var targetPersonnel = Enumerable.Range(1, 205).Select(i => $"user_{i}").ToList();
        var request = new BatchAssignCurriculumRequest(
            CurriculumId: "curriculum-v3-delta",
            StudyId: "CARDIO-2026",
            SiteIds: ["SITE-101"],
            Roles: ["Sub-Investigator"],
            TargetPersonnelIds: targetPersonnel,
            ReasonForChange: "Protocol amendment deployment");

        // Act
        var endpointMethod = typeof(TrainingEndpoints).GetMethod("BatchAssignCurriculumAsync", BindingFlags.NonPublic | BindingFlags.Static);
        var result = await (Task<IResult>)endpointMethod!.Invoke(null, [request, db, Caller(), _timeProvider, TestContext.Current.CancellationToken])!;

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Detail.Should().Contain("limit of 200 target personnel");
    }

    [Fact]
    public async Task BatchAssign_CreatesAssignmentsAndSkipsDuplicates()
    {
        // Arrange
        using var db = CreateDbContext(nameof(BatchAssign_CreatesAssignmentsAndSkipsDuplicates));

        // Seed an active assignment for user_1
        db.TrainingAssignments.Add(new TrainingAssignment
        {
            Id = "existing-asn-1",
            CurriculumId = "curriculum-v3-delta",
            StudyId = "CARDIO-2026",
            SiteId = "SITE-101",
            UserId = "user_1",
            UserName = "Personnel user_1",
            UserRole = "Clinical Research Coordinator",
            Status = "PENDING",
            CreatedAt = _timeProvider.GetUtcNow()
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = new BatchAssignCurriculumRequest(
            CurriculumId: "curriculum-v3-delta",
            StudyId: "CARDIO-2026",
            SiteIds: ["SITE-101"],
            Roles: ["Clinical Research Coordinator"],
            TargetPersonnelIds: ["user_1", "user_2", "user_3"],
            ReasonForChange: "Bulk assignment for site launch");

        // Act
        var endpointMethod = typeof(TrainingEndpoints).GetMethod("BatchAssignCurriculumAsync", BindingFlags.NonPublic | BindingFlags.Static);
        var result = await (Task<IResult>)endpointMethod!.Invoke(null, [request, db, Caller(), _timeProvider, TestContext.Current.CancellationToken])!;

        // Assert
        result.Should().BeOfType<Created<BatchAssignmentResponse>>();
        var createdResult = (Created<BatchAssignmentResponse>)result;
        var response = createdResult.Value!;

        response.TotalEligible.Should().Be(3);
        response.TotalAssigned.Should().Be(2);
        response.TotalSkipped.Should().Be(1);
        response.SkippedItems.Should().ContainSingle(s => s.UserId == "user_1");
        response.Assignments.Should().HaveCount(2);

        var dbAssignments = await db.TrainingAssignments.ToListAsync(TestContext.Current.CancellationToken);
        dbAssignments.Should().HaveCount(3); // 1 pre-existing + 2 newly assigned
    }

    /// <summary>Assigning a curriculum in a study the caller is not assigned to is refused and assigns nobody (#5361).</summary>
    [Fact]
    public async Task BatchAssign_ForAStudyOutsideTheCallersStudies_IsRefusedAndAssignsNobody()
    {
        using var db = CreateDbContext(nameof(BatchAssign_ForAStudyOutsideTheCallersStudies_IsRefusedAndAssignsNobody));
        var request = new BatchAssignCurriculumRequest(
            CurriculumId: "curriculum-v3-delta",
            StudyId: "CARDIO-2026",
            SiteIds: ["SITE-101"],
            Roles: ["Clinical Research Coordinator"],
            TargetPersonnelIds: ["user_1"],
            ReasonForChange: "Site launch");

        var endpointMethod = typeof(TrainingEndpoints).GetMethod("BatchAssignCurriculumAsync", BindingFlags.NonPublic | BindingFlags.Static);
        var result = await (Task<IResult>)endpointMethod!.Invoke(null, [request, db, Caller("ONCO-2027"), _timeProvider, TestContext.Current.CancellationToken])!;

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await db.TrainingAssignments.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.OrgAuditLogs.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    private static DefaultHttpContext Caller(params string[] studies)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "sponsor-dm-1",
            Roles = ["sponsor_dm"],
            AssignedStudies = studies.Length == 0 ? ["*"] : studies,
        };
        return httpContext;
    }
}
