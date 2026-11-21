using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Apps.Tickets.Endpoints;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.IntegrationTests.Database;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Tickets;

public sealed class TicketsAndKnowledgePostgresFixture : IAsyncLifetime
{
    private readonly ThrowawayPostgresDatabase _database = new();

    public string? ConnectionString => _database.ConnectionString;

    public void SkipIfUnavailable() => _database.SkipIfUnavailable();

    public ValueTask InitializeAsync() => _database.InitializeAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}

/// <summary>
/// Boots real <c>Cadence.Apps.Tickets.Program</c> and <c>Cadence.Apps.Knowledge.Program</c> against a real PostgreSQL
/// instance (with schema migrations and <see cref="GxPAuditInterceptor"/>), verifying persisted ticket reloads,
/// deterministic queue assignment, cryptographic Merkle audit chains, and contextual SOP resolution.
/// </summary>
public sealed class TicketPersistenceAndAuditPostgresTests(TicketsAndKnowledgePostgresFixture fixture)
    : IClassFixture<TicketsAndKnowledgePostgresFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-TCK-001")]
    [GxPRequirement("PRD-TCK-002")]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-SYS-003")]
    public async Task TicketsPersistedToPostgres_SurviveFreshDbContextReloadWithValidMerkleAuditChain()
    {
        fixture.SkipIfUnavailable();
        string connectionString = fixture.ConnectionString!;
        string studyCode = $"STUDY-PG-{Guid.NewGuid():N}"[..18];

        await using var factory = new WebApplicationFactory<Cadence.Apps.Tickets.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret();
            builder.UseSetting("ConnectionStrings:cadence", connectionString);
            builder.UseSetting("Database:MigrateOnStartup", "true");
        });

        var identity = new GatewayIdentity(
            UserId: "usr-pg-crc-01",
            Roles: ["CRC", "Data Manager"],
            SiteIds: ["SITE-101"],
            TenantId: TestSecurityContext.TenantCode,
            StudyIds: [studyCode]);
        using var client = factory.CreateDefaultClient(new TestSecurityContext.GatewaySigningHandler(identity));

        // Create two sequential tickets to verify predecessor-hash chaining in PostgreSQL
        TicketResponseDto firstTicket;
        TicketResponseDto secondTicket;

        using (var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto(
                Title: "First Postgres ticket on /ecrf",
                Description: "Investigating locked systolic field for SUBJ-8812.",
                Category: "PROTOCOL_DEVIATION",
                GxpSeverity: "MAJOR",
                Priority: "HIGH",
                StudyScope: studyCode,
                SiteScope: "SITE-101",
                SubjectCode: "SUBJ-8812",
                SourceRoute: "/ecrf",
                Persona: "site_crc")),
        })
        {
            req1.Headers.Add("X-Change-Reason", "Initial escalation from eCRF");
            var resp1 = await client.SendAsync(req1, Ct);
            resp1.StatusCode.Should().Be(HttpStatusCode.Created);
            firstTicket = (await resp1.Content.ReadFromJsonAsync<TicketResponseDto>(cancellationToken: Ct))!;
        }

        using (var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tickets")
        {
            Content = JsonContent.Create(new CreateTicketRequestDto(
                Title: "Second Postgres ticket on /ssu",
                Description: "Site startup greenlight checklist review.",
                Category: "SITE_OPERATIONS",
                GxpSeverity: "MINOR",
                Priority: "MEDIUM",
                StudyScope: studyCode,
                SiteScope: "SITE-101",
                SourceRoute: "/ssu",
                Persona: "cra_monitor")),
        })
        {
            req2.Headers.Add("X-Change-Reason", "Escalated site readiness check");
            var resp2 = await client.SendAsync(req2, Ct);
            resp2.StatusCode.Should().Be(HttpStatusCode.Created);
            secondTicket = (await resp2.Content.ReadFromJsonAsync<TicketResponseDto>(cancellationToken: Ct))!;
        }

        firstTicket.AssignedQueue.Should().Be("DATA_CAPTURE_ECRF");
        secondTicket.AssignedQueue.Should().Be("SUBJECT_MANAGEMENT_RTSM");

        // Reload via GET /api/v1/tickets
        var listed = await client.GetFromJsonAsync<List<TicketResponseDto>>($"/api/v1/tickets?study_id={studyCode}", Ct);
        listed.Should().NotBeNull();
        listed!.Select(t => t.Reference).Should().Contain([firstTicket.Reference, secondTicket.Reference]);

        // Reload via an independent fresh Npgsql DbContext in the tickets schema (bound to the tenant RLS session) and verify Merkle audit chain
        var tenantAccessor = factory.Services.GetRequiredService<ICurrentTenantAccessor>();
        var tenantScope = factory.Services.GetRequiredService<ITenantScope>();
        using (tenantScope.Bind(TestSecurityContext.TenantCode))
        {
            var optionsBuilder = new DbContextOptionsBuilder<TicketsDbContext>();
            optionsBuilder.UseNpgsql($"{connectionString};Search Path=tickets", npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "tickets"));
            optionsBuilder.UseCadenceStandaloneTenancy(tenantAccessor, isRelational: true);
            optionsBuilder.AddInterceptors(StandaloneTenantConnectionInterceptor.Instance);
            await using var freshDb = new TicketsDbContext(optionsBuilder.Options);

            var persistedRows = await freshDb.Tickets.AsNoTracking()
                .Where(t => t.StudyId == studyCode)
                .ToListAsync(Ct);
            persistedRows.Should().HaveCount(2);

            var auditEntries = await freshDb.AuditLogEntries.AsNoTracking()
                .OrderBy(e => e.Timestamp)
                .ToListAsync(Ct);
            auditEntries.Should().HaveCountGreaterThanOrEqualTo(2);
            VerifyMerkleChain(auditEntries);
            (await freshDb.TicketAuditLogs.CountAsync(Ct)).Should().Be(0);
        }
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-KNB-001")]
    [GxPRequirement("PRD-KNB-002")]
    public async Task KnowledgeContextualHelp_SeedsAndResolvesPublishedSopsInPostgres()
    {
        fixture.SkipIfUnavailable();
        string connectionString = fixture.ConnectionString!;

        await using var factory = new WebApplicationFactory<Cadence.Apps.Knowledge.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret();
            builder.UseSetting("ConnectionStrings:cadence", connectionString);
            builder.UseSetting("Database:MigrateOnStartup", "true");
        });

        var identity = TestSecurityContext.Identity("usr-pg-cra-01", "CRA");
        using var client = factory.CreateDefaultClient(new TestSecurityContext.GatewaySigningHandler(identity));

        var ssuResp = await client.GetFromJsonAsync<ContextualHelpResponseDto>(
            "/api/v1/knowledge/contextual-help?route=/ssu&persona=cra_monitor",
            Ct);
        ssuResp.Should().NotBeNull();
        ssuResp!.PrimaryArticle.Should().NotBeNull();
        ssuResp.PrimaryArticle!.Status.Should().Be("PUBLISHED");

        var studyParamResp = await client.GetFromJsonAsync<ContextualHelpResponseDto>(
            "/api/v1/knowledge/contextual-help?route=/studies/DEMO-01&persona=site_crc",
            Ct);
        studyParamResp.Should().NotBeNull();
        studyParamResp!.MatchedMapping!.RoutePattern.Should().Be("/studies/:studyCode");

        // Also verify a second tenant ("tenant-b") seeds and resolves its own isolated SOP catalog without unique-index collisions
        var tenantBIdentity = TestSecurityContext.Identity("usr-pg-cra-tenant-b", "CRA") with { TenantId = "tenant-b" };
        using var tenantBClient = factory.CreateDefaultClient(new TestSecurityContext.GatewaySigningHandler(tenantBIdentity));
        var tenantBSsuResp = await tenantBClient.GetFromJsonAsync<ContextualHelpResponseDto>(
            "/api/v1/knowledge/contextual-help?route=/ssu&persona=cra_monitor",
            Ct);
        tenantBSsuResp.Should().NotBeNull();
        tenantBSsuResp!.PrimaryArticle.Should().NotBeNull();
        tenantBSsuResp.PrimaryArticle!.Status.Should().Be("PUBLISHED");

        var tenantAccessor = factory.Services.GetRequiredService<ICurrentTenantAccessor>();
        var tenantScope = factory.Services.GetRequiredService<ITenantScope>();
        using (tenantScope.Bind(TestSecurityContext.TenantCode))
        {
            var optionsBuilder = new DbContextOptionsBuilder<KnowledgeDbContext>();
            optionsBuilder.UseNpgsql($"{connectionString};Search Path=knowledge", npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "knowledge"));
            optionsBuilder.UseCadenceStandaloneTenancy(tenantAccessor, isRelational: true);
            optionsBuilder.AddInterceptors(StandaloneTenantConnectionInterceptor.Instance);
            await using var freshDb = new KnowledgeDbContext(optionsBuilder.Options);
            var auditEntries = await freshDb.AuditLogEntries.AsNoTracking().ToListAsync(Ct);
            auditEntries.Should().NotBeEmpty();
            VerifyMerkleChain(auditEntries);
        }
    }

    private static void VerifyMerkleChain(IReadOnlyList<AuditLogEntry> entries)
    {
        foreach (var group in entries.GroupBy(e => e.EntityName))
        {
            var hashes = new HashSet<string>(group.Select(e => e.BlockHash), StringComparer.Ordinal);
            group.Count(e => string.IsNullOrEmpty(e.PredecessorHash)).Should().Be(1);
            foreach (var entry in group)
            {
                string expected = AuditLogEntry.ComputeBlockHash(
                    entry.EntityName,
                    entry.EntityId,
                    entry.Action,
                    AuditLogEntry.NormalizeTimestamp(entry.Timestamp),
                    entry.UserId,
                    entry.VersionIndex,
                    entry.ChangesJson,
                    entry.PredecessorHash);
                entry.BlockHash.Should().Be(expected);
                if (!string.IsNullOrEmpty(entry.PredecessorHash))
                {
                    hashes.Should().Contain(entry.PredecessorHash);
                }
            }
        }
    }
}
