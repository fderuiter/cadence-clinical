using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Standards;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Rules;

using FluentAssertions;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Rule packs as governed data on PostgreSQL (spec §10.1, #4811): a pack is drafted, released with an attestation and
/// loaded while the platform runs (K-RULE-1), pinned per study, and evaluated through <see cref="IRuleEvaluator"/>.
/// Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RulePackPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Rule pack test";
    private const string PackKey = "sample-vitals-rules";

    private static readonly RuleEvaluationContext Context = new(ValidationStage.Standardization, new DateOnly(2026, 9, 30))
    {
        Standard = "SDTMIG",
        StandardVersion = "3.4",
    };

    private static readonly RuleRecord[] Records =
    [
        new("VITALS", "row-1", new Dictionary<string, RuleValue>(StringComparer.Ordinal) { ["SYS"] = RuleValue.FromNumber(90m), ["DIA"] = RuleValue.FromNumber(100m) }),
        new("VITALS", "row-2", new Dictionary<string, RuleValue>(StringComparer.Ordinal) { ["SYS"] = RuleValue.FromNumber(130m), ["DIA"] = RuleValue.FromNumber(85m) }),
    ];

    /// <summary>
    /// Validates K-RULE-1: a new rule pack and a changed rule reach a running platform as library item versions. A
    /// draft does not load, release needs an Approved attestation over the pack's content hash (a Reviewed one is rejected),
    /// and the released version evaluates immediately.
    /// @req:PRD-CRF-004
    /// </summary>
    [Fact(DisplayName = "RulePack_DraftedAndReleasedAtRuntime_LoadsAndEvaluates")]
    [GxPRequirement("PRD-CRF-004")]
    public async Task RulePackDraftedAndReleasedAtRuntimeLoadsAndEvaluates()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var pack = SampleRulePacks.Sample();

        var v1 = await DraftAsync(tenant, RulePackJson.ToJson(pack), ct);
        (await fixture.RulePacks.LoadAsync(tenant, v1.Id, ct)).Error!.Code.Should().Be(RuleErrors.PackNotReleased.Code);

        var reviewed = await SignAsync(tenant, v1, AttestationMeaning.Reviewed, ct);
        (await SendAsync(new ApproveLibraryItem(tenant, v1.Id, reviewed, Key(), Reason), ct)).Error!.Code
            .Should().Be(StandardsErrors.AttestationRejected.Code);

        (await fixture.RulePacks.LoadAsync(tenant, v1.Id, ct)).Error!.Code.Should().Be(RuleErrors.PackNotReleased.Code);
        await ApproveAsync(tenant, v1, ct);

        var loaded = (await fixture.RulePacks.LoadAsync(tenant, v1.Id, ct)).Pack!;
        loaded.Version.Should().Be(1);
        loaded.ContentHash.Should().Be(v1.ContentHash);
        fixture.RuleEvaluator.Evaluate(loaded.Pack, Context, Records).Select(v => (v.RecordKey, v.RuleId))
            .Should().Equal(("row-1", "R1"), ("row-1", "R2"));

        // Change a rule at runtime: R2's threshold moves from 100 to 140, as version 2 of the pack.
        var changed = pack with
        {
            Rules = [.. pack.Rules.Select(r => r.Id == "R2" ? r with { Version = 2, Condition = Condition("""{"op":"<","args":[{"var":"SYS"},140]}""") } : r)],
        };
        var v2 = await DraftAsync(tenant, RulePackJson.ToJson(changed), ct);
        await ApproveAsync(tenant, v2, ct);

        var reloaded = (await fixture.RulePacks.LoadAsync(tenant, v2.Id, ct)).Pack!;
        reloaded.Version.Should().Be(2);
        fixture.RuleEvaluator.Evaluate(reloaded.Pack, Context, Records).Select(v => (v.RecordKey, v.RuleId, v.RuleVersion))
            .Should().Equal(("row-1", "R1", 3), ("row-1", "R2", 2), ("row-2", "R2", 2));
    }

    /// <summary>
    /// Validates that drafting runs the rule pack validator: invalid content is refused before it can be approved,
    /// and a library item of another kind is not a rule pack.
    /// </summary>
    [Fact(DisplayName = "RulePack_InvalidContent_IsRefusedWhenDrafted")]
    public async Task RulePackInvalidContentIsRefusedWhenDrafted()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        using var invalid = JsonDocument.Parse("""{"authority":"CDISC","title":"No rules","rules":[]}""");

        var result = await SendAsync(new DraftLibraryItem(
            tenant, LibraryItemId.New(), LibraryItemKind.RulePack, PackKey, "Invalid", invalid.RootElement, [], Key(), Reason), ct);

        result.Error!.Code.Should().Be(StandardsErrors.LibraryItemInvalid.Code);
        result.Error.Message.Should().Contain("a pack has between 1 and");

        using var subset = JsonDocument.Parse("""{"codelist":"C66770"}""");
        var other = await SendAsync(new DraftLibraryItem(
            tenant, LibraryItemId.New(), LibraryItemKind.CodelistSubset, "units", "Units", subset.RootElement, [], Key(), Reason), ct);
        (await fixture.RulePacks.LoadAsync(tenant, other.Value!.Id, ct)).Error!.Code.Should().Be(RuleErrors.NotARulePack.Code);
        (await fixture.RulePacks.LoadAsync(tenant, LibraryItemId.New(), ct)).Error!.Code.Should().Be(RuleErrors.PackUnknown.Code);
    }

    /// <summary>
    /// Validates that packs are pinned per study: a study loads the pack version it pinned, a newer release does not
    /// change it, and the pinned pack still loads after it is deprecated.
    /// </summary>
    [Fact(DisplayName = "RulePack_PinnedByStudy_LoadsThePinnedVersionEvenWhenDeprecated")]
    public async Task RulePackPinnedByStudyLoadsThePinnedVersionEvenWhenDeprecated()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(graph.Tenant, graph.Sponsor, graph.Study);
        var tenant = graph.Tenant.Id;
        (await fixture.RulePacks.LoadForStudyAsync(tenant, graph.Study.Id, ct)).Should().BeEmpty();

        var sdtmig = (await SendAsync(new RegisterStandardVersion(
            tenant, StandardVersionId.New(), StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package, null, Key(), Reason), ct)).Value;
        var sdtmigHash = (await fixture.Standards.FindStandardVersionAsync(tenant, sdtmig, ct))!.ContentHash;
        var standardApproval = await SignAsync(tenant, new AttestationTarget(NodeKinds.StandardVersion, sdtmig.Value, sdtmigHash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, sdtmig, standardApproval, Key(), Reason), ct)).IsSuccess.Should().BeTrue();
        var v1 = await DraftAsync(tenant, RulePackJson.ToJson(SampleRulePacks.Sample()), ct);
        await ApproveAsync(tenant, v1, ct);
        using var units = JsonDocument.Parse("""{"codelist":"C66770"}""");
        var subset = (await SendAsync(new DraftLibraryItem(
            tenant, LibraryItemId.New(), LibraryItemKind.CodelistSubset, "units", "Units", units.RootElement, [], Key(), Reason), ct)).Value!;
        await ApproveAsync(tenant, subset, ct);

        (await SendAsync(new DeclareStudyStandards(
            tenant, graph.Study.Id, StudyStandardsProfileId.New(), [sdtmig], [], [v1.Id, subset.Id], Key(), Reason), ct)).IsSuccess.Should().BeTrue();

        var v2 = await DraftAsync(tenant, RulePackJson.ToJson(SampleRulePacks.Sample() with { Title = "Sample pack, revised" }), ct);
        await ApproveAsync(tenant, v2, ct);
        var deprecation = await SignAsync(tenant, v1, AttestationMeaning.Responsibility, ct);
        (await SendAsync(new DeprecateLibraryItem(tenant, v1.Id, deprecation, Key(), Reason), ct)).IsSuccess.Should().BeTrue();

        var pinned = await fixture.RulePacks.LoadForStudyAsync(tenant, graph.Study.Id, ct);
        pinned.Should().ContainSingle();
        pinned[0].Id.Should().Be(v1.Id);
        pinned[0].Version.Should().Be(1);
        pinned[0].Status.Should().Be(GovernanceStatus.Deprecated);
        pinned[0].Pack.Title.Should().Be("Sample pack");
    }

    private async Task<TenantId> ProvisionTenantAsync(CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var command = new ProvisionTenant(TenantId.New(), $"T-{suffix}", $"Tenant {suffix}", Key(), Reason);
        (await SendAsync(command, ct)).IsSuccess.Should().BeTrue();
        return command.TenantId;
    }

    private async Task<LibraryItemVersion> DraftAsync(TenantId tenant, JsonElement content, CancellationToken ct)
    {
        var result = await SendAsync(new DraftLibraryItem(
            tenant, LibraryItemId.New(), LibraryItemKind.RulePack, PackKey, "Sample vitals rules", content, [], Key(), Reason), ct);
        result.Error.Should().BeNull();
        return result.Value!;
    }

    /// <summary>Releases a library item version: the fixture's person signs Approved over its content hash, then approves it.</summary>
    private async Task ApproveAsync(TenantId tenant, LibraryItemVersion item, CancellationToken ct)
    {
        var approval = await SignAsync(tenant, item, AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveLibraryItem(tenant, item.Id, approval, Key(), Reason), ct)).IsSuccess.Should().BeTrue();
    }

    private Task<AttestationId> SignAsync(TenantId tenant, LibraryItemVersion item, AttestationMeaning meaning, CancellationToken ct) =>
        SignAsync(tenant, new AttestationTarget(NodeKinds.LibraryItem, item.Id.Value, item.ContentHash), meaning, ct);

    private async Task<AttestationId> SignAsync(TenantId tenant, AttestationTarget target, AttestationMeaning meaning, CancellationToken ct)
    {
        var result = await SendAsync(
            new Attest(tenant, AttestationId.New(), meaning, target, fixture.Clock.UtcNow.AddMinutes(-1), "pwd+otp", Key(), $"Signing as {meaning}"), ct);
        result.Error.Should().BeNull();
        return result.Value;
    }

    private static RuleExpression Condition(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RuleExpressionJson.Parse(document.RootElement);
    }

    private Task<CommandResult<T>> SendAsync<T>(ICommand<T> command, CancellationToken ct) =>
        fixture.Sender.SendAsync(command, cancellationToken: ct);

    private static string Key() => $"key-{Guid.NewGuid():N}";
}
