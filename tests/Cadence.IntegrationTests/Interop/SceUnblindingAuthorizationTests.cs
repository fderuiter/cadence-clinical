using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Interop;

/// <summary>
/// Who may mint and use the SCE database-lock tokens that unlock unblinded analysis, and who may read unblinded runs
/// (#5382), through the real interop host on PostgreSQL. Before the fix any signed-in caller could mint a token, the
/// body chose its roles, a caller without a role claim was treated as the unblinded statistician, the token was
/// signed with a key written in the source, and <c>/execute</c> ran any unexpired token for anyone.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SceUnblindingAuthorizationTests(InteropPostgresApiFactory factory) : IClassFixture<InteropPostgresApiFactory>
{
    private const string Base = "/api/v1/interop/sce";

    private static readonly GatewayIdentity DataManager = TestSecurityContext.Identity("dm-sce", "sponsor_dm");
    private static readonly GatewayIdentity Coordinator = TestSecurityContext.Identity("crc-sce", "crc");
    private static readonly GatewayIdentity BlindedStatistician = TestSecurityContext.Identity("stat-blinded", "sponsor_statistician");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public async Task IssueLockToken_ByACoordinator_Returns403AndMintsNothing()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        using var client = factory.CreateSignedClient(Coordinator);

        var response = await client.PostAsJsonAsync($"{Base}/lock-tokens", IssueBody(study, "stat-a"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(Ct));
        (await TokensForAsync(study)).Should().BeEmpty();
    }

    /// <summary>A caller with no role claim gets nothing, where before it was treated as the unblinded statistician.</summary>
    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public async Task IssueLockToken_ByACallerWithNoRole_Returns403AndMintsNothing()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("no-role-sce"));

        var response = await client.PostAsJsonAsync($"{Base}/lock-tokens", IssueBody(study, "stat-a"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(Ct));
        (await TokensForAsync(study)).Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public async Task IssueLockToken_IgnoresRolesInTheBody()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        using var client = factory.CreateSignedClient(DataManager);
        var body = IssueBody(study, "stat-a");
        body["authorized_roles"] = new JsonArray("crc", "sponsor_statistician");

        var response = await client.PostAsJsonAsync($"{Base}/lock-tokens", body, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var token = (await TokensForAsync(study)).Single();
        token.AuthorizedRolesJson.Should().NotContain("crc").And.NotContain("sponsor_statistician");
        token.CreatedBy.Should().Be(DataManager.UserId);
    }

    /// <summary>A token row signed with the literal key that used to be in the source does not unlock anything.</summary>
    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public async Task Execute_WithATokenSignedWithTheOldLiteralKey_Returns403()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        var statistician = UnblindedStatistician();
        string program = await SeedProgramAsync(study);
        string tokenId = await SeedLiteralKeyTokenAsync(study, statistician.UserId);
        using var client = factory.CreateSignedClient(statistician);

        var response = await client.PostAsJsonAsync($"{Base}/execute", ExecuteBody(study, program, tokenId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(Ct));
        (await RunsForAsync(study)).Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public async Task Execute_WithATokenIssuedToSomeoneElse_Returns403()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        var recipient = UnblindedStatistician();
        var other = UnblindedStatistician();
        string program = await SeedProgramAsync(study);
        string tokenId = await IssueAsync(study, recipient.UserId);
        using var client = factory.CreateSignedClient(other);

        var response = await client.PostAsJsonAsync($"{Base}/execute", ExecuteBody(study, program, tokenId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(Ct));
        (await RunsForAsync(study)).Should().BeEmpty();
    }

    /// <summary>
    /// The recipient runs the unblinded analysis as themselves, and the audit trail names the verified callers. A
    /// blinded statistician then neither lists nor opens that run.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public async Task Execute_ByTheRecipient_RunsUnblindedAndOnlyUnblindedReadersSeeIt()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        var recipient = UnblindedStatistician();
        string program = await SeedProgramAsync(study);
        string tokenId = await IssueAsync(study, recipient.UserId);
        using var client = factory.CreateSignedClient(recipient);

        var response = await client.PostAsJsonAsync($"{Base}/execute", ExecuteBody(study, program, tokenId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var run = (await RunsForAsync(study)).Single();
        run.IsUnblindedRun.Should().BeTrue();
        run.ExecutedBy.Should().Be(recipient.UserId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
            var audit = await db.BiostatExportAuditLogs.AsNoTracking().Where(a => a.StudyId == study).ToListAsync(Ct);
            audit.Should().Contain(a => a.Action == "ISSUE_LOCK_TOKEN" && a.UserId == DataManager.UserId);
            audit.Should().Contain(a => a.Action == "EXECUTE_PROGRAM" && a.UserId == recipient.UserId && a.LockTokenId == tokenId);
        }

        using var blinded = factory.CreateSignedClient(BlindedStatistician);
        var list = await blinded.GetFromJsonAsync<JsonObject>($"{Base}/runs?study_id={study}", Ct);
        list!["runs"]!.AsArray().Should().BeEmpty();
        (await blinded.GetAsync($"{Base}/runs/{run.RunId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await blinded.GetAsync($"{Base}/runs/{run.RunId}/artifacts/art-1", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static GatewayIdentity UnblindedStatistician() =>
        TestSecurityContext.Identity($"stat-unblinded-{Guid.NewGuid():N}", "unblinded_statistician");

    private static string NewStudy() => $"STUDY-SCE-{Guid.NewGuid():N}";

    private static JsonObject IssueBody(string study, string issuedTo) => new()
    {
        ["study_id"] = study,
        ["lock_id"] = $"LOCK-{study}",
        ["milestone"] = "DATABASE_LOCK",
        ["issued_to"] = issuedTo,
        ["reason"] = "Final analysis after database lock",
    };

    private static object ExecuteBody(string study, string program, string tokenId) => new
    {
        study_id = study,
        program_id = program,
        snapshot_id = "snap-1",
        is_unblinded = true,
        database_lock_token_id = tokenId,
        reason_for_execution = "Unblinded final analysis",
    };

    private async Task<string> IssueAsync(string study, string issuedTo)
    {
        using var client = factory.CreateSignedClient(DataManager);
        var response = await client.PostAsJsonAsync($"{Base}/lock-tokens", IssueBody(study, issuedTo), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Ct);
        return body!["token"]!["token_id"]!.GetValue<string>();
    }

    private async Task<string> SeedProgramAsync(string study)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
        var program = new StatisticalProgram
        {
            StudyId = study,
            Name = $"adsl-{Guid.NewGuid():N}",
            Title = "ADSL",
            SourceCode = "print('ok')",
            CreatedAt = DateTimeOffset.UnixEpoch,
            CreatedBy = "stat-author",
        };
        db.StatisticalPrograms.Add(program);
        await db.SaveChangesAsync(Ct);
        return program.Id;
    }

    /// <summary>A token row as the old code signed it: HMAC-SHA256 keyed with the literal "CADENCE_SCE_SIGNING_KEY".</summary>
    private async Task<string> SeedLiteralKeyTokenAsync(string study, string issuedTo)
    {
        var now = SystemClock.Instance.UtcNow.AddMinutes(-1);
        var expiresAt = now.AddHours(24);
        string tokenId = $"DBLOCK-AUTH-FORGED-{Guid.NewGuid():N}";
        string lockId = $"LOCK-{study}";
        string payload = $"{tokenId}|{study}|{lockId}|{issuedTo}|{expiresAt:O}";
        string signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("CADENCE_SCE_SIGNING_KEY"), Encoding.UTF8.GetBytes(payload)));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
        db.DatabaseLockAuthTokens.Add(new DatabaseLockAuthToken
        {
            TokenId = tokenId,
            StudyId = study,
            LockId = lockId,
            IssuedTo = issuedTo,
            AuthorizedRolesJson = JsonSerializer.Serialize(new[] { "unblinded_statistician" }),
            IssuedAt = now,
            ExpiresAt = expiresAt,
            SignatureHash = signature,
            CreatedAt = now,
            CreatedBy = "forger",
        });
        await db.SaveChangesAsync(Ct);
        return tokenId;
    }

    private async Task<DatabaseLockAuthToken[]> TokensForAsync(string study)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
        return await db.DatabaseLockAuthTokens.AsNoTracking().Where(t => t.StudyId == study).ToArrayAsync(Ct);
    }

    private async Task<ExecutionRunRecord[]> RunsForAsync(string study)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
        return await db.ExecutionRunRecords.AsNoTracking().Where(r => r.StudyId == study).ToArrayAsync(Ct);
    }
}
