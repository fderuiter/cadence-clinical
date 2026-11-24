using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Randomization;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.TestSupport.Execution;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Randomisation through the real composition root on PostgreSQL, as the application role (#5607, ADR-2640): a
/// second person approves the scheme, a participant is randomised only when enrolled, consented and at an activated
/// site, the arm is stored only as ciphertext (in the list, the allocation, the kit and the audit trail), and the
/// database refuses to rewrite an allocation or a consumed slot.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RandomizationPostgresTests(ExecutionPostgresApiFactory factory) : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string Reason = "Randomisation check: participant met every eligibility criterion.";
    private const string Site = "SITE-RND-01";

    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    // Distinctive arm codes, so a clear-text arm is unmistakable in a raw read of any table.
    private string ArmA => $"ZEBRA-{_run}";

    private string ArmB => $"OKAPI-{_run}";

    private string Study => $"STUDY-RND-{_run}";

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SUB-003")]
    public async Task PermutedBlock_RandomisesWithAKitOfTheArm_AndStoresTheArmOnlyAsCiphertext()
    {
        factory.SkipIfUnavailable();
        await ApproveSchemeAsync(new
        {
            study_id = Study,
            method = "permuted_block",
            arms = new Dictionary<string, int> { [ArmA] = 1, [ArmB] = 1 },
            block_sizes = new[] { 2, 4 },
            slots_per_stratum = 8,
            requires_kit = true,
        });
        var kitArms = new Dictionary<string, string>();
        foreach (string kitNumber in await SeedKitsAsync(ArmA, 2))
        {
            kitArms[kitNumber] = ArmA;
        }

        foreach (string kitNumber in await SeedKitsAsync(ArmB, 2))
        {
            kitArms[kitNumber] = ArmB;
        }
        string subject = await SeedEnrolledAsync();

        var crcCaller = Caller("crc", "crc");
        using var crc = factory.CreateSignedClient(crcCaller);
        var response = await PostRandomizeSubjectAsync(crc, crcCaller, subject);

        string body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().NotContain(ArmA).And.NotContain(ArmB);
        using var json = JsonDocument.Parse(body);
        string kit = json.RootElement.GetProperty("kit_number").GetString()!;
        json.RootElement.GetProperty("status").GetString().Should().Be("RANDOMIZED");

        await using var db = await OpenAsync();
        (await CountAsync(db, "SELECT count(*) FROM randomization_list_slots s JOIN randomization_schemes r ON r.\"Id\" = s.\"SchemeId\" WHERE r.\"StudyCode\" = @study", Study))
            .Should().Be(8);
        (await CountAsync(db, "SELECT count(*) FROM randomization_list_slots s JOIN randomization_schemes r ON r.\"Id\" = s.\"SchemeId\" WHERE r.\"StudyCode\" = @study AND s.\"AllocationId\" IS NOT NULL", Study))
            .Should().Be(1);
        (await CountAsync(db, "SELECT count(*) FROM ip_kit_inventory_items WHERE \"StudyId\" = @study AND \"AssignedSubjectCode\" = @p1", Study, subject))
            .Should().Be(1);

        // A database read finds no clear-text arm in any table that holds randomisation data. The scheme's own audit
        // entries name the arm codes, which are configuration; nothing else may.
        foreach (string clear in new[] { ArmA, ArmB })
        {
            (await CountAsync(db, "SELECT count(*) FROM randomization_list_slots WHERE \"ArmCiphertext\" LIKE '%' || @p1 || '%'", Study, clear)).Should().Be(0);
            (await CountAsync(db, "SELECT count(*) FROM randomization_allocations WHERE \"ArmCiphertext\" LIKE '%' || @p1 || '%' OR \"StratificationFactorsJson\"::text LIKE '%' || @p1 || '%'", Study, clear)).Should().Be(0);
            (await CountAsync(db, "SELECT count(*) FROM ip_kit_inventory_items WHERE \"TreatmentArmCiphertext\" LIKE '%' || @p1 || '%'", Study, clear)).Should().Be(0);
            (await CountAsync(db, "SELECT count(*) FROM \"AuditLogEntries\" WHERE \"EntityName\" <> 'RandomizationScheme' AND \"ChangesJson\" LIKE '%' || @p1 || '%'", Study, clear)).Should().Be(0);
        }

        // An unblinded caller sees the arm, and it is the arm of the kit the participant was given.
        using var pharmacist = factory.CreateSignedClient(Caller("pharmacist", "Pharmacist") with { UnblindedAccess = true });
        using var detail = JsonDocument.Parse(await pharmacist.GetStringAsync($"/api/v1/execution/subjects/{subject}", Ct));
        string arm = detail.RootElement.GetProperty("treatment_group").GetString()!;
        arm.Should().BeOneOf(ArmA, ArmB);
        detail.RootElement.GetProperty("investigational_product_id").GetString().Should().Be(kit);
        kitArms[kit].Should().Be(arm);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var actions = await context.AuditLogEntries.AsNoTracking()
            .Where(e => e.UserId == $"crc-{_run}")
            .Select(e => e.Action)
            .ToListAsync(Ct);
        actions.Should().Contain(["RANDOMIZE_SUBJECT", "ASSIGN_KIT"]);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SUB-003")]
    public async Task ConcurrentRandomisations_EachTakeTheirOwnSlot_AndTheBlockStaysBalanced()
    {
        factory.SkipIfUnavailable();
        await ApproveSchemeAsync(new
        {
            study_id = Study,
            method = "permuted_block",
            arms = new Dictionary<string, int> { [ArmA] = 1, [ArmB] = 1 },
            block_sizes = new[] { 4 },
            slots_per_stratum = 4,
            requires_kit = false,
        });
        var subjects = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            subjects.Add(await SeedEnrolledAsync());
        }

        var responses = await Task.WhenAll(subjects.Select(async s =>
        {
            var crcCaller = Caller("crc", "crc");
            using var client = factory.CreateSignedClient(crcCaller);
            return await PostRandomizeSubjectAsync(client, crcCaller, s);
        }));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        using var unblinded = factory.CreateSignedClient(Caller("pharmacist", "Pharmacist") with { UnblindedAccess = true });
        var arms = new List<string>();
        foreach (string s in subjects)
        {
            using var detail = JsonDocument.Parse(await unblinded.GetStringAsync($"/api/v1/execution/subjects/{s}", Ct));
            arms.Add(detail.RootElement.GetProperty("treatment_group").GetString()!);
        }

        arms.Count(a => a == ArmA).Should().Be(2, "one block of four holds two of each arm");
        await using var db = await OpenAsync();
        (await CountAsync(db, "SELECT count(DISTINCT \"Sequence\") FROM randomization_allocations WHERE \"StudyCode\" = @study", Study)).Should().Be(4);

        var nextCaller = Caller("crc", "crc");
        using var next = factory.CreateSignedClient(nextCaller);
        var exhausted = await PostRandomizeSubjectAsync(next, nextCaller, await SeedEnrolledAsync());
        exhausted.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await exhausted.Content.ReadAsStringAsync(Ct)).Should().Contain("RANDOMIZATION_LIST_EXHAUSTED");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SUB-004")]
    public async Task Minimisation_RandomisesEveryStratum_WithoutAList()
    {
        factory.SkipIfUnavailable();
        await ApproveSchemeAsync(new
        {
            study_id = Study,
            method = "minimization",
            arms = new Dictionary<string, int> { [ArmA] = 1, [ArmB] = 1 },
            stratification_factors = new Dictionary<string, string[]> { ["sex"] = ["F", "M"] },
            preferred_arm_probability = 0.8,
            requires_kit = false,
        });

        var crcCaller = Caller("crc", "crc");
        using var crc = factory.CreateSignedClient(crcCaller);
        foreach (string sex in new[] { "F", "M", "F", "M" })
        {
            var response = await PostRandomizeSubjectAsync(crc, crcCaller, await SeedEnrolledAsync(),
                new { stratification_factors = new Dictionary<string, string> { ["sex"] = sex } });
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        }

        await using var db = await OpenAsync();
        (await CountAsync(db, "SELECT count(*) FROM randomization_allocations WHERE \"StudyCode\" = @study", Study)).Should().Be(4);
        (await CountAsync(db, "SELECT count(*) FROM randomization_allocations WHERE \"StudyCode\" = @study AND \"ListSlotId\" IS NULL", Study)).Should().Be(4);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SSU-003")]
    public async Task Randomisation_AtANonGreenlitSite_IsRefusedWith403_AndAnAuditEntryNamingTheCaller()
    {
        factory.SkipIfUnavailable();
        await ApproveDefaultSchemeAsync();
        string subject = await SeedEnrolledAsync(activateSite: false);

        var crcCaller = Caller("crc-blocked", "crc");
        using var crc = factory.CreateSignedClient(crcCaller);
        var response = await PostRandomizeSubjectAsync(crc, crcCaller, subject);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("RANDOMIZATION_BLOCKED_SITE");
        await ShouldBeRefusedAndAuditedAsync(subject, "BLOCKED_RANDOMIZATION_SITE", $"crc-blocked-{_run}");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SUB-001")]
    public async Task Randomisation_WithoutACurrentConsent_IsRefusedWith403_AndAudited()
    {
        factory.SkipIfUnavailable();
        await ApproveDefaultSchemeAsync();
        string subject = await SeedEnrolledAsync(consent: false);

        var crcCaller = Caller("crc-noconsent", "crc");
        using var crc = factory.CreateSignedClient(crcCaller);
        var response = await PostRandomizeSubjectAsync(crc, crcCaller, subject);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("RANDOMIZATION_REQUIRES_CONSENT");
        await ShouldBeRefusedAndAuditedAsync(subject, "BLOCKED_RANDOMIZATION_CONSENT", $"crc-noconsent-{_run}");
    }

    [Fact]
    public async Task SchemeApproval_ByItsAuthor_IsRefused_AndStateChangeCannotRandomise()
    {
        factory.SkipIfUnavailable();
        var authorCaller = Caller("stat-a", "unblinded_statistician");
        using var author = factory.CreateSignedClient(authorCaller);
        var drafted = await author.PostAsJsonAsync("/api/v1/execution/randomization/schemes", DefaultScheme(), Ct);
        drafted.StatusCode.Should().Be(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync(Ct));
        using var scheme = JsonDocument.Parse(await drafted.Content.ReadAsStringAsync(Ct));
        string id = scheme.RootElement.GetProperty("id").GetString()!;

        var selfApproval = await PostApproveSchemeAsync(author, authorCaller, id);
        selfApproval.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        string subject = await SeedEnrolledAsync();
        var crcCaller = Caller("crc", "crc");
        using var crc = factory.CreateSignedClient(crcCaller);
        var notLive = await PostRandomizeSubjectAsync(crc, crcCaller, subject);
        notLive.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await notLive.Content.ReadAsStringAsync(Ct)).Should().Contain("RANDOMIZATION_NOT_LIVE");

        var shortcut = await crc.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "RANDOMIZED" }, Ct);
        shortcut.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Enrolled);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SUB-003")]
    public async Task TheDatabase_RefusesToRewriteAnAllocationOrAConsumedSlot()
    {
        factory.SkipIfUnavailable();
        await ApproveDefaultSchemeAsync();
        string subject = await SeedEnrolledAsync();
        var crcCaller = Caller("crc", "crc");
        using var crc = factory.CreateSignedClient(crcCaller);
        (await PostRandomizeSubjectAsync(crc, crcCaller, subject)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        await using var db = await OpenAsync();
        await ShouldRefuseAsync(db, "UPDATE randomization_allocations SET \"ArmCiphertext\" = 'x' WHERE \"StudyCode\" = @study");
        await ShouldRefuseAsync(db, "DELETE FROM randomization_allocations WHERE \"StudyCode\" = @study");
        await ShouldRefuseAsync(db, "UPDATE randomization_list_slots SET \"AllocationId\" = NULL WHERE \"AllocationId\" IS NOT NULL AND \"SchemeId\" IN (SELECT \"Id\" FROM randomization_schemes WHERE \"StudyCode\" = @study)");
        await ShouldRefuseAsync(db, "DELETE FROM randomization_list_slots WHERE \"SchemeId\" IN (SELECT \"Id\" FROM randomization_schemes WHERE \"StudyCode\" = @study)");

        (await PostRandomizeSubjectAsync(crc, crcCaller, subject)).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "a participant is randomised once");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SchemeApproval_WithoutSigToken_Returns401ReauthenticationRequired()
    {
        factory.SkipIfUnavailable();
        using var author = factory.CreateSignedClient(Caller("stat-a", "unblinded_statistician"));
        using var approver = factory.CreateSignedClient(Caller("stat-b", "unblinded_statistician"));
        var drafted = await author.PostAsJsonAsync("/api/v1/execution/randomization/schemes", DefaultScheme(), Ct);
        drafted.StatusCode.Should().Be(HttpStatusCode.Created);
        using var scheme = JsonDocument.Parse(await drafted.Content.ReadAsStringAsync(Ct));
        string id = scheme.RootElement.GetProperty("id").GetString()!;

        using var response = await approver.PostAsync($"/api/v1/execution/randomization/schemes/{id}/approve", null, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        string body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain(SigTokenVerifier.ReauthenticationRequired);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SubjectRandomization_WithoutSigToken_Returns401ReauthenticationRequired()
    {
        factory.SkipIfUnavailable();
        await ApproveDefaultSchemeAsync();
        string subject = await SeedEnrolledAsync();

        using var crc = factory.CreateSignedClient(Caller("crc", "crc"));
        using var response = await crc.PostAsJsonAsync($"/api/v1/execution/subjects/{subject}/randomize", new { }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        string body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain(SigTokenVerifier.ReauthenticationRequired);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SchemeApproval_ReusingConsumedSigToken_Returns401ReauthenticationRequired()
    {
        factory.SkipIfUnavailable();
        var approverCaller = Caller("stat-b", "unblinded_statistician");
        using var author = factory.CreateSignedClient(Caller("stat-a", "unblinded_statistician"));
        using var approver = factory.CreateSignedClient(approverCaller);
        var drafted = await author.PostAsJsonAsync("/api/v1/execution/randomization/schemes", DefaultScheme(), Ct);
        drafted.StatusCode.Should().Be(HttpStatusCode.Created);
        using var scheme = JsonDocument.Parse(await drafted.Content.ReadAsStringAsync(Ct));
        string id = scheme.RootElement.GetProperty("id").GetString()!;

        string path = RandomizationEndpoints.ApproveSchemeAction(Guid.Parse(id));
        string sigToken = TestSecurityContext.CreateSigToken(
            approverCaller.UserId,
            path,
            RandomizationEndpoints.ApproveSchemeSemanticAction,
            roles: [RoleNormalization.ROLE_UNBLINDED_STATISTICIAN]);

        using var response1 = await PostApproveSchemeAsync(approver, approverCaller, id, sigToken: sigToken);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response2 = await PostApproveSchemeAsync(approver, approverCaller, id, sigToken: sigToken);
        response2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        string body = await response2.Content.ReadAsStringAsync(Ct);
        body.Should().Contain(SigTokenVerifier.ReauthenticationRequired);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SubjectRandomization_ReusingConsumedSigToken_Returns401ReauthenticationRequired()
    {
        factory.SkipIfUnavailable();
        await ApproveDefaultSchemeAsync();
        string subject = await SeedEnrolledAsync();

        var crcCaller = Caller("crc", "crc");
        using var crc = factory.CreateSignedClient(crcCaller);

        string path = RandomizationEndpoints.RandomizeSubjectAction(subject);
        string sigToken = TestSecurityContext.CreateSigToken(
            crcCaller.UserId,
            path,
            RandomizationEndpoints.RandomizeSubjectSemanticAction,
            roles: [RoleNormalization.ROLE_CRC]);

        using var response1 = await PostRandomizeSubjectAsync(crc, crcCaller, subject, sigToken: sigToken);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response2 = await PostRandomizeSubjectAsync(crc, crcCaller, subject, sigToken: sigToken);
        response2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        string body = await response2.Content.ReadAsStringAsync(Ct);
        body.Should().Contain(SigTokenVerifier.ReauthenticationRequired);
    }

    [Fact]
    public async Task ApproveScheme_RetiresActiveVersionStagesMultiBatchSlotsAndPreservesAuditChain()
    {
        factory.SkipIfUnavailable();
        var authorCaller = Caller("stat-a", "unblinded_statistician");
        var approverCaller = Caller("stat-b", "unblinded_statistician");
        using var author = factory.CreateSignedClient(authorCaller);
        using var approver = factory.CreateSignedClient(approverCaller);

        var draftV1 = await author.PostAsJsonAsync("/api/v1/execution/randomization/schemes", DefaultScheme(), Ct);
        draftV1.StatusCode.Should().Be(HttpStatusCode.Created);
        using var v1Json = JsonDocument.Parse(await draftV1.Content.ReadAsStringAsync(Ct));
        string v1Id = v1Json.RootElement.GetProperty("id").GetString()!;
        (await PostApproveSchemeAsync(approver, approverCaller, v1Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        // 2,500 slots crosses UnnestBatchSize (2,048), exercising both full and tail unnest batches on PostgreSQL 18.
        var draftV2 = await author.PostAsJsonAsync("/api/v1/execution/randomization/schemes", new
        {
            study_id = Study,
            method = "permuted_block",
            arms = new Dictionary<string, int> { [ArmA] = 1, [ArmB] = 1 },
            block_sizes = new[] { 2, 4 },
            slots_per_stratum = 2_500,
            requires_kit = false,
        }, Ct);
        draftV2.StatusCode.Should().Be(HttpStatusCode.Created);
        using var v2Json = JsonDocument.Parse(await draftV2.Content.ReadAsStringAsync(Ct));
        string v2Id = v2Json.RootElement.GetProperty("id").GetString()!;

        using var approvedV2 = await PostApproveSchemeAsync(approver, approverCaller, v2Id);
        approvedV2.StatusCode.Should().Be(HttpStatusCode.OK, await approvedV2.Content.ReadAsStringAsync(Ct));

        await using var db = await OpenAsync();
        (await CountAsync(db, "SELECT count(*) FROM randomization_list_slots WHERE \"SchemeId\" = @p1::uuid", Study, v2Id))
            .Should().Be(2_500);

        using var scope = factory.Services.CreateSeedingScope();
        var efDb = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var schemes = await efDb.RandomizationSchemes.AsNoTracking()
            .Where(s => s.StudyCode == new StudyCode(Study))
            .OrderBy(s => s.Version)
            .ToListAsync(Ct);
        schemes.Should().HaveCount(2);
        schemes[0].Status.Should().Be(RandomizationSchemeStatus.Retired);
        schemes[1].Status.Should().Be(RandomizationSchemeStatus.Approved);

        var schemeIds = schemes.Select(s => s.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        var auditEntries = await efDb.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityName == nameof(RandomizationScheme))
            .ToListAsync(Ct);
        auditEntries.Where(e => schemeIds.Contains(e.EntityId)).Select(e => e.Action)
            .Should().Contain(["DRAFT_RANDOMIZATION_SCHEME", "RETIRE_RANDOMIZATION_SCHEME", "APPROVE_RANDOMIZATION_SCHEME"]);
        AuditLogChainVerifier.Verify(auditEntries).Should().ContainSingle().Which.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveScheme_ConcurrentRequestsByTwoStatisticians_SerializesSoExactlyOneWinsAndOneReturns409()
    {
        factory.SkipIfUnavailable();
        var authorCaller = Caller("stat-a", "unblinded_statistician");
        var approverB = Caller("stat-b", "unblinded_statistician");
        var approverC = Caller("stat-c", "unblinded_statistician");
        using var author = factory.CreateSignedClient(authorCaller);
        using var clientB = factory.CreateSignedClient(approverB);
        using var clientC = factory.CreateSignedClient(approverC);

        var drafted = await author.PostAsJsonAsync("/api/v1/execution/randomization/schemes", new
        {
            study_id = Study,
            method = "permuted_block",
            arms = new Dictionary<string, int> { [ArmA] = 1, [ArmB] = 1 },
            slots_per_stratum = 256,
            requires_kit = false,
        }, Ct);
        drafted.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await drafted.Content.ReadAsStringAsync(Ct));
        string schemeId = json.RootElement.GetProperty("id").GetString()!;

        using var barrier = new SemaphoreSlim(0, 2);
        async Task<HttpResponseMessage> AttemptApproveAsync(HttpClient client, GatewayIdentity caller)
        {
            await barrier.WaitAsync(Ct);
            return await PostApproveSchemeAsync(client, caller, schemeId);
        }

        var taskB = AttemptApproveAsync(clientB, approverB);
        var taskC = AttemptApproveAsync(clientC, approverC);
        barrier.Release(2);

        HttpResponseMessage[] responses = await Task.WhenAll(taskB, taskC);
        try
        {
            responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
            HttpResponseMessage conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
            (await conflict.Content.ReadAsStringAsync(Ct)).Should().Contain("RANDOMIZATION_SCHEME_NOT_DRAFT");
        }
        finally
        {
            foreach (var r in responses)
            {
                r.Dispose();
            }
        }

        await using var db = await OpenAsync();
        (await CountAsync(db, "SELECT count(*) FROM randomization_list_slots WHERE \"SchemeId\" = @p1::uuid", Study, schemeId))
            .Should().Be(256, "the losing concurrent approval must abort before inserting duplicate slots");
    }

    [Fact]
    public async Task ApproveScheme_FailureAfterStagingSlots_RollsBackSlotsAndLeavesDraftUnchanged()
    {
        factory.SkipIfUnavailable();
        var authorCaller = Caller("stat-a", "unblinded_statistician");
        using var author = factory.CreateSignedClient(authorCaller);
        var drafted = await author.PostAsJsonAsync("/api/v1/execution/randomization/schemes", new
        {
            study_id = Study,
            method = "permuted_block",
            arms = new Dictionary<string, int> { [ArmA] = 1, [ArmB] = 1 },
            slots_per_stratum = 64,
            requires_kit = false,
        }, Ct);
        drafted.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await drafted.Content.ReadAsStringAsync(Ct));
        Guid schemeId = Guid.Parse(json.RootElement.GetProperty("id").GetString()!);

        using (var scope = factory.Services.CreateSeedingScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<IAllocationProtector>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var service = new RandomizationApplicationService(
                new RandomizationRepository(db),
                new SubjectRepository(db),
                new ConsentRepository(db),
                new SiteComplianceRepository(db),
                protector,
                new ThrowingApproveAuditLogWriter(),
                new FixedApproveAuditContext($"stat-b-{_run}", Reason),
                clock);

            var act = () => service.ApproveSchemeAsync(schemeId, Reason, Ct);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Simulated post-staging failure*");
        }

        await using var rawDb = await OpenAsync();
        (await CountAsync(rawDb, "SELECT count(*) FROM randomization_list_slots WHERE \"SchemeId\" = @p1::uuid", Study, schemeId.ToString("D")))
            .Should().Be(0, "untracked slots staged inside the rolled-back transaction must never persist");

        using var verifyScope = factory.Services.CreateSeedingScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var scheme = await verifyDb.RandomizationSchemes.AsNoTracking().SingleAsync(s => s.Id == schemeId, Ct);
        scheme.Status.Should().Be(RandomizationSchemeStatus.Draft);
        scheme.ApprovedBy.Should().BeNull();
    }

    private sealed record FixedApproveAuditContext(string UserId, string ChangeReason) : ICurrentAuditContextAccessor;

    private sealed class ThrowingApproveAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(
            string entityName,
            string recordId,
            string action,
            string userId,
            string changeReason,
            IReadOnlyDictionary<string, object?>? oldValues,
            IReadOnlyDictionary<string, object?>? newValues,
            CancellationToken ct)
        {
            if (action == "APPROVE_RANDOMIZATION_SCHEME")
            {
                throw new InvalidOperationException("Simulated post-staging failure after AddListSlotsAsync.");
            }

            return Task.CompletedTask;
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GatewayIdentity Caller(string name, string role) =>
        TestSecurityContext.Identity($"{name}-{_run}", role) with
        {
            SiteIds = RoleNormalization.GetNormalizedRoles([role]).Any(SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains) ? [Site] : [],
            ChangeReason = Reason,
        };

    private object DefaultScheme() => new
    {
        study_id = Study,
        method = "permuted_block",
        arms = new Dictionary<string, int> { [ArmA] = 1, [ArmB] = 1 },
        slots_per_stratum = 4,
        requires_kit = false,
    };

    private Task ApproveDefaultSchemeAsync() => ApproveSchemeAsync(DefaultScheme());

    /// <summary>Drafts a scheme as one statistician and approves it as another.</summary>
    private async Task ApproveSchemeAsync(object draft)
    {
        var authorCaller = Caller("stat-a", "unblinded_statistician");
        var approverCaller = Caller("stat-b", "unblinded_statistician");
        using var author = factory.CreateSignedClient(authorCaller);
        using var approver = factory.CreateSignedClient(approverCaller);
        var drafted = await author.PostAsJsonAsync("/api/v1/execution/randomization/schemes", draft, Ct);
        drafted.StatusCode.Should().Be(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync(Ct));
        using var scheme = JsonDocument.Parse(await drafted.Content.ReadAsStringAsync(Ct));
        string id = scheme.RootElement.GetProperty("id").GetString()!;

        var approved = await PostApproveSchemeAsync(approver, approverCaller, id);
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<HttpResponseMessage> PostApproveSchemeAsync(
        HttpClient client, GatewayIdentity caller, string schemeId, string? sigToken = null)
    {
        string path = RandomizationEndpoints.ApproveSchemeAction(Guid.Parse(schemeId));
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        string token = sigToken ?? TestSecurityContext.CreateSigToken(
            caller.UserId,
            path,
            RandomizationEndpoints.ApproveSchemeSemanticAction,
            roles: [RoleNormalization.ROLE_UNBLINDED_STATISTICIAN]);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Add(GatewayHeaders.SigToken, token);
        }
        return await client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> PostRandomizeSubjectAsync(
        HttpClient client, GatewayIdentity caller, string subjectId, object? body = null, string? sigToken = null)
    {
        string path = RandomizationEndpoints.RandomizeSubjectAction(subjectId);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body ?? new { })
        };
        string token = sigToken ?? TestSecurityContext.CreateSigToken(
            caller.UserId,
            path,
            RandomizationEndpoints.RandomizeSubjectSemanticAction,
            roles: [RoleNormalization.NormalizeRole(caller.Roles.FirstOrDefault() ?? "crc")]);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Add(GatewayHeaders.SigToken, token);
        }
        return await client.SendAsync(request, Ct);
    }

    /// <summary>
    /// Seeds kits labelled with <paramref name="arm"/>, encrypted with the host's allocation key. Kit numbers are opaque,
    /// as on a real blinded label.
    /// </summary>
    private async Task<List<string>> SeedKitsAsync(string arm, int count)
    {
        var kitNumbers = new List<string>();
        using var scope = factory.Services.CreateSeedingScope();
        var protector = scope.ServiceProvider.GetRequiredService<IAllocationProtector>();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        for (int i = 0; i < count; i++)
        {
            string kitNumber = $"KIT-{Guid.NewGuid().ToString("N")[..10]}";
            kitNumbers.Add(kitNumber);
            var kit = new IpKitInventoryItem(Guid.NewGuid(), kitNumber, Study, Site, "LOT-RND", SystemClock.Instance.UtcNow.AddYears(1));
            kit.LabelTreatment(protector.Protect(arm, IpKitInventoryItem.ArmPurpose(Study, kitNumber)));
            db.IpKitInventoryItems.Add(kit);
        }

        await db.SaveChangesAsync(Ct);
        return kitNumbers;
    }

    private async Task<string> SeedEnrolledAsync(bool activateSite = true, bool consent = true)
    {
        string code = $"SUBJ-{Guid.NewGuid().ToString("N")[..10]}";
        var study = new StudyCode(Study);
        var site = new SiteCode(Site);
        using var scope = factory.Services.CreateSeedingScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();

        int index = await db.ClinicalSubjects.CountAsync(s => s.StudyCode == study, Ct) + 1;
        var subject = ClinicalSubject.Create(new SubjectCode(code), study, site, index, "seed-ciphertext", null);
        subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();
        db.ClinicalSubjects.Add(subject);
        if (consent)
        {
            db.SubjectConsents.Add(SubjectConsent.Create(subject.SubjectCode, new ProtocolVersionRef(study, "1.0", 1),
                icfSigned: true, SystemClock.Instance.UtcNow.AddDays(-1), requiresReconsent: false, isPaperOverride: false));
        }

        if (activateSite && !await db.SiteComplianceCaches.AnyAsync(c => c.StudyCode == study && c.SiteCode == site, Ct))
        {
            db.SiteComplianceCaches.Add(SiteComplianceCache.Create(study, site, "SITE_ACTIVATION", true, []));
        }

        await db.SaveChangesAsync(Ct);
        return code;
    }

    private async Task ShouldBeRefusedAndAuditedAsync(string subject, string action, string userId)
    {
        var stored = await ReadSubjectAsync(subject);
        stored.Status.Should().Be(SubjectState.Enrolled);
        stored.RandomizationId.Should().BeNull();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        (await db.AuditLogEntries.AsNoTracking().CountAsync(e => e.Action == action && e.UserId == userId, Ct))
            .Should().Be(1);
        (await db.RandomizationAllocations.AsNoTracking().CountAsync(a => a.SubjectId == stored.Id, Ct)).Should().Be(0);
    }

    private async Task<ClinicalSubject> ReadSubjectAsync(string code)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        return await db.ClinicalSubjects.AsNoTracking().SingleAsync(s => s.SubjectCode == new SubjectCode(code), Ct);
    }

    /// <summary>A raw connection as the application role and active tenant session the host itself uses.</summary>
    private async Task<NpgsqlConnection> OpenAsync()
    {
        using var scope = factory.Services.CreateScope();
        string connectionString = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>().Database.GetConnectionString()!;
        Guid? tenantId = await scope.ServiceProvider
            .GetRequiredService<Cadence.Database.Tenancy.ICurrentTenantAccessor>()
            .GetCurrentTenantIdAsync(Ct);
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var setTenant = new NpgsqlCommand("SELECT pg_catalog.set_config('cadence.tenant_id', @tenant, false)", connection);
        setTenant.Parameters.AddWithValue("tenant", tenantId?.ToString("D") ?? string.Empty);
        await setTenant.ExecuteNonQueryAsync(Ct);
        return connection;
    }

    private static async Task<long> CountAsync(NpgsqlConnection db, string sql, string study, string? p1 = null)
    {
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("study", study);
        command.Parameters.AddWithValue("p1", (object?)p1 ?? DBNull.Value);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task ShouldRefuseAsync(NpgsqlConnection db, string sql)
    {
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("study", Study);
        var act = () => command.ExecuteNonQueryAsync(Ct);
        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.IntegrityConstraintViolation);
    }
}
