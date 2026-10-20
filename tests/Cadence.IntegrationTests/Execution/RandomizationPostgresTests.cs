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
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
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

        using var crc = factory.CreateSignedClient(Caller("crc", "crc"));
        var response = await crc.PostAsJsonAsync($"/api/v1/execution/subjects/{subject}/randomize", new { }, Ct);

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
            using var client = factory.CreateSignedClient(Caller("crc", "crc"));
            return await client.PostAsJsonAsync($"/api/v1/execution/subjects/{s}/randomize", new { }, Ct);
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

        using var next = factory.CreateSignedClient(Caller("crc", "crc"));
        var exhausted = await next.PostAsJsonAsync($"/api/v1/execution/subjects/{await SeedEnrolledAsync()}/randomize", new { }, Ct);
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

        using var crc = factory.CreateSignedClient(Caller("crc", "crc"));
        foreach (string sex in new[] { "F", "M", "F", "M" })
        {
            var response = await crc.PostAsJsonAsync($"/api/v1/execution/subjects/{await SeedEnrolledAsync()}/randomize",
                new { stratification_factors = new Dictionary<string, string> { ["sex"] = sex } }, Ct);
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

        using var crc = factory.CreateSignedClient(Caller("crc-blocked", "crc"));
        var response = await crc.PostAsJsonAsync($"/api/v1/execution/subjects/{subject}/randomize", new { }, Ct);

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

        using var crc = factory.CreateSignedClient(Caller("crc-noconsent", "crc"));
        var response = await crc.PostAsJsonAsync($"/api/v1/execution/subjects/{subject}/randomize", new { }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("RANDOMIZATION_REQUIRES_CONSENT");
        await ShouldBeRefusedAndAuditedAsync(subject, "BLOCKED_RANDOMIZATION_CONSENT", $"crc-noconsent-{_run}");
    }

    [Fact]
    public async Task SchemeApproval_ByItsAuthor_IsRefused_AndStateChangeCannotRandomise()
    {
        factory.SkipIfUnavailable();
        using var author = factory.CreateSignedClient(Caller("stat-a", "unblinded_statistician"));
        var drafted = await author.PostAsJsonAsync("/api/v1/execution/randomization/schemes", DefaultScheme(), Ct);
        drafted.StatusCode.Should().Be(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync(Ct));
        using var scheme = JsonDocument.Parse(await drafted.Content.ReadAsStringAsync(Ct));
        string id = scheme.RootElement.GetProperty("id").GetString()!;

        var selfApproval = await author.PostAsync($"/api/v1/execution/randomization/schemes/{id}/approve", null, Ct);
        selfApproval.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        string subject = await SeedEnrolledAsync();
        using var crc = factory.CreateSignedClient(Caller("crc", "crc"));
        var notLive = await crc.PostAsJsonAsync($"/api/v1/execution/subjects/{subject}/randomize", new { }, Ct);
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
        using var crc = factory.CreateSignedClient(Caller("crc", "crc"));
        (await crc.PostAsJsonAsync($"/api/v1/execution/subjects/{subject}/randomize", new { }, Ct)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        await using var db = await OpenAsync();
        await ShouldRefuseAsync(db, "UPDATE randomization_allocations SET \"ArmCiphertext\" = 'x' WHERE \"StudyCode\" = @study");
        await ShouldRefuseAsync(db, "DELETE FROM randomization_allocations WHERE \"StudyCode\" = @study");
        await ShouldRefuseAsync(db, "UPDATE randomization_list_slots SET \"AllocationId\" = NULL WHERE \"AllocationId\" IS NOT NULL AND \"SchemeId\" IN (SELECT \"Id\" FROM randomization_schemes WHERE \"StudyCode\" = @study)");
        await ShouldRefuseAsync(db, "DELETE FROM randomization_list_slots WHERE \"SchemeId\" IN (SELECT \"Id\" FROM randomization_schemes WHERE \"StudyCode\" = @study)");

        (await crc.PostAsJsonAsync($"/api/v1/execution/subjects/{subject}/randomize", new { }, Ct)).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "a participant is randomised once");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GatewayIdentity Caller(string name, string role) =>
        TestSecurityContext.Identity($"{name}-{_run}", role) with { ChangeReason = Reason };

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
        using var author = factory.CreateSignedClient(Caller("stat-a", "unblinded_statistician"));
        using var approver = factory.CreateSignedClient(Caller("stat-b", "unblinded_statistician"));
        var drafted = await author.PostAsJsonAsync("/api/v1/execution/randomization/schemes", draft, Ct);
        drafted.StatusCode.Should().Be(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync(Ct));
        using var scheme = JsonDocument.Parse(await drafted.Content.ReadAsStringAsync(Ct));
        string id = scheme.RootElement.GetProperty("id").GetString()!;

        var approved = await approver.PostAsync($"/api/v1/execution/randomization/schemes/{id}/approve", null, Ct);
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync(Ct));
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

    /// <summary>A raw connection as the application role the host itself uses.</summary>
    private async Task<NpgsqlConnection> OpenAsync()
    {
        using var scope = factory.Services.CreateScope();
        string connectionString = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>().Database.GetConnectionString()!;
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
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
