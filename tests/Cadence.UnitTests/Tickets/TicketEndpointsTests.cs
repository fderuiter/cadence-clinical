using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Endpoints;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.UnitTests.Tickets;

/// <summary>
/// Verifies support ticket persistence, deterministic 6-queue routing, Dual-Target SLA timestamps via <see cref="IClock"/>,
/// verified caller identity attribution, study/site scope isolation, mandatory <c>X-Change-Reason</c>, direct PII rejection,
/// and shared Merkle-chained <c>audit_log_entries</c> recording.
/// </summary>
public sealed class TicketEndpointsTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 20, 14, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Cadence.Apps.Tickets.Program> _factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public TicketEndpointsTests()
    {
        _connection.Open();
        _factory = new WebApplicationFactory<Cadence.Apps.Tickets.Program>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret().UseTestPostgresConnectionString();
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
                services.RemoveAll<DbContextOptions<TicketsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<TicketsDbContext>>();
                services.AddDbContext<TicketsDbContext>((sp, options) =>
                {
                    options.UseSqlite(_connection);
                    options.AddInterceptors(new GxPAuditInterceptor(
                        () =>
                        {
                            HttpContext? ctx = sp.GetService<IHttpContextAccessor>()?.HttpContext;
                            return (ctx is null ? null : CallerScope.Principal(ctx)?.UserId)
                                   ?? ctx?.User.FindFirst(CadenceClaimTypes.Subject)?.Value
                                   ?? throw new InvalidOperationException("Authenticated caller required.");
                        },
                        sp.GetRequiredService<IClock>()));
                });
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task EnsureDbCreatedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        await db.Database.EnsureCreatedAsync(Ct);
    }

    private HttpClient CreateScopedClient(
        string userId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string>? studyIds = null,
        IReadOnlyList<string>? siteIds = null)
    {
        var identity = new GatewayIdentity(
            UserId: userId,
            Roles: roles,
            SiteIds: siteIds ?? [],
            TenantId: TestSecurityContext.TenantCode,
            StudyIds: studyIds ?? TestSecurityContext.AllStudies);
        return _factory.CreateDefaultClient(new TestSecurityContext.GatewaySigningHandler(identity));
    }

    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-TCK-001")]
    [GxPRequirement("PRD-TCK-002")]
    [InlineData("PROTOCOL_DEVIATION", "/studies/DEMO-01", "sponsor_designer", "MEDIUM", "MINOR", "PROTOCOL_DESIGNER", 240, 24)]
    [InlineData("PROTOCOL_DEVIATION", "/ecrf", "site_crc", "CRITICAL", "CRITICAL", "DATA_CAPTURE_ECRF", 15, 4)]
    [InlineData("SITE_OPERATIONS", "/ssu", "cra_monitor", "HIGH", "MAJOR", "SUBJECT_MANAGEMENT_RTSM", 60, 8)]
    [InlineData("SYSTEM_SUPPORT", "/exports", "data_manager", "MEDIUM", "MINOR", "REPORTING_ANALYTICS", 240, 24)]
    [InlineData("ACCESS_CONTROL", "/settings", "super_admin", "LOW", "MINOR", "PLATFORM_ADMIN_ACCESS", 240, 72)]
    [InlineData("REGULATORY_QUERY", "/audit", "data_manager", "HIGH", "MAJOR", "REGULATORY_COMPLIANCE", 60, 8)]
    public async Task CreateTicket_DeterministicallyRoutesAllSixQueuesAndComputesDualTargetSla(
        string category,
        string sourceRoute,
        string persona,
        string priority,
        string severity,
        string expectedQueue,
        int expectedAckMinutes,
        int expectedResolveHours)
    {
        await EnsureDbCreatedAsync();
        using var client = CreateScopedClient(
            "usr-crc-queue-test",
            ["CRC", "Data Manager"],
            studyIds: ["STUDY-ONC-202"],
            siteIds: ["SITE-101"]);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto(
                Title: $"Escalation from {sourceRoute}",
                Description: "Operational support request for pseudonymous subject SUBJ-8812.",
                Category: category,
                GxpSeverity: severity,
                Priority: priority,
                StudyScope: "STUDY-ONC-202",
                SiteScope: "SITE-101",
                SubjectCode: "SUBJ-8812",
                SourceRoute: sourceRoute,
                Persona: persona)),
        };
        req.Headers.Add(GatewayHeaders.ChangeReason, "Escalated from contextual help drawer");

        var response = await client.SendAsync(req, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<TicketResponseDto>(cancellationToken: Ct);
        created.Should().NotBeNull();
        created!.Reference.Should().StartWith("TKT-2026-");
        created.AssignedQueue.Should().Be(expectedQueue);
        created.Reporter.Should().Be("usr-crc-queue-test");
        created.CreatedBy.Should().Be("usr-crc-queue-test");
        created.CreatedAt.Should().Be(_clock.UtcNow);
        created.AcknowledgeTargetAt.Should().Be(_clock.UtcNow.AddMinutes(expectedAckMinutes));
        created.SlaTargetAt.Should().Be(_clock.UtcNow.AddHours(expectedResolveHours));
        created.ReasonForChange.Should().Be("Escalated from contextual help drawer");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-SYS-003")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task CreateAndListTickets_EnforcesChangeReasonScopeIsolationAndMerkleAuditChain()
    {
        await EnsureDbCreatedAsync();
        using var site101Client = CreateScopedClient(
            "usr-site101-crc",
            ["CRC"],
            studyIds: ["STUDY-A"],
            siteIds: ["SITE-101"]);

        // 1. Missing X-Change-Reason is rejected with 400 Bad Request
        using var noReasonReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto(
                Title: "Missing reason check",
                Description: "Should be rejected without X-Change-Reason.")),
        };
        var noReasonResp = await site101Client.SendAsync(noReasonReq, Ct);
        noReasonResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Out-of-scope study or site is rejected with 403 Forbidden
        using var wrongSiteReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto(
                Title: "Cross-site attempt",
                Description: "Should be refused by CallerScope.",
                StudyScope: "STUDY-A",
                SiteScope: "SITE-999")),
        };
        wrongSiteReq.Headers.Add(GatewayHeaders.ChangeReason, "Scope check");
        var wrongSiteResp = await site101Client.SendAsync(wrongSiteReq, Ct);
        wrongSiteResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Valid write within scope succeeds and auto-populates study/site when omitted
        using var validReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto(
                Title: "Locked visit date field on Cycle 1",
                Description: "Unable to enter visit date for SUBJ-1004.",
                Category: "PROTOCOL_DEVIATION",
                SourceRoute: "/ecrf")),
        };
        validReq.Headers.Add(GatewayHeaders.ChangeReason, "Logged site query escalation");
        var validResp = await site101Client.SendAsync(validReq, Ct);
        validResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var saved = await validResp.Content.ReadFromJsonAsync<TicketResponseDto>(cancellationToken: Ct);
        saved!.StudyScope.Should().Be("STUDY-A");
        saved.SiteScope.Should().Be("SITE-101");

        // 4. Caller at SITE-102 cannot see SITE-101's ticket
        using var site102Client = CreateScopedClient(
            "usr-site102-crc",
            ["CRC"],
            studyIds: ["STUDY-A"],
            siteIds: ["SITE-102"]);
        var site102List = await site102Client.GetFromJsonAsync<List<TicketResponseDto>>("/api/v1/tickets", Ct);
        site102List.Should().BeEmpty();

        var site102Get = await site102Client.GetAsync($"/api/v1/tickets/{saved.Id}", Ct);
        site102Get.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 5. Read-only auditor can list/read in-scope tickets but is forbidden from creating tickets
        using var auditorClient = CreateScopedClient(
            "usr-auditor-01",
            ["Auditor"],
            studyIds: ["STUDY-A"]);
        var auditorList = await auditorClient.GetFromJsonAsync<List<TicketResponseDto>>("/api/v1/tickets", Ct);
        auditorList.Should().ContainSingle(t => t.Reference == saved.Reference);

        using var auditorPost = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto("Auditor write", "Should fail 403")),
        };
        auditorPost.Headers.Add(GatewayHeaders.ChangeReason, "Audit probe");
        (await auditorClient.SendAsync(auditorPost, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 6. Verify shared Merkle-chained AuditLogEntries recorded and legacy TicketAuditLog untouched
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var auditEntries = await db.AuditLogEntries.AsNoTracking().ToListAsync(Ct);
        var ticketAudit = auditEntries.Should().ContainSingle(e =>
            e.EntityId == saved.Id &&
            e.Action == "TICKET_CREATED" &&
            e.UserId == "usr-site101-crc" &&
            e.ReasonForChange == "Logged site query escalation").Subject;
        ticketAudit.BlockHash.Should().Be(AuditLogEntry.ComputeBlockHash(
            ticketAudit.EntityName,
            ticketAudit.EntityId,
            ticketAudit.Action,
            AuditLogEntry.NormalizeTimestamp(ticketAudit.Timestamp),
            ticketAudit.UserId,
            ticketAudit.VersionIndex,
            ticketAudit.ChangesJson,
            ticketAudit.PredecessorHash));
        (await db.TicketAuditLogs.CountAsync(Ct)).Should().Be(0);
    }

    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-TCK-001")]
    [InlineData("Contact jane.patient@clinic-mail.org about visit", null)] // deid-ignore: synthetic PII rejection test fixture
    [InlineData("Participant SSN 123-45-6789 entered in notes", null)] // deid-ignore: synthetic PII rejection test fixture
    [InlineData("Call participant at (415) 555-2671 immediately", null)] // deid-ignore: synthetic PII rejection test fixture
    [InlineData("Verified DOB: 1984-05-19 on intake sheet", null)] // deid-ignore: synthetic PII rejection test fixture
    [InlineData("Check Vital Signs form", "Jane Doe")] // deid-ignore: synthetic PII rejection test fixture
    public async Task CreateTicket_RejectsDirectParticipantPiiAndWritesNothing(string narrative, string? subjectField)
    {
        await EnsureDbCreatedAsync();
        using var client = CreateScopedClient("usr-crc-pii", ["CRC"], studyIds: ["STUDY-PII"], siteIds: ["SITE-101"]);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto(
                Title: "Form entry question",
                Description: narrative,
                StudyScope: "STUDY-PII",
                SiteScope: "SITE-101",
                SubjectCode: subjectField)),
        };
        req.Headers.Add(GatewayHeaders.ChangeReason, "Testing PII guardrail");

        var response = await client.SendAsync(req, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        (await db.Tickets.CountAsync(Ct)).Should().Be(0);
    }
}
