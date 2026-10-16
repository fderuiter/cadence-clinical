using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Interop;

/// <summary>
/// The SCE routes admit a caller only for the studies in their signed <c>study_ids</c> claim (#5402, ADR-2516), through
/// the real interop host on PostgreSQL as the application role. Before the fix, a data manager or unblinded statistician
/// assigned to one study could list programs, mint and revoke lock tokens, and run unblinded programs for any study.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SceStudyScopeTests(InteropPostgresApiFactory factory) : IClassFixture<InteropPostgresApiFactory>
{
    private const string Base = "/api/v1/interop/sce";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public async Task ADataManagerOnStudyA_CannotListMintOrRevokeStudyBLockTokens()
    {
        factory.SkipIfUnavailable();
        var (studyA, studyB) = (NewStudy(), NewStudy());
        string statistician = $"stat-{Guid.NewGuid():N}";
        string tokenB = await IssueAsync(AllStudies(DataManager()), studyB, statistician);
        using var client = factory.CreateSignedClient(OnlyIn(DataManager(), studyA));

        var list = await client.GetFromJsonAsync<JsonObject>($"{Base}/lock-tokens?study_id={studyB}", Ct);
        list!["tokens"]!.AsArray().Should().BeEmpty();

        var issue = await client.PostAsJsonAsync($"{Base}/lock-tokens", IssueBody(studyB, statistician), Ct);
        issue.StatusCode.Should().Be(HttpStatusCode.Forbidden, await issue.Content.ReadAsStringAsync(Ct));

        var revoke = await client.PostAsync($"{Base}/lock-tokens/{tokenB}/revoke?reason=out-of-scope", null, Ct);
        revoke.StatusCode.Should().Be(HttpStatusCode.Forbidden, await revoke.Content.ReadAsStringAsync(Ct));

        var tokens = await TokensForAsync(studyB);
        tokens.Should().ContainSingle().Which.IsRevoked.Should().BeFalse();
        (await AuditActionsAsync(studyB)).Should().Equal("ISSUE_LOCK_TOKEN");

        // The same caller still works in their own study.
        (await client.PostAsJsonAsync($"{Base}/lock-tokens", IssueBody(studyA, statistician), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public async Task AStatisticianOnStudyA_CannotListReadCreateOrExecuteStudyBPrograms()
    {
        factory.SkipIfUnavailable();
        var (studyA, studyB) = (NewStudy(), NewStudy());
        var statistician = Statistician();
        string programB = await SeedProgramAsync(studyB);
        string tokenB = await IssueAsync(AllStudies(DataManager()), studyB, statistician.UserId);
        using var client = factory.CreateSignedClient(OnlyIn(statistician, studyA));

        var list = await client.GetFromJsonAsync<JsonObject>($"{Base}/programs?study_id={studyB}", Ct);
        list!["programs"]!.AsArray().Should().BeEmpty();
        (await client.GetAsync($"{Base}/programs/{programB}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var create = await client.PostAsJsonAsync($"{Base}/programs", new { study_id = studyB, name = $"adae-{Guid.NewGuid():N}", title = "ADAE" }, Ct);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden, await create.Content.ReadAsStringAsync(Ct));

        var execute = await client.PostAsJsonAsync($"{Base}/execute", ExecuteBody(studyB, programB, tokenB), Ct);
        execute.StatusCode.Should().Be(HttpStatusCode.Forbidden, await execute.Content.ReadAsStringAsync(Ct));

        // Naming study A does not let the caller run study B's program under it.
        var crossStudy = await client.PostAsJsonAsync($"{Base}/execute", ExecuteBody(studyA, programB, null), Ct);
        crossStudy.StatusCode.Should().Be(HttpStatusCode.NotFound, await crossStudy.Content.ReadAsStringAsync(Ct));

        (await ProgramsForAsync(studyB)).Should().ContainSingle();
        (await RunsForAsync(studyA)).Should().BeEmpty();
        (await RunsForAsync(studyB)).Should().BeEmpty();
        (await AuditActionsAsync(studyB)).Should().NotContain("EXECUTE_PROGRAM");
    }

    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public async Task AStatisticianOnStudyA_CannotListOpenVerifyOrReadArtifactsOfStudyBRuns()
    {
        factory.SkipIfUnavailable();
        var (studyA, studyB) = (NewStudy(), NewStudy());
        var statistician = Statistician();
        string programB = await SeedProgramAsync(studyB);
        string tokenB = await IssueAsync(AllStudies(DataManager()), studyB, statistician.UserId);
        using (var owner = factory.CreateSignedClient(statistician))
        {
            var run = await owner.PostAsJsonAsync($"{Base}/execute", ExecuteBody(studyB, programB, tokenB), Ct);
            run.StatusCode.Should().Be(HttpStatusCode.OK, await run.Content.ReadAsStringAsync(Ct));
        }

        string runB = (await RunsForAsync(studyB)).Single().RunId;
        using var client = factory.CreateSignedClient(OnlyIn(statistician, studyA));

        var list = await client.GetFromJsonAsync<JsonObject>($"{Base}/runs?study_id={studyB}", Ct);
        list!["runs"]!.AsArray().Should().BeEmpty();
        (await client.GetAsync($"{Base}/runs/{runB}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync($"{Base}/runs/{runB}/verify", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"{Base}/runs/{runB}/artifacts/art-1", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A caller whose signed claim names no study reaches none: scope fails closed.</summary>
    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public async Task ACallerWithNoStudyAssignment_ReachesNoStudy()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        using var client = factory.CreateSignedClient(DataManager() with { StudyIds = [] });

        var issue = await client.PostAsJsonAsync($"{Base}/lock-tokens", IssueBody(study, "stat-a"), Ct);

        issue.StatusCode.Should().Be(HttpStatusCode.Forbidden, await issue.Content.ReadAsStringAsync(Ct));
        (await TokensForAsync(study)).Should().BeEmpty();
    }

    /// <summary>The interop host under test connects as an application role, never as a superuser or with BYPASSRLS.</summary>
    [Fact]
    public async Task TheHostConnectsAsANonSuperuserApplicationRole()
    {
        factory.SkipIfUnavailable();
        await using var scope = factory.Services.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<InteropDbContext>().Database.GetDbConnection();
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user";

        (await command.ExecuteScalarAsync(Ct)).Should().Be(false);
    }

    private static GatewayIdentity DataManager() => TestSecurityContext.Identity($"dm-{Guid.NewGuid():N}", "sponsor_dm");

    private static GatewayIdentity Statistician() =>
        TestSecurityContext.Identity($"stat-unblinded-{Guid.NewGuid():N}", "unblinded_statistician");

    private static GatewayIdentity OnlyIn(GatewayIdentity identity, string study) => identity with { StudyIds = [study] };

    private static GatewayIdentity AllStudies(GatewayIdentity identity) => identity with { StudyIds = TestSecurityContext.AllStudies };

    private static string NewStudy() => $"STUDY-SCE-{Guid.NewGuid():N}";

    private static JsonObject IssueBody(string study, string issuedTo) => new()
    {
        ["study_id"] = study,
        ["lock_id"] = $"LOCK-{study}",
        ["milestone"] = "DATABASE_LOCK",
        ["issued_to"] = issuedTo,
        ["reason"] = "Final analysis after database lock",
    };

    private static object ExecuteBody(string study, string program, string? tokenId) => new
    {
        study_id = study,
        program_id = program,
        snapshot_id = "snap-1",
        is_unblinded = tokenId != null,
        database_lock_token_id = tokenId,
        reason_for_execution = "Final analysis",
    };

    private async Task<string> IssueAsync(GatewayIdentity issuer, string study, string issuedTo)
    {
        using var client = factory.CreateSignedClient(issuer);
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

    private async Task<DatabaseLockAuthToken[]> TokensForAsync(string study)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
        return await db.DatabaseLockAuthTokens.AsNoTracking().Where(t => t.StudyId == study).ToArrayAsync(Ct);
    }

    private async Task<StatisticalProgram[]> ProgramsForAsync(string study)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
        return await db.StatisticalPrograms.AsNoTracking().Where(p => p.StudyId == study).ToArrayAsync(Ct);
    }

    private async Task<ExecutionRunRecord[]> RunsForAsync(string study)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
        return await db.ExecutionRunRecords.AsNoTracking().Where(r => r.StudyId == study).ToArrayAsync(Ct);
    }

    private async Task<string[]> AuditActionsAsync(string study)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
        return await db.BiostatExportAuditLogs.AsNoTracking().Where(a => a.StudyId == study).Select(a => a.Action).ToArrayAsync(Ct);
    }
}
