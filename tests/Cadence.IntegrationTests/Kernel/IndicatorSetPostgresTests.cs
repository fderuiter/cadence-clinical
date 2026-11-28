using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Indicators;
using Cadence.Rules.Packs;
using Cadence.Standards;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Rules;

using FluentAssertions;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Study Pulse indicators as governed data on PostgreSQL (ADR-2488): an indicator set is drafted, released with an
/// attestation and pinned by a study like a rule pack, and its values are computed by the rule evaluator. Skipped
/// unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IndicatorSetPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Indicator set test";
    private const string SetKey = "sample-pulse";

    /// <summary>
    /// A released indicator set pinned by a study computes its key risk indicators and flags a breached quality
    /// tolerance limit, naming the indicator version and the objects behind the value. A draft does not load, and
    /// the study's rule packs and indicator sets load separately from the same pins.
    /// @req:PRD-CTMS-007
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-CTMS-007")]
    public async Task IndicatorSet_ReleasedAndPinned_ComputesValuesAndFlagsABreachedLimit()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(graph.Tenant, graph.Sponsor, graph.Study);
        var tenant = graph.Tenant.Id;

        var set = await DraftAsync(tenant, LibraryItemKind.IndicatorSet, SetKey, IndicatorSetJson.ToJson(SampleIndicatorSets.Pulse()), ct);
        (await fixture.IndicatorSets.LoadAsync(tenant, set.Id, ct)).Error!.Code.Should().Be(IndicatorErrors.SetNotReleased.Code);
        await ApproveAsync(tenant, set, ct);
        var pack = await DraftAsync(tenant, LibraryItemKind.RulePack, "sample-rules", RulePackJson.ToJson(SampleRulePacks.Sample()), ct);
        await ApproveAsync(tenant, pack, ct);
        var sdtmig = await ApprovedStandardAsync(tenant, ct);
        (await SendAsync(new DeclareStudyStandards(
            tenant, graph.Study.Id, StudyStandardsProfileId.New(), [sdtmig], [], [set.Id, pack.Id], Key(), Reason), ct)).IsSuccess.Should().BeTrue();

        var pinned = await fixture.IndicatorSets.LoadForStudyAsync(tenant, graph.Study.Id, ct);
        (await fixture.RulePacks.LoadForStudyAsync(tenant, graph.Study.Id, ct)).Should().ContainSingle().Which.Id.Should().Be(pack.Id);
        pinned.Should().ContainSingle();
        pinned[0].ContentHash.Should().Be(set.ContentHash);

        var findings = new[]
        {
            SampleIndicatorSets.Record(new NodeRef(NodeKinds.ValidationFinding, Guid.NewGuid()), ("STATUS", RuleValue.FromText("Open")), ("SEVERITY", RuleValue.FromText("Error"))),
            SampleIndicatorSets.Record(new NodeRef(NodeKinds.ValidationFinding, Guid.NewGuid()), ("STATUS", RuleValue.FromText("Resolved")), ("SEVERITY", RuleValue.FromText("Error"))),
        };
        var results = fixture.Indicators.Evaluate(
            pinned[0],
            new IndicatorContext(NodeRef.For(graph.Study.Id), new DateOnly(2026, 9, 30)),
            new Dictionary<string, IReadOnlyList<IndicatorRecord>>(StringComparer.Ordinal)
            {
                [SampleIndicatorSets.Enrollments] = SampleIndicatorSets.EnrollmentRecords("Enrolled", "ScreenFailed", "Randomized", "Enrolled"),
                [SampleIndicatorSets.Findings] = findings,
            });

        results.Select(r => (r.Indicator.IndicatorId, r.Value, r.Breached)).Should().Equal(
            ("pulse.enrolled", 3m, false),
            ("pulse.screen_failure_rate", 25m, false),
            ("qtl.open_error_findings", 1m, true));
        var qtl = results[2];
        qtl.Kind.Should().Be(IndicatorKind.Qtl);
        qtl.Indicator.SetId.Should().Be(set.Id);
        qtl.Contributors.Should().Equal(findings[0].Node);
        qtl.Explanation.Producer.Should().Be("quality.indicator/qtl.open_error_findings@1");
    }

    /// <summary>
    /// A study's Pulse reads its live enrollments and validation findings from the kernel sources at an as-of and
    /// counts only what the reader may see: an enrollment at a site hidden from the reader is neither counted nor listed,
    /// and the value says it is partial without saying how many were left out (ADR-2488 §4.7).
    /// @req:PRD-CTMS-007
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-CTMS-007")]
    public async Task StudyPulse_ReadsLiveKernelPopulationsAndCountsOnlyWhatTheReaderMaySee()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(graph.Tenant, graph.Sponsor, graph.Institution, graph.Site, graph.Study, graph.StudySite);
        var tenant = graph.Tenant.Id;
        var (set, _, sdtmig) = await PinSampleSetAndPackAsync(tenant, graph.Study.Id, ct);

        var enrolled = await ScreenAsync(tenant, graph.StudySite.Id, EnrollmentStatus.Enrolled, ct);
        await ScreenAsync(tenant, graph.StudySite.Id, EnrollmentStatus.ScreenFailed, ct);
        await ScreenAsync(tenant, graph.StudySite.Id, null, ct);
        await ScreenAsync(tenant, graph.StudySite.Id, null, ct);
        var hidden = await ScreenAsync(tenant, graph.StudySite.Id, EnrollmentStatus.Enrolled, ct);
        fixture.Visibility.Masked[NodeRef.For(hidden)] = MaskReason.NotPermitted;
        await ValidateAsync(tenant, graph.Study.Id, sdtmig, ct);
        var asOf = fixture.Clock.Advance(TimeSpan.FromSeconds(1));

        var result = await fixture.StudyIndicators.EvaluateAsync(tenant, Actor.Person("monitor@cadence.test"), graph.Study.Id, asOf, ct);

        result.Error.Should().BeNull();
        var values = result.Value!.Indicators;
        values.Select(v => (v.Result.Indicator.IndicatorId, v.Result.Value, v.Result.Breached, v.Partial)).Should().Equal(
            ("pulse.enrolled", 1m, false, true),
            ("pulse.screen_failure_rate", 25m, false, true),
            ("qtl.open_error_findings", 1m, true, false));
        values[0].Result.Indicator.SetId.Should().Be(set);
        values[0].Result.Contributors.Should().Equal(NodeRef.For(enrolled));
        values[0].HiddenReasons.Should().Equal(MaskReason.NotPermitted);
        values[2].Result.Contributors.Should().ContainSingle().Which.Kind.Should().Be(NodeKinds.ValidationFinding);

        var before = await fixture.StudyIndicators.EvaluateAsync(
            tenant, Actor.Person("monitor@cadence.test"), graph.Study.Id, fixture.Clock.UtcNow.AddYears(-1), ct);
        before.Value!.Indicators[0].Result.Population.Should().Be(0, "no enrollment had a status a year earlier");
    }

    /// <summary>Drafting runs the indicator set validator, so invalid content is refused before it can be approved.</summary>
    [Fact]
    public async Task IndicatorSet_InvalidContent_IsRefusedWhenDrafted()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(graph.Tenant, graph.Sponsor, graph.Study);
        using var invalid = JsonDocument.Parse("""{"authority":"Sponsor","title":"Empty","indicators":[]}""");

        var result = await SendAsync(new DraftLibraryItem(
            graph.Tenant.Id, LibraryItemId.New(), LibraryItemKind.IndicatorSet, SetKey, "Invalid", invalid.RootElement, [], Key(), Reason), ct);

        result.Error!.Code.Should().Be(StandardsErrors.LibraryItemInvalid.Code);
        result.Error.Message.Should().Contain("a set has between 1 and");
        (await fixture.IndicatorSets.LoadAsync(graph.Tenant.Id, LibraryItemId.New(), ct)).Error!.Code.Should().Be(IndicatorErrors.SetUnknown.Code);
    }

    private async Task<(LibraryItemId Set, LibraryItemId Pack, StandardVersionId Sdtmig)> PinSampleSetAndPackAsync(
        TenantId tenant, StudyId study, CancellationToken ct)
    {
        var set = await DraftAsync(tenant, LibraryItemKind.IndicatorSet, SetKey, IndicatorSetJson.ToJson(SampleIndicatorSets.Pulse()), ct);
        await ApproveAsync(tenant, set, ct);
        var pack = await DraftAsync(tenant, LibraryItemKind.RulePack, "sample-rules", RulePackJson.ToJson(SampleRulePacks.Sample()), ct);
        await ApproveAsync(tenant, pack, ct);
        var sdtmig = await ApprovedStandardAsync(tenant, ct);
        (await SendAsync(new DeclareStudyStandards(
            tenant, study, StudyStandardsProfileId.New(), [sdtmig], [], [set.Id, pack.Id], Key(), Reason), ct)).Error.Should().BeNull();
        return (set.Id, pack.Id, sdtmig);
    }

    /// <summary>Screens a new participant at the study site and, when <paramref name="to"/> is given, moves them there.</summary>
    private async Task<EnrollmentId> ScreenAsync(TenantId tenant, StudySiteId studySite, EnrollmentStatus? to, CancellationToken ct)
    {
        var participant = (await SendAsync(new RegisterParticipant(tenant, ParticipantId.New(), Key(), "Participant consented"), ct)).Value;
        var screened = await SendAsync(new ScreenParticipant(
            tenant, EnrollmentId.New(), participant, studySite, SubjectCode.From($"SUBJ-{Guid.NewGuid():N}"[..13]), Tick(), Key(), "Screening visit"), ct);
        screened.Error.Should().BeNull();
        var enrollment = screened.Value!.EnrollmentId;
        if (to is { } status)
        {
            (await SendAsync(new ChangeEnrollmentStatus(tenant, enrollment, status, Tick(), Key(), $"Moved to {status}"), ct)).Error.Should().BeNull();
        }

        return enrollment;
    }

    /// <summary>Validates one vitals record the sample pack flags with an Error finding.</summary>
    private async Task ValidateAsync(TenantId tenant, StudyId study, StandardVersionId sdtmig, CancellationToken ct)
    {
        Tick();
        var vitals = new RuleRecord("VITALS", "row-1", new Dictionary<string, RuleValue>(StringComparer.Ordinal)
        {
            ["SYS"] = RuleValue.FromNumber(90m),
            ["DIA"] = RuleValue.FromNumber(100m),
        });
        var request = new ValidationRequest(tenant, study, ValidationStage.Standardization, new DateOnly(2026, 9, 30), [vitals], Key(), Reason)
        {
            Standard = "SDTMIG",
            StandardVersion = "3.4",
            Inputs = [NodeRef.For(sdtmig, 2)],
            Datasets = ["VITALS"],
        };
        (await fixture.Validation.RunAsync(request, ct)).Error.Should().BeNull();
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private async Task<StandardVersionId> ApprovedStandardAsync(TenantId tenant, CancellationToken ct)
    {
        var sdtmig = (await SendAsync(new RegisterStandardVersion(
            tenant, StandardVersionId.New(), StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package, null, Key(), Reason), ct)).Value;
        var hash = (await fixture.Standards.FindStandardVersionAsync(tenant, sdtmig, ct))!.ContentHash;
        var approval = await SignAsync(tenant, new AttestationTarget(NodeKinds.StandardVersion, sdtmig.Value, hash), ct);
        (await SendAsync(new ApproveStandardVersion(tenant, sdtmig, approval, Key(), Reason), ct)).IsSuccess.Should().BeTrue();
        return sdtmig;
    }

    private async Task<LibraryItemVersion> DraftAsync(TenantId tenant, LibraryItemKind kind, string key, JsonElement content, CancellationToken ct)
    {
        var result = await SendAsync(new DraftLibraryItem(tenant, LibraryItemId.New(), kind, key, key, content, [], Key(), Reason), ct);
        result.Error.Should().BeNull();
        return result.Value!;
    }

    private async Task ApproveAsync(TenantId tenant, LibraryItemVersion item, CancellationToken ct)
    {
        var approval = await SignAsync(tenant, new AttestationTarget(NodeKinds.LibraryItem, item.Id.Value, item.ContentHash), ct);
        (await SendAsync(new ApproveLibraryItem(tenant, item.Id, approval, Key(), Reason), ct)).IsSuccess.Should().BeTrue();
    }

    private async Task<AttestationId> SignAsync(TenantId tenant, AttestationTarget target, CancellationToken ct)
    {
        var result = await SendAsync(
            new Attest(tenant, AttestationId.New(), AttestationMeaning.Approved, target, fixture.Clock.UtcNow.AddMinutes(-1), "pwd+otp", Key(), "Signing as Approved"), ct);
        result.Error.Should().BeNull();
        return result.Value;
    }

    private Task<CommandResult<T>> SendAsync<T>(ICommand<T> command, CancellationToken ct) =>
        fixture.Sender.SendAsync(command, cancellationToken: ct);

    private static string Key() => $"key-{Guid.NewGuid():N}";
}
