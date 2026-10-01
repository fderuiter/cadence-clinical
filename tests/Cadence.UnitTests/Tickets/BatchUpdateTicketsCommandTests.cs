using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Domain;
using Cadence.Apps.Tickets.Endpoints;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Tickets;

public class BatchUpdateTicketsCommandTests
{
    private static TicketsDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<TicketsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new TicketsDbContext(options);
    }

    private static HttpContext CreateHttpContext(string userId, string role)
    {
        var httpContext = new DefaultHttpContext();
        var principal = new Principal
        {
            UserId = userId,
            Roles = [role],
            RawRoles = [role]
        };
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = principal;

        var claims = new List<Claim>
        {
            new(CadenceClaimTypes.Subject, userId),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);

        return httpContext;
    }

    [Fact(DisplayName = "BatchUpdateTickets_ValidTickets_UpdatesStatusAndWritesAuditEntries")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task BatchUpdateTickets_ValidTickets_UpdatesStatusAndWritesAuditEntries()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        var httpContext = CreateHttpContext("dm_user_1", "sponsor_dm");

        var ticket1 = new Ticket
        {
            Id = "tk-101",
            Reference = "DEV-101",
            Title = "Discrepancy 1",
            Status = "OPEN",
            CreatedBy = "crc_1",
            ReasonForChange = "Initial"
        };
        var ticket2 = new Ticket
        {
            Id = "tk-102",
            Reference = "DEV-102",
            Title = "Discrepancy 2",
            Status = "IN_PROGRESS",
            CreatedBy = "crc_1",
            ReasonForChange = "Initial"
        };

        dbContext.Tickets.AddRange(ticket1, ticket2);
        await dbContext.SaveChangesAsync(ct);

        var command = new BatchUpdateTicketsCommand(
            TicketIds: ["tk-101", "tk-102"],
            TicketIdsAlt: null,
            Action: "RESOLVE",
            Rationale: "Verified discrepancy tickets clearance during DB lock",
            ResolutionRationale: null,
            RootCauseCategory: "DATA_ENTRY_CORRECTION",
            RootCauseSummary: "Correction verified against source document",
            ResolutionCode: "VERIFIED_CORRECTED",
            SignatureToken: "sig_token_part11_test",
            SigToken: null,
            SignatureMeaning: "I authoritatively verify and close these clinical deviations/queries.");

        // Act
        var result = await TicketEndpoints.BatchUpdateTicketsAsync(
            command, httpContext, dbContext, clock, ct);

        // Assert
        result.Should().NotBeNull();
        var okResult = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<BatchUpdateTicketsResult>>().Subject;
        var batchResult = okResult.Value;

        batchResult.Should().NotBeNull();
        batchResult!.SuccessCount.Should().Be(2);
        batchResult.FailureCount.Should().Be(0);
        batchResult.Results.Should().HaveCount(2);

        var t1 = await dbContext.Tickets.FindAsync(["tk-101"], ct);
        t1.Should().NotBeNull();
        t1!.Status.Should().Be("RESOLVED");
        t1.SignatureToken.Should().Be("sig_token_part11_test");
        t1.RootCauseCategory.Should().Be("DATA_ENTRY_CORRECTION");
        t1.ResolutionCode.Should().Be("VERIFIED_CORRECTED");

        var t2 = await dbContext.Tickets.FindAsync(["tk-102"], ct);
        t2.Should().NotBeNull();
        t2!.Status.Should().Be("RESOLVED");

        var auditLogs = await dbContext.TicketAuditLogs.Where(a => a.TicketId == "tk-101" || a.TicketId == "tk-102").ToListAsync(ct);
        auditLogs.Should().HaveCount(2);
    }

    [Fact(DisplayName = "BatchUpdateTickets_MixedValidAndClosedTickets_FlagsFailedItemsWithoutFailingValidItems")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task BatchUpdateTickets_MixedValidAndClosedTickets_FlagsFailedItemsWithoutFailingValidItems()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        var httpContext = CreateHttpContext("dm_user_1", "sponsor_dm");

        var ticket1 = new Ticket { Id = "tk-201", Reference = "DEV-201", Status = "OPEN", CreatedBy = "crc", ReasonForChange = "Init" };
        var ticket2 = new Ticket { Id = "tk-202", Reference = "DEV-202", Status = "CLOSED", CreatedBy = "crc", ReasonForChange = "Init" };

        dbContext.Tickets.AddRange(ticket1, ticket2);
        await dbContext.SaveChangesAsync(ct);

        var command = new BatchUpdateTicketsCommand(
            TicketIds: ["tk-201", "tk-202", "tk-nonexistent"],
            TicketIdsAlt: null,
            Action: "RESOLVE",
            Rationale: "Bulk resolution note",
            ResolutionRationale: null,
            RootCauseCategory: null,
            RootCauseSummary: null,
            ResolutionCode: null,
            SignatureToken: "token_123",
            SigToken: null,
            SignatureMeaning: null);

        // Act
        var result = await TicketEndpoints.BatchUpdateTicketsAsync(
            command, httpContext, dbContext, clock, ct);

        // Assert
        var okResult = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<BatchUpdateTicketsResult>>().Subject;
        var batchResult = okResult.Value!;

        batchResult.SuccessCount.Should().Be(1);
        batchResult.FailureCount.Should().Be(2);

        var item1 = batchResult.Results.First(r => r.TicketId == "tk-201");
        item1.Success.Should().BeTrue();
        item1.Status.Should().Be("RESOLVED");

        var item2 = batchResult.Results.First(r => r.TicketId == "tk-202");
        item2.Success.Should().BeFalse();
        item2.ErrorMessage.Should().Contain("CLOSED");

        var item3 = batchResult.Results.First(r => r.TicketId == "tk-nonexistent");
        item3.Success.Should().BeFalse();
        item3.ErrorMessage.Should().Contain("was not found");
    }

    [Fact(DisplayName = "BatchUpdateTickets_WithoutCapability_ReturnsForbidden")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task BatchUpdateTickets_WithoutCapability_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        var httpContext = CreateHttpContext("unauth_user", "guest_persona");

        var command = new BatchUpdateTicketsCommand(
            TicketIds: ["tk-301"],
            TicketIdsAlt: null,
            Action: "RESOLVE",
            Rationale: "Attempted resolve",
            ResolutionRationale: null,
            RootCauseCategory: null,
            RootCauseSummary: null,
            ResolutionCode: null,
            SignatureToken: null,
            SigToken: null,
            SignatureMeaning: null);

        // Act
        var result = await TicketEndpoints.BatchUpdateTicketsAsync(
            command, httpContext, dbContext, clock, ct);

        // Assert
        var problemResult = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact(DisplayName = "BatchUpdateTickets_Exceeds100Tickets_ReturnsBadRequest")]
    public async Task BatchUpdateTickets_Exceeds100Tickets_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        var httpContext = CreateHttpContext("dm_user_1", "sponsor_dm");

        var largeList = Enumerable.Range(1, 101).Select(i => $"tk-{i}").ToList();
        var command = new BatchUpdateTicketsCommand(
            TicketIds: largeList,
            TicketIdsAlt: null,
            Action: "RESOLVE",
            Rationale: "Too many tickets",
            ResolutionRationale: null,
            RootCauseCategory: null,
            RootCauseSummary: null,
            ResolutionCode: null,
            SignatureToken: null,
            SigToken: null,
            SignatureMeaning: null);

        // Act
        var result = await TicketEndpoints.BatchUpdateTicketsAsync(
            command, httpContext, dbContext, clock, ct);

        // Assert
        var problemResult = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problemResult.ProblemDetails!.Detail.Should().Contain("100");
    }

    [Fact(DisplayName = "BatchUpdateTickets_EscalateAction_UpdatesSlaAndAssignee")]
    public async Task BatchUpdateTickets_EscalateAction_UpdatesSlaAndAssignee()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        var httpContext = CreateHttpContext("dm_user_1", "sponsor_dm");

        var ticket = new Ticket { Id = "tk-401", Reference = "DEV-401", Status = "OPEN", CreatedBy = "crc", ReasonForChange = "Init" };
        dbContext.Tickets.Add(ticket);
        await dbContext.SaveChangesAsync(ct);

        var command = new BatchUpdateTicketsCommand(
            TicketIds: ["tk-401"],
            TicketIdsAlt: null,
            Action: "ESCALATE",
            Rationale: "Escalating stagnant query to PI",
            ResolutionRationale: null,
            RootCauseCategory: null,
            RootCauseSummary: null,
            ResolutionCode: null,
            SignatureToken: null,
            SigToken: null,
            SignatureMeaning: null);

        // Act
        var result = await TicketEndpoints.BatchUpdateTicketsAsync(
            command, httpContext, dbContext, clock, ct);

        // Assert
        var okResult = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<BatchUpdateTicketsResult>>().Subject;
        okResult.Value!.SuccessCount.Should().Be(1);

        var updated = await dbContext.Tickets.FindAsync(["tk-401"], ct);
        updated!.Status.Should().Be("ESCALATED");
        updated.EscalationCount.Should().Be(1);
        updated.AssigneeRole.Should().Be("PRINCIPAL_INVESTIGATOR");
        updated.LastEscalatedAt.Should().Be(clock.UtcNow);
    }

    [Fact(DisplayName = "BatchUpdateTickets_PauseSLAAction_UpdatesSlaPausedAt")]
    public async Task BatchUpdateTickets_PauseSLAAction_UpdatesSlaPausedAt()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        var httpContext = CreateHttpContext("dm_user_1", "sponsor_dm");

        var ticket = new Ticket { Id = "tk-501", Reference = "DEV-501", Status = "IN_PROGRESS", CreatedBy = "crc", ReasonForChange = "Init" };
        dbContext.Tickets.Add(ticket);
        await dbContext.SaveChangesAsync(ct);

        var command = new BatchUpdateTicketsCommand(
            TicketIds: ["tk-501"],
            TicketIdsAlt: null,
            Action: "PAUSE_SLA",
            Rationale: "Awaiting site clarification",
            ResolutionRationale: null,
            RootCauseCategory: null,
            RootCauseSummary: null,
            ResolutionCode: null,
            SignatureToken: null,
            SigToken: null,
            SignatureMeaning: null);

        // Act
        var result = await TicketEndpoints.BatchUpdateTicketsAsync(
            command, httpContext, dbContext, clock, ct);

        // Assert
        var okResult = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<BatchUpdateTicketsResult>>().Subject;
        okResult.Value!.SuccessCount.Should().Be(1);

        var updated = await dbContext.Tickets.FindAsync(["tk-501"], ct);
        updated!.Status.Should().Be("WAITING_ON_SITE");
        updated.SlaPausedAt.Should().Be(clock.UtcNow);
    }
}
