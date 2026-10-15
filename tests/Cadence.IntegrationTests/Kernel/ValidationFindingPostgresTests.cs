using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.Persistence.Standards;
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

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Validation runs and findings on PostgreSQL (spec §10.3, #4812): a study's pinned rule packs run through
/// <see cref="ValidationRunner"/>, findings keep their identity across runs and are resolved by the run that no longer
/// detects them (K-FIND-1), acceptance and explanation need a matching attestation (K-FIND-2), and runs link to their
/// rule packs and inputs in lineage (K-FIND-3). Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ValidationFindingPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Validation test";
    private const string PackKey = "sample-vitals-rules";

    /// <summary>
    /// Validates spec §14 test 10 (K-FIND-1): re-running validation after a mapping fix resolves the finding with the
    /// run as evidence instead of duplicating it, and a problem that is still there keeps its finding.
    /// @req:PRD-CRF-004
    /// </summary>
    [Fact(DisplayName = "Rerun_AfterMappingFix_ResolvesTheFindingInsteadOfDuplicatingIt")]
    [GxPRequirement("PRD-CRF-004")]
    public async Task RerunAfterMappingFixResolvesTheFindingInsteadOfDuplicatingIt()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, pack, sdtmig) = await StudyWithPackAsync(ct);

        var first = await RunAsync(tenant, study, [Vitals("row-1", 90m, 100m), Vitals("row-2", 130m, 85m)], ct, [sdtmig]);
        first.Should().BeEquivalentTo(new { Detected = 2, New = 2, Resolved = 0 });

        var open = await fixture.Findings.ListAsync(tenant, new FindingQuery(study), ct);
        open.Total.Should().Be(2);
        open.Findings.Select(f => (f.RuleId, f.Severity, f.Status, f.Blocks)).Should().Equal(
            ("R1", ValidationSeverity.Error, FindingStatus.Open, true),
            ("R2", ValidationSeverity.Warning, FindingStatus.Open, false));
        var systolic = open.Findings[0];
        systolic.Target.Should().Be(new FindingTarget(FindingTargetKind.DatasetRecord, "VITALS", "row-1"));
        systolic.Observed.Should().Equal(new ObservedVariable("SYS", "90"), new ObservedVariable("DIA", "100"));
        systolic.RulePack.Should().Be(pack);
        systolic.Standard.Should().Be("SDTMIG");

        // The same data again: the same findings, detected by the second run too.
        var second = await RunAsync(tenant, study, [Vitals("row-1", 90m, 100m), Vitals("row-2", 130m, 85m)], ct);
        second.Should().BeEquivalentTo(new { Detected = 2, New = 0, Resolved = 0 });
        var again = (await fixture.Findings.FindAsync(tenant, systolic.Id, ct))!;
        again.FirstDetectedRun.Should().Be(first.RunId);
        again.LastDetectedRun.Should().Be(second.RunId);

        // The mapping fix: row-1 now maps the values the right way round, so R1 no longer fires; R2 (SYS < 100) still does.
        var fixedRun = await RunAsync(tenant, study, [Vitals("row-1", 95m, 90m), Vitals("row-2", 130m, 85m)], ct);
        fixedRun.Should().BeEquivalentTo(new { Detected = 1, New = 0, Resolved = 1 });

        var resolved = (await fixture.Findings.FindAsync(tenant, systolic.Id, ct))!;
        resolved.Status.Should().Be(FindingStatus.Resolved);
        resolved.ResolvedBy.Should().Be(fixedRun.RunId);
        resolved.Explain().Conditions[0].Evidence.Should().Equal(NodeRef.For(fixedRun.RunId));
        (await fixture.Findings.ListAsync(tenant, new FindingQuery(study) { BlockingOnly = true }, ct)).Total.Should().Be(0);
        (await fixture.Findings.ListAsync(tenant, new FindingQuery(study), ct)).Total.Should().Be(2, "the fix resolved a finding, it did not add one");

        var run = (await fixture.Findings.FindRunAsync(tenant, fixedRun.RunId, ct))!;
        run.RulePacks.Should().ContainSingle().Which.Should().BeEquivalentTo(new { LibraryItemId = pack, Key = PackKey, Version = 1 });
        run.Datasets.Should().Equal("VITALS");
        run.Detected.Should().Be(1);
        run.RecordedBy.Should().Be(KernelPostgresFixture.ActorId);
    }

    /// <summary>
    /// Validates K-FIND-3: a run links to the rule pack version that defined it and to the specifications it validated,
    /// and a decision on a finding derives from the run it was made on.
    /// </summary>
    [Fact(DisplayName = "Runs_AndDecisions_LinkToTheirInputsInLineage")]
    public async Task RunsAndDecisionsLinkToTheirInputsInLineage()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, pack, sdtmig) = await StudyWithPackAsync(ct);
        var sdtmigVersion = (await fixture.Standards.FindStandardVersionAsync(tenant, sdtmig, ct))!;

        var outcome = await RunAsync(tenant, study, [Vitals("row-1", 90m, 100m)], ct, [sdtmig]);
        var finding = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study), ct)).Findings[0];
        (await SendAsync(new InvestigateFinding(tenant, finding.Id, Key(), Reason), ct)).Error.Should().BeNull();

        await using var db = fixture.CreateContext(tenant);
        var edges = await db.Set<LineageEdgeRecord>().AsNoTracking().ToListAsync(ct);
        edges.Should().ContainSingle(e => e.ToId == outcome.RunId.Value && e.FromId == pack.Value && e.Kind == LineageEdgeKind.DefinedBy)
            .Which.FromKind.Should().Be(NodeKinds.LibraryItem);
        edges.Should().ContainSingle(e => e.ToId == outcome.RunId.Value && e.FromId == sdtmig.Value && e.Kind == LineageEdgeKind.ValidatedBy);
        edges.Should().ContainSingle(e => e.ToId == finding.Id.Value && e.FromId == outcome.RunId.Value && e.Kind == LineageEdgeKind.DerivedFrom)
            .Which.Should().BeEquivalentTo(new { FromKind = NodeKinds.ValidationRun, FromVersion = 1L, ToKind = NodeKinds.ValidationFinding, ToVersion = 1L });
        sdtmigVersion.Id.Should().Be(sdtmig);

        // Runs and findings sit under their study in the zoom hierarchy, so the study's activity includes them.
        var navigator = new KernelNavigator(new KernelScopeGraph(fixture.TenantContexts));
        (await navigator.GetZoomPathAsync(tenant, NodeRef.For(finding.Id), ct))!.Select(z => z.Level)
            .Should().Equal(NodeKinds.Tenant, NodeKinds.Study, NodeKinds.ValidationFinding);
        (await navigator.ResolveAsync(tenant, NodeRef.For(outcome.RunId), ct))!.Should().BeEquivalentTo(new
        {
            Ref = NodeRef.For(outcome.RunId, 1),
            Title = "Standardization validation run",
        });
        (await navigator.ResolveAsync(tenant, NodeRef.For(finding.Id), ct))!.Ref.Should().Be(NodeRef.For(finding.Id, 1));
    }

    /// <summary>
    /// Validates K-FIND-2: accepting or explaining a finding needs an attestation by the acting person over the
    /// decision's content (finding, run, decision and text) with the right meaning; a mismatched one is refused. An
    /// explained finding no longer blocks and is listed with its explanation for the reviewer guide.
    /// </summary>
    [Fact(DisplayName = "AcceptAndExplain_RequireAMatchingAttestation")]
    public async Task AcceptAndExplainRequireAMatchingAttestation()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, _, _) = await StudyWithPackAsync(ct);
        var run = await RunAsync(tenant, study, [Vitals("row-1", 90m, 100m)], ct);
        var findings = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study), ct)).Findings;
        var error = findings.Single(f => f.RuleId == "R1");
        var warning = findings.Single(f => f.RuleId == "R2");
        const string Explanation = "Values were collected with the cuff reversed; confirmed with the site.";

        var wrongMeaning = await SignAsync(tenant, error.Id, run.RunId, FindingStatus.Explained, Explanation, AttestationMeaning.Reviewed, ct);
        (await SendAsync(new ExplainFinding(tenant, error.Id, Explanation, wrongMeaning, Key(), Reason), ct)).Error!.Code
            .Should().Be(ValidationErrors.AttestationRejected.Code);

        var otherText = await SignAsync(tenant, error.Id, run.RunId, FindingStatus.Explained, "Something else.", AttestationMeaning.Approved, ct);
        (await SendAsync(new ExplainFinding(tenant, error.Id, Explanation, otherText, Key(), Reason), ct)).Error!.Code
            .Should().Be(ValidationErrors.AttestationRejected.Code);

        var approved = await SignAsync(tenant, error.Id, run.RunId, FindingStatus.Explained, Explanation, AttestationMeaning.Approved, ct);
        (await SendAsync(new ExplainFinding(tenant, error.Id, Explanation, approved, Key(), Reason), ct)).Error.Should().BeNull();

        var explained = (await fixture.Findings.FindAsync(tenant, error.Id, ct))!;
        explained.Status.Should().Be(FindingStatus.Explained);
        explained.Blocks.Should().BeFalse();
        explained.Decision.Should().BeEquivalentTo(new { Status = FindingStatus.Explained, DecidedBy = KernelPostgresFixture.ActorId, Text = Explanation, DecidedOnRun = run.RunId, AttestationId = approved });
        explained.Explain().Steps[^1].Evidence.Should().Equal(NodeRef.For(approved));

        var reviewerGuide = await fixture.Findings.ListAsync(tenant, new FindingQuery(study) { Statuses = [FindingStatus.Explained] }, ct);
        reviewerGuide.Findings.Should().ContainSingle().Which.Decision!.Text.Should().Be(Explanation);

        var responsibility = await SignAsync(tenant, warning.Id, run.RunId, FindingStatus.Accepted, "Known low reading.", AttestationMeaning.Responsibility, ct);
        (await SendAsync(new AcceptFinding(tenant, warning.Id, "Known low reading.", responsibility, Key(), Reason), ct)).Error.Should().BeNull();
        (await fixture.Findings.FindAsync(tenant, warning.Id, ct))!.Status.Should().Be(FindingStatus.Accepted);

        // A later run that still detects them leaves the decisions in force.
        await RunAsync(tenant, study, [Vitals("row-1", 90m, 100m)], ct);
        (await fixture.Findings.FindAsync(tenant, error.Id, ct))!.Status.Should().Be(FindingStatus.Explained);
    }

    /// <summary>
    /// Validates the decision workflow: investigate, classify and reopen follow the derived status, an unknown finding
    /// is refused, and a resolved then recurring finding reopens even though it was accepted before.
    /// </summary>
    [Fact(DisplayName = "Decisions_FollowTheDerivedStatus_AndARecurrenceReopens")]
    public async Task DecisionsFollowTheDerivedStatusAndARecurrenceReopens()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, _, _) = await StudyWithPackAsync(ct);
        var run = await RunAsync(tenant, study, [Vitals("row-1", 90m, 100m)], ct);
        var finding = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study) { Severity = ValidationSeverity.Error }, ct)).Findings.Single();

        (await SendAsync(new InvestigateFinding(tenant, ValidationFindingId.New(), Key(), Reason), ct)).Error!.Code.Should().Be(ValidationErrors.FindingUnknown.Code);
        (await SendAsync(new ClassifyFinding(tenant, finding.Id, FindingClassification.MappingIssue, Key(), Reason), ct)).Error.Should().BeNull();
        (await SendAsync(new InvestigateFinding(tenant, finding.Id, Key(), Reason), ct)).Error.Should().BeNull();
        (await SendAsync(new InvestigateFinding(tenant, finding.Id, Key(), Reason), ct)).Error!.Code.Should().Be(ValidationErrors.InvalidTransition.Code);
        (await fixture.Findings.FindAsync(tenant, finding.Id, ct))!.Should().BeEquivalentTo(new
        {
            Status = FindingStatus.Investigating,
            Classification = (FindingClassification?)FindingClassification.MappingIssue,
            Blocks = true,
        });
        (await SendAsync(new ReopenFinding(tenant, finding.Id, Key(), Reason), ct)).Error.Should().BeNull();

        var attestation = await SignAsync(tenant, finding.Id, run.RunId, FindingStatus.Accepted, "Accepted for now.", AttestationMeaning.Responsibility, ct);
        (await SendAsync(new AcceptFinding(tenant, finding.Id, "Accepted for now.", attestation, Key(), Reason), ct)).Error.Should().BeNull();

        await RunAsync(tenant, study, [Vitals("row-1", 100m, 90m)], ct);
        (await fixture.Findings.FindAsync(tenant, finding.Id, ct))!.Status.Should().Be(FindingStatus.Resolved);
        (await SendAsync(new ReopenFinding(tenant, finding.Id, Key(), Reason), ct)).Error!.Code.Should().Be(ValidationErrors.InvalidTransition.Code);

        await RunAsync(tenant, study, [Vitals("row-1", 90m, 100m)], ct);
        var recurred = (await fixture.Findings.FindAsync(tenant, finding.Id, ct))!;
        recurred.Status.Should().Be(FindingStatus.Open);
        recurred.Decision.Should().BeNull();
        recurred.Classification.Should().Be(FindingClassification.MappingIssue);
    }

    /// <summary>Validates that a run needs a released rule pack and a study of the tenant.</summary>
    [Fact(DisplayName = "Record_NeedsAReleasedPackAndAKnownStudy")]
    public async Task RecordNeedsAReleasedPackAndAKnownStudy()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, pack, _) = await StudyWithPackAsync(ct);
        var draft = await DraftAsync(tenant, ct);

        (await SendAsync(Record(tenant, study, draft.Id), ct)).Error!.Code.Should().Be(ValidationErrors.RulePackNotUsable.Code);
        (await SendAsync(Record(tenant, study, LibraryItemId.New()), ct)).Error!.Code.Should().Be(ValidationErrors.RulePackNotUsable.Code);
        (await SendAsync(Record(tenant, StudyId.New(), pack), ct)).Error!.Code.Should().Be(ValidationErrors.StudyUnknown.Code);

        var command = Record(tenant, study, pack);
        (await SendAsync(command, ct)).Error.Should().BeNull();
        (await SendAsync(command with { IdempotencyKey = Key() }, ct)).Error!.Code.Should().Be(KernelErrors.AlreadyExists.Code);
    }

    /// <summary>
    /// Validates K-ID-2 for validation: another tenant sees neither the runs nor the findings, and cannot decide on them.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "Findings_AreIsolatedByTenant")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task FindingsAreIsolatedByTenant()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, _, _) = await StudyWithPackAsync(ct);
        var run = await RunAsync(tenant, study, [Vitals("row-1", 90m, 100m)], ct);
        var finding = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study), ct)).Findings[0];
        var (other, _, _, _) = await StudyWithPackAsync(ct);

        (await fixture.Findings.FindAsync(other, finding.Id, ct)).Should().BeNull();
        (await fixture.Findings.FindRunAsync(other, run.RunId, ct)).Should().BeNull();
        (await fixture.Findings.ListAsync(other, new FindingQuery(study), ct)).Total.Should().Be(0);
        (await SendAsync(new InvestigateFinding(other, finding.Id, Key(), Reason), ct)).Error!.Code.Should().Be(ValidationErrors.FindingUnknown.Code);
    }

    /// <summary>Validates that a decisions record keeps its finding, study and author in the database (SQLSTATE KS001).</summary>
    [Fact(DisplayName = "DecisionRecord_Identity_CannotBeChangedInTheDatabase")]
    public async Task DecisionRecordIdentityCannotBeChangedInTheDatabase()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, _, _) = await StudyWithPackAsync(ct);
        await RunAsync(tenant, study, [Vitals("row-1", 90m, 100m)], ct);
        var finding = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study), ct)).Findings[0];
        (await SendAsync(new InvestigateFinding(tenant, finding.Id, Key(), Reason), ct)).Error.Should().BeNull();

        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"UPDATE kernel.validation_findings SET created_by = 'someone-else' WHERE id = '{finding.Id}'", connection);
        var act = async () => await command.ExecuteNonQueryAsync(ct);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(ImmutableColumnsSql.ViolationSqlState);
    }

    /// <summary>Records one validation run with a detection and an input, for tests of the validation tables.</summary>
    internal static async Task SeedRunAsync(KernelPostgresFixture fixture, CancellationToken ct)
    {
        var tests = new ValidationFindingPostgresTests(fixture);
        var (tenant, study, _, sdtmig) = await tests.StudyWithPackAsync(ct);
        await tests.RunAsync(tenant, study, [Vitals("row-1", 90m, 100m)], ct, [sdtmig]);
    }

    /// <summary>A tenant with a study whose standards profile pins SDTMIG 3.4 and one released rule pack.</summary>
    private async Task<(TenantId Tenant, StudyId Study, LibraryItemId Pack, StandardVersionId Sdtmig)> StudyWithPackAsync(CancellationToken ct)
    {
        var graph = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(graph.Tenant, graph.Sponsor, graph.Study);
        var tenant = graph.Tenant.Id;

        var sdtmig = (await SendAsync(new RegisterStandardVersion(
            tenant, StandardVersionId.New(), StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package, null, Key(), Reason), ct)).Value;
        var hash = (await fixture.Standards.FindStandardVersionAsync(tenant, sdtmig, ct))!.ContentHash;
        var standardApproval = await SignAsync(tenant, new AttestationTarget(NodeKinds.StandardVersion, sdtmig.Value, hash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, sdtmig, standardApproval, Key(), Reason), ct)).Error.Should().BeNull();

        var pack = await DraftAsync(tenant, ct);
        var packApproval = await SignAsync(tenant, new AttestationTarget(NodeKinds.LibraryItem, pack.Id.Value, pack.ContentHash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveLibraryItem(tenant, pack.Id, packApproval, Key(), Reason), ct)).Error.Should().BeNull();
        (await SendAsync(new DeclareStudyStandards(
            tenant, graph.Study.Id, StudyStandardsProfileId.New(), [sdtmig], [], [pack.Id], Key(), Reason), ct)).Error.Should().BeNull();
        return (tenant, graph.Study.Id, pack.Id, sdtmig);
    }

    private async Task<LibraryItemVersion> DraftAsync(TenantId tenant, CancellationToken ct)
    {
        var result = await SendAsync(new DraftLibraryItem(
            tenant, LibraryItemId.New(), LibraryItemKind.RulePack, PackKey, "Sample vitals rules", RulePackJson.ToJson(SampleRulePacks.Sample()), [], Key(), Reason), ct);
        result.Error.Should().BeNull();
        return result.Value!;
    }

    /// <summary>Validates the records with the study's pinned packs, one minute after the previous run.</summary>
    private async Task<ValidationRunOutcome> RunAsync(
        TenantId tenant, StudyId study, IReadOnlyList<RuleRecord> records, CancellationToken ct, IReadOnlyList<StandardVersionId>? inputs = null)
    {
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var request = new ValidationRequest(tenant, study, ValidationStage.Standardization, new DateOnly(2026, 9, 30), records, Key(), Reason)
        {
            Standard = "SDTMIG",
            StandardVersion = "3.4",
            Inputs = [.. (inputs ?? []).Select(i => NodeRef.For(i, 2))],
            Datasets = ["VITALS"],
        };
        var result = await fixture.Validation.RunAsync(request, ct);
        result.Error.Should().BeNull();
        return result.Value!;
    }

    private RecordValidationRun Record(TenantId tenant, StudyId study, LibraryItemId pack) =>
        new(tenant, ValidationRunId.New(), study, ValidationStage.Standardization, [pack], new DateOnly(2026, 9, 30), null, null, null, [],
            ["VITALS"], fixture.Clock.UtcNow.AddMinutes(-1), fixture.Clock.UtcNow, [], Key(), Reason);

    private Task<AttestationId> SignAsync(
        TenantId tenant, ValidationFindingId finding, ValidationRunId run, FindingStatus decision, string text, AttestationMeaning meaning, CancellationToken ct) =>
        SignAsync(tenant, new AttestationTarget(NodeKinds.ValidationFinding, finding.Value, FindingDecisionContent.Hash(finding, run, decision, text)), meaning, ct);

    private async Task<AttestationId> SignAsync(TenantId tenant, AttestationTarget target, AttestationMeaning meaning, CancellationToken ct)
    {
        var result = await SendAsync(
            new Attest(tenant, AttestationId.New(), meaning, target, fixture.Clock.UtcNow.AddMinutes(-1), "pwd+otp", Key(), $"Signing as {meaning}"), ct);
        result.Error.Should().BeNull();
        return result.Value;
    }

    private static RuleRecord Vitals(string key, decimal systolic, decimal diastolic) =>
        new("VITALS", key, new Dictionary<string, RuleValue>(StringComparer.Ordinal)
        {
            ["SYS"] = RuleValue.FromNumber(systolic),
            ["DIA"] = RuleValue.FromNumber(diastolic),
        });

    private Task<CommandResult<T>> SendAsync<T>(ICommand<T> command, CancellationToken ct) =>
        fixture.Sender.SendAsync(command, cancellationToken: ct);

    private static string Key() => $"key-{Guid.NewGuid():N}";
}
