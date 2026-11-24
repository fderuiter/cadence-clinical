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
                services.RemoveAll<ISigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore, ReplayCache>();
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
        IReadOnlyList<string>? siteIds = null) =>
        CreateClient(
            userId: userId,
            tenantId: TestSecurityContext.TenantCode,
            roles: roles,
            studyIds: studyIds,
            siteIds: siteIds);

    private HttpClient CreateClient(
        string userId,
        string tenantId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string>? studyIds = null,
        IReadOnlyList<string>? siteIds = null,
        string? sigToken = null)
    {
        var identity = new GatewayIdentity(
            UserId: userId,
            Roles: roles,
            SiteIds: siteIds ?? [],
            TenantId: tenantId,
            StudyIds: studyIds ?? TestSecurityContext.AllStudies,
            SigToken: sigToken,
            ProductFamilies: TestSecurityContext.AllProductFamilies);
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

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-TCK-001")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task Comments_EnforceInternalSponsorIsolation_SitePostingGuard_AndPiiRejection()
    {
        await EnsureDbCreatedAsync();
        using var safetyClient = CreateScopedClient(
            "usr-safety-01",
            ["safety_reviewer"],
            studyIds: ["STUDY-CMT"]);
        using var crcClient = CreateScopedClient(
            "usr-crc-01",
            ["CRC"],
            studyIds: ["STUDY-CMT"],
            siteIds: ["SITE-101"]);

        var created = await CreateTestTicketAsync(crcClient, "STUDY-CMT", "SITE-101");

        // 1. Sponsor safety reviewer records both a PUBLIC note and an INTERNAL_SPONSOR note
        using (var pubReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{created.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto(
                Body: "Please confirm laboratory centrifuge calibration date for SUBJ-8812.",
                Visibility: "PUBLIC")),
        })
        {
            pubReq.Headers.Add(GatewayHeaders.ChangeReason, "Issued public query to site");
            (await safetyClient.SendAsync(pubReq, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using (var intReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{created.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto(
                Body: "Internal medical monitor note: cross-checking DSMB charter threshold.",
                Visibility: "INTERNAL_SPONSOR")),
        })
        {
            intReq.Headers.Add(GatewayHeaders.ChangeReason, "Logged sponsor medical review note");
            (await safetyClient.SendAsync(intReq, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // 2. Safety reviewer sees both comments; site CRC sees ONLY the PUBLIC comment
        var sponsorComments = await safetyClient.GetFromJsonAsync<List<TicketCommentResponseDto>>(
            $"/api/v1/tickets/{created.Id}/comments",
            Ct);
        sponsorComments.Should().HaveCount(2);

        var crcComments = await crcClient.GetFromJsonAsync<List<TicketCommentResponseDto>>(
            $"/api/v1/tickets/{created.Id}/comments",
            Ct);
        crcComments.Should().ContainSingle().Which.Visibility.Should().Be("PUBLIC");

        // 3. Site CRC cannot post INTERNAL_SPONSOR notes (403 Forbidden)
        using (var crcInternalReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{created.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto(
                Body: "Attempted internal sponsor note by site user.",
                Visibility: "INTERNAL_SPONSOR")),
        })
        {
            crcInternalReq.Headers.Add(GatewayHeaders.ChangeReason, "Site internal probe");
            (await crcClient.SendAsync(crcInternalReq, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 4. Direct participant PII in comment body is rejected with 400 Bad Request
        using (var piiCommentReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{created.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto(
                Body: "Participant email is jane.patient@clinic-mail.org", // deid-ignore: synthetic PII rejection test fixture
                Visibility: "PUBLIC")),
        })
        {
            piiCommentReq.Headers.Add(GatewayHeaders.ChangeReason, "PII comment probe");
            (await crcClient.SendAsync(piiCommentReq, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // 5. Site CRC audit-trail view excludes the INTERNAL_SPONSOR comment audit entry while sponsor sees all 3 entries
        var sponsorAudit = await safetyClient.GetFromJsonAsync<List<TicketAuditEntryDto>>(
            $"/api/v1/tickets/{created.Id}/audit-trail",
            Ct);
        sponsorAudit.Should().HaveCount(3);

        var crcAudit = await crcClient.GetFromJsonAsync<List<TicketAuditEntryDto>>(
            $"/api/v1/tickets/{created.Id}/audit-trail",
            Ct);
        crcAudit.Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-TCK-001")]
    [GxPRequirement("PRD-TCK-002")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task TrialCoordinator_EnforcesVerifiedTrialTenantWrites_SiteBoundReads_AndSponsorCommentAllowlist()
    {
        await EnsureDbCreatedAsync();

        // Seed enterprise tickets across SITE-101 and SITE-102 with both PUBLIC and INTERNAL_SPONSOR notes
        using var enterpriseSponsorClient = CreateClient(
            userId: "usr-ent-sponsor",
            tenantId: "tenant-enterprise-01",
            roles: ["Sponsor Admin"],
            studyIds: ["STUDY-ONC-01"],
            siteIds: []);

        var createdTicket = await CreateTestTicketAsync(enterpriseSponsorClient, "STUDY-ONC-01", "SITE-101");
        var otherSiteTicket = await CreateTestTicketAsync(enterpriseSponsorClient, "STUDY-ONC-01", "SITE-102");

        using (var pubEnterpriseReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{createdTicket.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto("Enterprise public triage note", "PUBLIC")),
        })
        {
            pubEnterpriseReq.Headers.Add(GatewayHeaders.ChangeReason, "Enterprise public note");
            (await enterpriseSponsorClient.SendAsync(pubEnterpriseReq, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using (var intEnterpriseReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{createdTicket.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto("Enterprise sponsor confidential note", "INTERNAL_SPONSOR")),
        })
        {
            intEnterpriseReq.Headers.Add(GatewayHeaders.ChangeReason, "Enterprise internal note");
            (await enterpriseSponsorClient.SendAsync(intEnterpriseReq, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // 1. Enterprise trial_coordinator without site_id claims receives 403 across reads, creates, updates, comments, transitions, and signatures
        string enterpriseSignPath = $"/api/v1/tickets/{createdTicket.Id}/sign";
        string enterpriseSigToken = TestSecurityContext.CreateSigToken(
            userId: "usr-trial-coord",
            action: enterpriseSignPath,
            semanticAction: TicketEndpoints.SignTicketSemanticAction,
            roles: ["trial_coordinator"],
            issuedAt: _clock.UtcNow);

        using var trialCoordEnterpriseClient = CreateClient(
            userId: "usr-trial-coord",
            tenantId: "tenant-enterprise-01",
            roles: ["trial_coordinator"],
            studyIds: ["STUDY-ONC-01"],
            siteIds: [],
            sigToken: enterpriseSigToken);

        (await trialCoordEnterpriseClient.GetAsync("/api/v1/tickets", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await trialCoordEnterpriseClient.GetAsync($"/api/v1/tickets/{createdTicket.Id}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await trialCoordEnterpriseClient.GetAsync($"/api/v1/tickets/{createdTicket.Id}/comments", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await trialCoordEnterpriseClient.GetAsync($"/api/v1/tickets/{createdTicket.Id}/audit-trail", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using (var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto(
                Title: "Unauthorized enterprise coordinator create",
                Description: "Attempted ticket creation outside trial-* tenant.",
                StudyScope: "STUDY-ONC-01",
                SiteScope: "SITE-101")),
        })
        {
            createReq.Headers.Add(GatewayHeaders.ChangeReason, "Enterprise create probe");
            (await trialCoordEnterpriseClient.SendAsync(createReq, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{createdTicket.Id}")
        {
            Content = JsonContent.Create(new UpdateTicketRequestDto(Title: "Unauthorized patch")),
        })
        {
            patchReq.Headers.Add(GatewayHeaders.ChangeReason, "Enterprise patch probe");
            (await trialCoordEnterpriseClient.SendAsync(patchReq, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var coordPostReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{createdTicket.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto("Internal probe", "INTERNAL_SPONSOR")),
        };
        coordPostReq.Headers.Add(GatewayHeaders.ChangeReason, "Coordinator internal post check");
        (await trialCoordEnterpriseClient.SendAsync(coordPostReq, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using (var transitionReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{createdTicket.Id}/transitions")
        {
            Content = JsonContent.Create(new TransitionTicketRequestDto(TargetStatus: "IN_PROGRESS")),
        })
        {
            transitionReq.Headers.Add(GatewayHeaders.ChangeReason, "Enterprise transition probe");
            (await trialCoordEnterpriseClient.SendAsync(transitionReq, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var signReq = new HttpRequestMessage(HttpMethod.Post, enterpriseSignPath)
        {
            Content = JsonContent.Create(new SignTicketRequestDto(
                SignatureMeaning: "Attempted closure sign-off in enterprise tenant",
                SignatureAction: "SIGN_CLOSURE")),
        })
        {
            signReq.Headers.Add(GatewayHeaders.ChangeReason, "Enterprise sign probe");
            (await trialCoordEnterpriseClient.SendAsync(signReq, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 2. Enterprise trial_coordinator with explicit site assignment can read assigned-site PUBLIC data only, and still cannot mutate
        using var siteScopedEnterpriseCoordClient = CreateClient(
            userId: "usr-trial-coord-site",
            tenantId: "tenant-enterprise-01",
            roles: ["trial_coordinator"],
            studyIds: ["STUDY-ONC-01"],
            siteIds: ["SITE-101"]);

        (await siteScopedEnterpriseCoordClient.GetAsync($"/api/v1/tickets/{createdTicket.Id}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await siteScopedEnterpriseCoordClient.GetAsync($"/api/v1/tickets/{otherSiteTicket.Id}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var siteScopedCoordComments = await siteScopedEnterpriseCoordClient
            .GetFromJsonAsync<List<TicketCommentResponseDto>>($"/api/v1/tickets/{createdTicket.Id}/comments", Ct);
        siteScopedCoordComments.Should().ContainSingle().Which.Visibility.Should().Be("PUBLIC");

        using (var publicMutateReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{createdTicket.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto("Public probe from non-trial coordinator", "PUBLIC")),
        })
        {
            publicMutateReq.Headers.Add(GatewayHeaders.ChangeReason, "Non-trial coordinator public comment probe");
            (await siteScopedEnterpriseCoordClient.SendAsync(publicMutateReq, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 3. Verified trial-<12hex> tenant: multi-role founder retains INTERNAL_SPONSOR visibility while standalone trial_coordinator sees PUBLIC only
        const string verifiedTrialTenant = "trial-a1b2c3d4e5f6";
        using var trialFounderClient = CreateClient(
            userId: "usr-trial-founder",
            tenantId: verifiedTrialTenant,
            roles: ["Sponsor Admin", "Trial Coordinator"],
            studyIds: ["STUDY-TRIAL-01"],
            siteIds: []);

        using var standaloneTrialCoordClient = CreateClient(
            userId: "usr-standalone-trial-coord",
            tenantId: verifiedTrialTenant,
            roles: ["trial_coordinator"],
            studyIds: ["STUDY-TRIAL-01"],
            siteIds: []);

        var trialTicket = await CreateTestTicketAsync(standaloneTrialCoordClient, "STUDY-TRIAL-01", "SITE-TRIAL-01");

        using (var trialPubReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{trialTicket.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto("Coordinator progress note for SUBJ-8812.", "PUBLIC")),
        })
        {
            trialPubReq.Headers.Add(GatewayHeaders.ChangeReason, "Logged public trial progress note");
            (await standaloneTrialCoordClient.SendAsync(trialPubReq, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using (var founderInternalReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{trialTicket.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto("Founder internal sponsor assessment.", "INTERNAL_SPONSOR")),
        })
        {
            founderInternalReq.Headers.Add(GatewayHeaders.ChangeReason, "Logged founder internal sponsor note");
            (await trialFounderClient.SendAsync(founderInternalReq, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var standaloneTrialComments = await standaloneTrialCoordClient
            .GetFromJsonAsync<List<TicketCommentResponseDto>>($"/api/v1/tickets/{trialTicket.Id}/comments", Ct);
        standaloneTrialComments.Should().NotBeEmpty();
        standaloneTrialComments.Should().OnlyContain(c => c.Visibility == "PUBLIC");

        using (var standaloneInternalPostReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{trialTicket.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto("Standalone internal attempt", "INTERNAL_SPONSOR")),
        })
        {
            standaloneInternalPostReq.Headers.Add(GatewayHeaders.ChangeReason, "Standalone internal comment attempt");
            (await standaloneTrialCoordClient.SendAsync(standaloneInternalPostReq, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var standaloneTrialAudit = await standaloneTrialCoordClient
            .GetFromJsonAsync<List<TicketAuditEntryDto>>($"/api/v1/tickets/{trialTicket.Id}/audit-trail", Ct);
        standaloneTrialAudit.Should().HaveCount(2);

        var founderComments = await trialFounderClient
            .GetFromJsonAsync<List<TicketCommentResponseDto>>($"/api/v1/tickets/{trialTicket.Id}/comments", Ct);
        founderComments.Should().HaveCount(2);
        founderComments.Should().Contain(c => c.Visibility == "INTERNAL_SPONSOR");

        var founderAudit = await trialFounderClient
            .GetFromJsonAsync<List<TicketAuditEntryDto>>($"/api/v1/tickets/{trialTicket.Id}/audit-trail", Ct);
        founderAudit.Should().HaveCount(3);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-TCK-002")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task Transitions_Sign_AndAuditTrail_PauseSla_EnforceSingleUsePart11Token_AndLockSignedTicket()
    {
        await EnsureDbCreatedAsync();
        using var dmClient = CreateScopedClient(
            "usr-dm-lead",
            ["Data Manager"],
            studyIds: ["STUDY-WF"]);

        var created = await CreateTestTicketAsync(dmClient, "STUDY-WF", "SITE-101");
        DateTimeOffset initialSlaTarget = created.SlaTargetAt!.Value;

        // 1. OPEN -> IN_PROGRESS -> WAITING_ON_SITE (pauses SLA clock)
        await TransitionAsync(dmClient, created.Id, "IN_PROGRESS", "Triaged deviation");
        var paused = await TransitionAsync(dmClient, created.Id, "WAITING_ON_SITE", "Awaiting site calibration log");
        paused.Status.Should().Be("WAITING_ON_SITE");

        // Advance IClock by 2 hours while paused, then resume IN_PROGRESS
        _clock.Advance(TimeSpan.FromHours(2));
        var resumed = await TransitionAsync(dmClient, created.Id, "IN_PROGRESS", "Site supplied calibration certificate");
        resumed.SlaTotalPausedSeconds.Should().Be(7200);
        resumed.SlaTargetAt.Should().Be(initialSlaTarget.AddHours(2));

        // 2. Resolving without root_cause_summary or with PII in root_cause_summary fails 400
        using (var missingRcaReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{created.Id}/transitions")
        {
            Content = JsonContent.Create(new TransitionTicketRequestDto(
                TargetStatus: "RESOLVED")),
        })
        {
            missingRcaReq.Headers.Add(GatewayHeaders.ChangeReason, "Missing RCA check");
            (await dmClient.SendAsync(missingRcaReq, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var piiRcaReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{created.Id}/transitions")
        {
            Content = JsonContent.Create(new TransitionTicketRequestDto(
                TargetStatus: "RESOLVED",
                RootCauseCategory: "DATA_ENTRY_ERROR",
                RootCauseSummary: "Confirmed SSN 123-45-6789 mismatch", // deid-ignore: synthetic PII rejection test fixture
                ResolutionCode: "CORRECTED")),
        })
        {
            piiRcaReq.Headers.Add(GatewayHeaders.ChangeReason, "PII RCA check");
            (await dmClient.SendAsync(piiRcaReq, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // 3. Valid RESOLVED transition records RCA fields
        using (var resolveReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{created.Id}/transitions")
        {
            Content = JsonContent.Create(new TransitionTicketRequestDto(
                TargetStatus: "RESOLVED",
                RootCauseCategory: "PROCESS_WORKFLOW_DEFICIT",
                RootCauseSummary: "Updated site binder checklist and completed coordinator retraining.",
                ResolutionCode: "CORRECTIVE_ACTION_IMPLEMENTED")),
        })
        {
            resolveReq.Headers.Add(GatewayHeaders.ChangeReason, "Resolved following CAPA verification");
            var resolveResp = await dmClient.SendAsync(resolveReq, Ct);
            resolveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 4. Sign resolved ticket with a valid step-up X-Sig-Token
        string signPath = $"/api/v1/tickets/{created.Id}/sign";
        string sigToken = TestSecurityContext.CreateSigToken(
            userId: "usr-dm-lead",
            action: signPath,
            semanticAction: "tickets.ticket.sign",
            roles: ["Data Manager"],
            issuedAt: _clock.UtcNow);

        TicketResponseDto signed;
        using (var signReq = new HttpRequestMessage(HttpMethod.Post, signPath)
        {
            Content = JsonContent.Create(new SignTicketRequestDto(
                SignatureMeaning: "I approve the clinical assessment and corrective medical action.",
                SignatureAction: "SIGN_CLOSURE")),
        })
        {
            signReq.Headers.Add(GatewayHeaders.SigToken, sigToken);
            signReq.Headers.Add(GatewayHeaders.ChangeReason, "Part 11 closure authorization");
            var signResp = await dmClient.SendAsync(signReq, Ct);
            signResp.StatusCode.Should().Be(HttpStatusCode.OK);
            signed = (await signResp.Content.ReadFromJsonAsync<TicketResponseDto>(cancellationToken: Ct))!;
        }

        signed.SignatureUser.Should().Be("usr-dm-lead");
        signed.SignatureMeaning.Should().Be("I approve the clinical assessment and corrective medical action.");
        signed.SignatureToken.Should().NotBeNullOrWhiteSpace();
        signed.SignatureTimestamp.Should().Be(_clock.UtcNow);

        // 5. Replaying the same X-Sig-Token is refused with 401 Unauthorized
        using (var replayReq = new HttpRequestMessage(HttpMethod.Post, signPath)
        {
            Content = JsonContent.Create(new SignTicketRequestDto(
                SignatureMeaning: "Replay attempt")),
        })
        {
            replayReq.Headers.Add(GatewayHeaders.SigToken, sigToken);
            replayReq.Headers.Add(GatewayHeaders.ChangeReason, "Replay probe");
            (await dmClient.SendAsync(replayReq, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // 6. Signed ticket rejects further comments or status transitions with 409 Conflict
        using (var postSignComment = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{created.Id}/comments")
        {
            Content = JsonContent.Create(new CreateTicketCommentRequestDto("Post-signature edit", "PUBLIC")),
        })
        {
            postSignComment.Headers.Add(GatewayHeaders.ChangeReason, "Post-sign comment probe");
            (await dmClient.SendAsync(postSignComment, Ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        using (var postSignTransition = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{created.Id}/transitions")
        {
            Content = JsonContent.Create(new TransitionTicketRequestDto(TargetStatus: "CLOSED")),
        })
        {
            postSignTransition.Headers.Add(GatewayHeaders.ChangeReason, "Post-sign transition probe");
            (await dmClient.SendAsync(postSignTransition, Ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        // 7. Audit trail returns tamper-evident Merkle chain covering creation, transitions, and Part 11 signature
        var auditTrail = await dmClient.GetFromJsonAsync<List<TicketAuditEntryDto>>(
            $"/api/v1/tickets/{created.Id}/audit-trail",
            Ct);
        auditTrail.Should().NotBeNull();
        auditTrail!.Select(a => a.Action).Should().Contain(["TICKET_CREATED", "TICKET_TRANSITIONED", "TICKET_SIGNED"]);
        auditTrail.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.BlockHash));
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-TCK-001")]
    [GxPRequirement("PRD-TCK-002")]
    public async Task CreateTicket_ContextualEscalationWithoutCrfForm_PreservesOverviewRoutingAndEcrfClinicalScope()
    {
        await EnsureDbCreatedAsync();
        using var adminClient = CreateScopedClient(
            "usr-admin-01",
            ["Sponsor Admin"],
            studyIds: ["STUDY-A"],
            siteIds: ["SITE-101"]);

        // 1. Escalating from /overview with SYSTEM_SUPPORT and empty EntityType routes to PLATFORM_ADMIN_ACCESS
        using var overviewReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto(
                Title: "[Help Escalation] Operational Assistance on /overview",
                Description: "Requested assistance from Contextual Help on /overview.",
                Category: "SYSTEM_SUPPORT",
                GxpSeverity: "MINOR",
                Priority: "MEDIUM",
                EntityType: string.Empty,
                EntityId: null,
                SourceRoute: "/overview",
                Persona: "super_admin")),
        };
        overviewReq.Headers.Add(GatewayHeaders.ChangeReason, "Escalated to Support Ticket from Contextual Help on /overview");

        var overviewResp = await adminClient.SendAsync(overviewReq, Ct);
        overviewResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var overviewTicket = await overviewResp.Content.ReadFromJsonAsync<TicketResponseDto>(cancellationToken: Ct);
        overviewTicket.Should().NotBeNull();
        overviewTicket!.AssignedQueue.Should().Be("PLATFORM_ADMIN_ACCESS");
        overviewTicket.RelatedEntityType.Should().BeNull();
        overviewTicket.RelatedEntityId.Should().BeNull();

        // 2. Escalating from /ecrf?studyId=STUDY-A&siteId=SITE-101&subjectId=SUBJ-1001 persists clinical scope and can be listed/fetched inline
        using var ecrfReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto(
                Title: "[Help Escalation] Operational Assistance on /ecrf?studyId=STUDY-A&siteId=SITE-101&subjectId=SUBJ-1001",
                Description: "Subject SUBJ-1001 visit form assistance.",
                Category: "PROTOCOL_DEVIATION",
                GxpSeverity: "MINOR",
                Priority: "MEDIUM",
                StudyScope: "STUDY-A",
                SiteScope: "SITE-101",
                SubjectCode: "SUBJ-1001",
                EntityType: string.Empty,
                EntityId: null,
                SourceRoute: "/ecrf?studyId=STUDY-A&siteId=SITE-101&subjectId=SUBJ-1001",
                Persona: "site_crc")),
        };
        ecrfReq.Headers.Add(GatewayHeaders.ChangeReason, "Escalated to Support Ticket from Contextual Help on /ecrf");

        var ecrfResp = await adminClient.SendAsync(ecrfReq, Ct);
        ecrfResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var ecrfTicket = await ecrfResp.Content.ReadFromJsonAsync<TicketResponseDto>(cancellationToken: Ct);
        ecrfTicket.Should().NotBeNull();
        ecrfTicket!.AssignedQueue.Should().Be("DATA_CAPTURE_ECRF");
        ecrfTicket.StudyScope.Should().Be("STUDY-A");
        ecrfTicket.SiteScope.Should().Be("SITE-101");
        ecrfTicket.SubjectCode.Should().Be("SUBJ-1001");

        var scopedList = await adminClient.GetFromJsonAsync<List<TicketResponseDto>>(
            "/api/v1/tickets?study_id=STUDY-A&site_id=SITE-101",
            Ct);
        scopedList.Should().ContainSingle(t => t.Id == ecrfTicket.Id);

        var fetchedDetail = await adminClient.GetFromJsonAsync<TicketResponseDto>(
            $"/api/v1/tickets/{ecrfTicket.Id}",
            Ct);
        fetchedDetail!.Reference.Should().Be(ecrfTicket.Reference);
        fetchedDetail.SubjectCode.Should().Be("SUBJ-1001");
    }

    private static async Task<TicketResponseDto> CreateTestTicketAsync(HttpClient client, string studyCode, string siteCode)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto(
                Title: "Cold-chain excursion follow-up",
                Description: "Investigating logger excursion for SUBJ-8812.",
                Category: "PROTOCOL_DEVIATION",
                GxpSeverity: "MAJOR",
                Priority: "HIGH",
                StudyScope: studyCode,
                SiteScope: siteCode,
                SubjectCode: "SUBJ-8812",
                SourceRoute: "/ecrf",
                Persona: "site_crc")),
        };
        req.Headers.Add(GatewayHeaders.ChangeReason, "Initial ticket creation");
        var resp = await client.SendAsync(req, Ct);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<TicketResponseDto>(cancellationToken: Ct))!;
    }

    private static async Task<TicketResponseDto> TransitionAsync(HttpClient client, string ticketId, string toStatus, string reason)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{ticketId}/transitions")
        {
            Content = JsonContent.Create(new TransitionTicketRequestDto(TargetStatus: toStatus)),
        };
        req.Headers.Add(GatewayHeaders.ChangeReason, reason);
        var resp = await client.SendAsync(req, Ct);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<TicketResponseDto>(cancellationToken: Ct))!;
    }
}
