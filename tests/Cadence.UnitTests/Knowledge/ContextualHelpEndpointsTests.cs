using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Application;
using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
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

namespace Cadence.UnitTests.Knowledge;

/// <summary>
/// Verifies Hierarchical Specificity Scoring, published SOP resolution across all 6 staff personas and core routes
/// (/studies, /studies/:studyCode, /ecrf, /ctms, /ssu), article keyword search, category visibility, and RBAC gates.
/// </summary>
public sealed class ContextualHelpEndpointsTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Cadence.Apps.Knowledge.Program> _factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ContextualHelpEndpointsTests()
    {
        _connection.Open();
        _factory = new WebApplicationFactory<Cadence.Apps.Knowledge.Program>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret().UseTestPostgresConnectionString();
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
                services.RemoveAll<DbContextOptions<KnowledgeDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KnowledgeDbContext>>();
                services.AddDbContext<KnowledgeDbContext>((sp, options) =>
                {
                    options.UseSqlite(_connection);
                    options.AddInterceptors(new GxPAuditInterceptor(
                        () => KnowledgeAuditActorScope.CurrentUserId
                              ?? sp.GetService<IHttpContextAccessor>()?.HttpContext?.User.FindFirst(CadenceClaimTypes.Subject)?.Value
                              ?? "svc:knowledge-sop-catalog",
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
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        await db.Database.EnsureCreatedAsync(Ct);
    }

    private HttpClient CreateStaffClient(string userId = "usr-crc-01", string role = "CRC")
    {
        var identity = TestSecurityContext.Identity(userId, role);
        return _factory.CreateDefaultClient(new TestSecurityContext.GatewaySigningHandler(identity));
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-KNB-002")]
    public void Scorer_EnforcesHierarchicalSpecificityOrder_ExactOverParamOverPrefixOverGlobal()
    {
        var baseTime = _clock.UtcNow;
        var mappings = new[]
        {
            BuildMapping("m-global", "*", null, priority: 99, baseTime),
            BuildMapping("m-prefix-short", "/studies/*", "site_crc", priority: 50, baseTime),
            BuildMapping("m-param", "/studies/:studyCode", "site_crc", priority: 10, baseTime),
            BuildMapping("m-exact", "/studies/DEMO-01", null, priority: 1, baseTime),
        };

        var articlesById = mappings.ToDictionary(
            m => m.ArticleId,
            m => BuildPublishedArticle(m.ArticleId, baseTime));
        var versionsById = new Dictionary<string, KnowledgeArticleVersion>();

        // Tier 1: Exact route match beats parameterized + exact persona even with lower priority weight
        var exactWinner = ContextualHelpScorer.Resolve("/studies/DEMO-01", "site_crc", mappings, articlesById, versionsById);
        exactWinner.MatchedMapping!.Id.Should().Be("m-exact");
        exactWinner.RelatedArticles.Select(a => a.Id).Should().Equal(
            "art-m-param",
            "art-m-prefix-short",
            "art-m-global");

        // Tier 2: Parameterized segment match beats prefix wildcard for arbitrary study code
        var paramWinner = ContextualHelpScorer.Resolve("/studies/CARDIO-204", "site_crc", mappings, articlesById, versionsById);
        paramWinner.MatchedMapping!.Id.Should().Be("m-param");
        paramWinner.RelatedArticles.Select(a => a.Id).Should().StartWith(["art-m-prefix-short", "art-m-global"]);

        // Tier 3: Prefix wildcard matches nested path where 2-segment parameterized pattern does not
        var nestedWinner = ContextualHelpScorer.Resolve("/studies/CARDIO-204/cohorts", "site_crc", mappings, articlesById, versionsById);
        nestedWinner.MatchedMapping!.Id.Should().Be("m-prefix-short");
        nestedWinner.RelatedArticles.Select(a => a.Id).Should().StartWith("art-m-global");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-KNB-002")]
    public void Scorer_BreaksEqualRouteTiesByExactPersonaThenPriorityThenRecency()
    {
        var t0 = _clock.UtcNow;
        var mappings = new[]
        {
            BuildMapping("m-universal-high-prio", "/ecrf", null, priority: 100, t0.AddMinutes(10)),
            BuildMapping("m-persona-older-low-prio", "/ecrf", "site_crc", priority: 10, t0),
            BuildMapping("m-persona-older-high-prio", "/ecrf", "site_crc", priority: 20, t0),
            BuildMapping("m-persona-newer-high-prio", "/ecrf", "site_crc", priority: 20, t0.AddMinutes(5)),
        };
        var articlesById = mappings.ToDictionary(
            m => m.ArticleId,
            m => BuildPublishedArticle(m.ArticleId, t0));
        var versionsById = new Dictionary<string, KnowledgeArticleVersion>();

        var resolution = ContextualHelpScorer.Resolve("/ecrf", "site_crc", mappings, articlesById, versionsById);
        resolution.MatchedMapping!.Id.Should().Be("m-persona-newer-high-prio");
        resolution.RelatedArticles.Select(a => a.Id).Should().Equal(
            "art-m-persona-older-high-prio",
            "art-m-persona-older-low-prio",
            "art-m-universal-high-prio");
    }

    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-KNB-001")]
    [GxPRequirement("PRD-KNB-002")]
    [InlineData("/studies", "super_admin")]
    [InlineData("/studies", "sponsor_designer")]
    [InlineData("/studies", "site_crc")]
    [InlineData("/studies", "cra_monitor")]
    [InlineData("/studies", "data_manager")]
    [InlineData("/studies", "auditor")]
    [InlineData("/studies/DEMO-01", "super_admin")]
    [InlineData("/studies/DEMO-01", "sponsor_designer")]
    [InlineData("/studies/DEMO-01", "site_crc")]
    [InlineData("/studies/DEMO-01", "cra_monitor")]
    [InlineData("/studies/DEMO-01", "data_manager")]
    [InlineData("/studies/DEMO-01", "auditor")]
    [InlineData("/ecrf", "super_admin")]
    [InlineData("/ecrf", "sponsor_designer")]
    [InlineData("/ecrf", "site_crc")]
    [InlineData("/ecrf", "cra_monitor")]
    [InlineData("/ecrf", "data_manager")]
    [InlineData("/ecrf", "auditor")]
    [InlineData("/ctms", "super_admin")]
    [InlineData("/ctms", "sponsor_designer")]
    [InlineData("/ctms", "site_crc")]
    [InlineData("/ctms", "cra_monitor")]
    [InlineData("/ctms", "data_manager")]
    [InlineData("/ctms", "auditor")]
    [InlineData("/ssu", "super_admin")]
    [InlineData("/ssu", "sponsor_designer")]
    [InlineData("/ssu", "site_crc")]
    [InlineData("/ssu", "cra_monitor")]
    [InlineData("/ssu", "data_manager")]
    [InlineData("/ssu", "auditor")]
    [InlineData("/ssu", "safety_officer")]
    [InlineData("/ssu", "site_investigator")]
    [InlineData("/overview", "super_admin")]
    [InlineData("/overview", "safety_officer")]
    [InlineData("/overview", "site_investigator")]
    [InlineData("/plans", "super_admin")]
    [InlineData("/plans", "sponsor_designer")]
    [InlineData("/onboarding/trial", "super_admin")]
    [InlineData("/onboarding/trial", "site_crc")]
    [InlineData("/etmf", "cra_monitor")]
    [InlineData("/etmf", "auditor")]
    [InlineData("/training", "site_crc")]
    [InlineData("/training", "site_investigator")]
    [InlineData("/observations/OBS-2026-09", "safety_officer")]
    [InlineData("/observations/OBS-2026-09", "site_investigator")]
    [InlineData("/observations/OBS-2026-09", "data_manager")]
    [InlineData("/tickets", "safety_officer")]
    [InlineData("/tickets", "site_crc")]
    [InlineData("/knowledge", "auditor")]
    [InlineData("/knowledge", "safety_officer")]
    public async Task ResolveContextualHelp_ReturnsPublishedSopAndLockedVersionAcrossAllRoutesAndPersonas(
        string route,
        string persona)
    {
        await EnsureDbCreatedAsync();
        using var client = CreateStaffClient("usr-staff-01", "Sponsor Admin");

        var response = await client.GetAsync(
            $"/api/v1/knowledge/contextual-help?route={Uri.EscapeDataString(route)}&persona={Uri.EscapeDataString(persona)}",
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ContextualHelpResponseDto>(cancellationToken: Ct);
        payload.Should().NotBeNull();
        payload!.PrimaryArticle.Should().NotBeNull($"route {route} and persona {persona} must resolve a published SOP");
        payload.PrimaryArticle!.Status.Should().Be("PUBLISHED");
        payload.PrimaryArticle.BodyMarkdown.Should().NotBeNullOrWhiteSpace();
        payload.PrimaryVersion.Should().NotBeNull();
        payload.PrimaryVersion!.IsLocked.Should().BeTrue();
        payload.MatchedMapping.Should().NotBeNull();
        payload.MatchedMapping!.Persona.Should().Be(persona);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-KNB-001")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SearchArticles_GetArticle_AndCategories_FilterPublishedSopsAndRecordAuditChain()
    {
        await EnsureDbCreatedAsync();
        using var client = CreateStaffClient("usr-dm-01", "Data Manager");

        // Search published articles by keyword
        var searchResp = await client.GetAsync("/api/v1/knowledge/articles?status_filter=PUBLISHED&q=greenlight", Ct);
        searchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var articles = await searchResp.Content.ReadFromJsonAsync<List<KnowledgeArticleDto>>(cancellationToken: Ct);
        articles.Should().NotBeNull().And.NotBeEmpty();
        articles!.Should().OnlyContain(a => a.Status == "PUBLISHED");

        // Fetch individual article by ID and verify 404 on unknown ID
        string articleId = articles[0].Id;
        var detailResp = await client.GetAsync($"/api/v1/knowledge/articles/{articleId}", Ct);
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResp.Content.ReadFromJsonAsync<KnowledgeArticleDto>(cancellationToken: Ct);
        detail!.Id.Should().Be(articleId);

        var missingResp = await client.GetAsync("/api/v1/knowledge/articles/art-does-not-exist", Ct);
        missingResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Categories filtered by persona visibility (including safety_officer and site_investigator)
        var categoriesResp = await client.GetAsync("/api/v1/knowledge/categories?persona=auditor", Ct);
        categoriesResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var categories = await categoriesResp.Content.ReadFromJsonAsync<List<KnowledgeCategoryDto>>(cancellationToken: Ct);
        categories.Should().NotBeNull().And.Contain(c => c.Slug == "regulatory-compliance");

        var safetyCategories = await client.GetFromJsonAsync<List<KnowledgeCategoryDto>>(
            "/api/v1/knowledge/categories?persona=safety_officer",
            Ct);
        safetyCategories.Should().NotBeNull().And.NotBeEmpty();
        safetyCategories!.Select(c => c.Slug).Should().Contain(["clinical-operations", "regulatory-compliance"]);

        var investigatorCategories = await client.GetFromJsonAsync<List<KnowledgeCategoryDto>>(
            "/api/v1/knowledge/categories?persona=site_investigator",
            Ct);
        investigatorCategories.Should().NotBeNull().And.NotBeEmpty();
        investigatorCategories!.Select(c => c.Slug).Should().Contain(["site-startup-activation", "clinical-operations"]);

        // Article versions and Merkle audit-log read endpoints
        var versionsResp = await client.GetAsync($"/api/v1/knowledge/articles/{articleId}/versions", Ct);
        versionsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var versions = await versionsResp.Content.ReadFromJsonAsync<List<KnowledgeArticleVersionDto>>(cancellationToken: Ct);
        versions.Should().NotBeNull().And.ContainSingle();
        versions![0].IsLocked.Should().BeTrue();
        versions[0].BodyMarkdown.Should().NotBeNullOrWhiteSpace();

        (await client.GetAsync("/api/v1/knowledge/articles/art-does-not-exist/versions", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var auditLogResp = await client.GetAsync($"/api/v1/knowledge/articles/{articleId}/audit-log", Ct);
        auditLogResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var articleAudits = await auditLogResp.Content.ReadFromJsonAsync<List<KnowledgeArticleAuditEntryDto>>(cancellationToken: Ct);
        articleAudits.Should().NotBeNull().And.NotBeEmpty();
        articleAudits!.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Sha256Hash));

        (await client.GetAsync("/api/v1/knowledge/articles/art-does-not-exist/audit-log", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Verify catalog publication wrote to shared Merkle-chained AuditLogEntries rather than KnowledgeArticleAuditLog
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        var auditEntries = await db.AuditLogEntries.AsNoTracking().ToListAsync(Ct);
        auditEntries.Should().NotBeEmpty();
        VerifyMerkleChain(auditEntries);
        (await db.KnowledgeArticleAuditLogs.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task SubjectCaller_IsForbiddenFromStaffContextualHelp()
    {
        await EnsureDbCreatedAsync();
        var subjectIdentity = TestSecurityContext.Identity("subj-01", "subject");
        using var client = _factory.CreateDefaultClient(new TestSecurityContext.GatewaySigningHandler(subjectIdentity));

        var response = await client.GetAsync("/api/v1/knowledge/contextual-help?route=/ecrf", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static ContextualHelpMapping BuildMapping(
        string id,
        string routePattern,
        string? persona,
        int priority,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = id,
            RoutePattern = routePattern,
            Persona = persona,
            ArticleId = $"art-{id}",
            SectionAnchor = "#section",
            Priority = priority,
            IsActive = true,
            CreatedAt = createdAt,
            CreatedBy = "svc:knowledge-sop-catalog",
        };

    private static KnowledgeArticle BuildPublishedArticle(string id, DateTimeOffset createdAt) =>
        new()
        {
            Id = id,
            CategoryId = "cat-test",
            Title = $"SOP {id}",
            Slug = id,
            Status = "PUBLISHED",
            VersionLabel = "v1.0",
            AuthorUserId = "lead_clinical_ops",
            IsDeleted = false,
            CreatedAt = createdAt,
            CreatedBy = "svc:knowledge-sop-catalog",
            ReasonForChange = "Test baseline",
            VersionIndex = 1,
        };

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
