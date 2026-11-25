using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Application.Standards;
using Cadence.Kernel.Application.Validation;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Migrations;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Kernel.WorkQueues;
using Cadence.Rules;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Standards;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Rules;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    [KernelRule("K-FIND-1")]
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
    [KernelRule("K-FIND-3")]
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
    [KernelRule("K-FIND-2")]
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
    [KernelRule("K-ID-2")]
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

    /// <summary>
    /// Validates study and study-site grants over open and decided validation findings and their attestations:
    /// <see cref="AuthorityEvaluator"/> and <see cref="GrantNodeVisibility"/> resolve both open and decided findings
    /// from <c>kernel.validation_detections</c>, scoping collection-instance and observation findings to their study
    /// site and tabulation findings to their study.
    /// </summary>
    [Fact(DisplayName = "StudyAndSiteGrants_AuthorizeAndExposeOpenAndDecidedFindingsByScope")]
    [KernelRule("K-AUTH-2")]
    public async Task StudyAndSiteGrantsAuthorizeAndExposeOpenAndDecidedFindingsByScope()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var forms = new KernelCollectionScenario(fixture);
        var bostonForm = await forms.NewFormAsync(ct);
        var tenant = bostonForm.Subject.Tenant;
        var study = bostonForm.Subject.Study;
        var bostonSite = bostonForm.Subject.StudySite;

        var chicagoSite = await forms.Observations.AddStudySiteAsync(tenant, study, ct);
        var chicagoEnrollment = await forms.Observations.ScreenAsync(tenant, chicagoSite, ct);
        var chicagoSubject = new KernelObservationScenario.Subject(tenant, study, chicagoSite, chicagoEnrollment);
        var chicagoOpened = await forms.Protocol.OkAsync(KernelCollectionScenario.Open(chicagoSubject, CollectionInstanceId.New(), forms.Tick()), ct);

        StudyId otherStudy;
        await using (var db = fixture.CreateContext(tenant))
        {
            var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
            var secondStudy = new Study(StudyId.New(), tenant, sponsor, $"STUDY-{Guid.NewGuid():N}"[..16], "Second study", "Phase 2", KernelPostgresFixture.ActorId, "Seeded");
            await fixture.SeedAsync(secondStudy);
            otherStudy = secondStudy.Id;
        }

        var sdtmig = (await SendAsync(new RegisterStandardVersion(
            tenant, StandardVersionId.New(), StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package, null, Key(), Reason), ct)).Value;
        var sdtmigHash = (await fixture.Standards.FindStandardVersionAsync(tenant, sdtmig, ct))!.ContentHash;
        var standardApproval = await SignAsync(tenant, new AttestationTarget(NodeKinds.StandardVersion, sdtmig.Value, sdtmigHash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, sdtmig, standardApproval, Key(), Reason), ct)).Error.Should().BeNull();

        var pack = await DraftAsync(tenant, ct);
        var packApproval = await SignAsync(tenant, new AttestationTarget(NodeKinds.LibraryItem, pack.Id.Value, pack.ContentHash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveLibraryItem(tenant, pack.Id, packApproval, Key(), Reason), ct)).Error.Should().BeNull();
        (await SendAsync(new DeclareStudyStandards(tenant, study, StudyStandardsProfileId.New(), [sdtmig], [], [pack.Id], Key(), Reason), ct)).Error.Should().BeNull();

        // Study-level tabulation findings (open warning + decided error):
        var studyRun = await RunAsync(tenant, study, [Vitals("row-study", 90m, 100m)], ct);
        var studyFindings = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study) { Stage = ValidationStage.Standardization }, ct)).Findings;
        var decidedStudyFinding = studyFindings.Single(f => f.RuleId == "R1").Id;
        var openStudyFinding = studyFindings.Single(f => f.RuleId == "R2").Id;
        const string StudyExplanation = "Study-level mapping explanation.";
        var studyFindingAttestation = await SignAsync(tenant, decidedStudyFinding, studyRun.RunId, FindingStatus.Explained, StudyExplanation, AttestationMeaning.Approved, ct);
        (await SendAsync(new ExplainFinding(tenant, decidedStudyFinding, StudyExplanation, studyFindingAttestation, Key(), Reason), ct)).Error.Should().BeNull();

        // Site-level findings at Boston (open Observation target + decided DatasetRecord collection_instance target) and Chicago (open DatasetRecord target):
        var bostonSaved = await forms.Protocol.OkAsync(
            KernelCollectionScenario.Save(
                bostonForm,
                KernelCollectionScenario.Value(KernelCollectionScenario.Systolic, KernelCollectionScenario.Mmhg(88)),
                KernelCollectionScenario.Value(KernelCollectionScenario.Diastolic, KernelCollectionScenario.Mmhg(96))),
            ct);
        var bostonObservation = bostonSaved.Values.Single(v => v.ItemId == KernelCollectionScenario.Systolic).ObservationId;

        var siteRunId = ValidationRunId.New();
        var siteRunAt = forms.Tick();
        (await SendAsync(
            new RecordValidationRun(
                tenant,
                siteRunId,
                study,
                ValidationStage.Collection,
                [pack.Id],
                new DateOnly(2026, 9, 30),
                null,
                null,
                null,
                [],
                ["VITALS"],
                siteRunAt.AddSeconds(-1),
                siteRunAt,
                [
                    new ValidationDetection(
                        PackKey,
                        "R1",
                        1,
                        ValidationSeverity.Error,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "VITALS", bostonForm.Instance.Value.ToString("D"), KernelCollectionScenario.Systolic),
                        "Boston form systolic < diastolic.",
                        null,
                        [new ObservedVariable("SYS", "88")]),
                    new ValidationDetection(
                        PackKey,
                        "R2",
                        1,
                        ValidationSeverity.Warning,
                        new FindingTarget(FindingTargetKind.Observation, "VITALS", NodeRef.For(bostonObservation).ToString()),
                        "Boston observation low reading.",
                        null,
                        [new ObservedVariable("SYS", "88")]),
                    new ValidationDetection(
                        PackKey,
                        "R1",
                        1,
                        ValidationSeverity.Error,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "VITALS", chicagoOpened.CollectionInstanceId.Value.ToString("D"), KernelCollectionScenario.Systolic),
                        "Chicago form systolic < diastolic.",
                        null,
                        [new ObservedVariable("SYS", "85")]),
                ],
                Key(),
                Reason),
            ct)).Error.Should().BeNull();

        var collectionFindings = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study) { Stage = ValidationStage.Collection }, ct)).Findings;
        var bostonDecidedFinding = collectionFindings.Single(f => f.Target.Key == bostonForm.Instance.Value.ToString("D")).Id;
        var bostonOpenObservationFinding = collectionFindings.Single(f => f.Target.Kind == FindingTargetKind.Observation).Id;
        var chicagoOpenFinding = collectionFindings.Single(f => f.Target.Key == chicagoOpened.CollectionInstanceId.Value.ToString("D")).Id;

        const string BostonRationale = "Confirmed standing reading at Boston clinic.";
        var bostonFindingAttestation = await SignAsync(tenant, bostonDecidedFinding, siteRunId, FindingStatus.Accepted, BostonRationale, AttestationMeaning.Responsibility, ct);
        (await SendAsync(new AcceptFinding(tenant, bostonDecidedFinding, BostonRationale, bostonFindingAttestation, Key(), Reason), ct)).Error.Should().BeNull();

        // Issue a study grant to a data manager and a Boston study-site grant to a coordinator:
        const string Capability = "validation.decide";
        var studyManager = Actor.Person("dm@validation-scope.test");
        var bostonCoordinator = Actor.Person("boston-crc@validation-scope.test");
        var now = fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await SendAsync(new IssueGrant(tenant, GrantId.New(), studyManager, Capability, null, AuthorityScope.Study(study), now, null, false, Key(), Reason), ct)).Error.Should().BeNull();
        (await SendAsync(new IssueGrant(tenant, GrantId.New(), bostonCoordinator, Capability, null, AuthorityScope.StudySite(bostonSite), now, null, false, Key(), Reason), ct)).Error.Should().BeNull();
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));

        var evaluator = new AuthorityEvaluator(fixture.AuthorityStore, new KernelAuthorityOptions(new HashSet<string>()));
        var visibility = new GrantNodeVisibility(fixture.AuthorityStore, fixture.Clock);
        var evaluatedAt = fixture.Clock.UtcNow;
        var capability = new Capability(Capability, VerbClass.Execute);
        ValueTask<AuthorizationDecision> EvaluateAsync(Actor actor, NodeRef resource) =>
            evaluator.EvaluateAsync(new AuthorizationRequest(actor, tenant, capability, resource, evaluatedAt, evaluatedAt), ct);

        // Study grant authorizes open and decided study-level and site-level findings in this study, but not another study:
        foreach (var allowed in new[]
        {
            NodeRef.For(openStudyFinding),
            NodeRef.For(decidedStudyFinding),
            NodeRef.For(bostonDecidedFinding),
            NodeRef.For(bostonOpenObservationFinding),
            NodeRef.For(chicagoOpenFinding),
        })
        {
            (await EvaluateAsync(studyManager, allowed)).Permitted
                .Should().BeTrue($"study grant must cover {allowed}");
        }

        (await EvaluateAsync(studyManager, NodeRef.For(otherStudy))).Permitted
            .Should().BeFalse();

        // Boston site grant authorizes Boston's open and decided findings, and denies study-wide and Chicago findings:
        (await EvaluateAsync(bostonCoordinator, NodeRef.For(bostonDecidedFinding))).Permitted.Should().BeTrue();
        (await EvaluateAsync(bostonCoordinator, NodeRef.For(bostonOpenObservationFinding))).Permitted.Should().BeTrue();
        (await EvaluateAsync(bostonCoordinator, NodeRef.For(openStudyFinding))).Permitted.Should().BeFalse();
        (await EvaluateAsync(bostonCoordinator, NodeRef.For(decidedStudyFinding))).Permitted.Should().BeFalse();
        (await EvaluateAsync(bostonCoordinator, NodeRef.For(chicagoOpenFinding))).Permitted.Should().BeFalse();

        // Visibility: study manager sees all findings and attestations; Boston coordinator sees Boston findings + study-level findings, while Chicago's site finding is masked:
        NodeRef[] inspectedNodes =
        [
            NodeRef.For(openStudyFinding),
            NodeRef.For(decidedStudyFinding, 1),
            NodeRef.For(studyFindingAttestation, 1),
            NodeRef.For(bostonDecidedFinding, 1),
            NodeRef.For(bostonFindingAttestation, 1),
            NodeRef.For(bostonOpenObservationFinding),
            NodeRef.For(chicagoOpenFinding),
        ];
        (await visibility.MaskAsync(tenant, studyManager, inspectedNodes, ct)).Should().BeEmpty();
        var coordinatorMasks = await visibility.MaskAsync(tenant, bostonCoordinator, inspectedNodes, ct);
        coordinatorMasks.Keys.Should().Equal(NodeRef.For(chicagoOpenFinding));
    }

    /// <summary>
    /// Validates ADR-2462 §5, K-TX-1, and K-ID-2 on PostgreSQL: projection rows update atomically with runs and decisions,
    /// emit zero extra audit entries per command, and enforce forced Row-Level Security across tenants and unbound connections.
    /// </summary>
    [Fact(DisplayName = "Projection_CommitsTransactionally_PreservesSingleAggregateAudit_AndEnforcesForcedRls")]
    [KernelRule("K-FIND-1")]
    [KernelRule("K-ID-2")]
    public async Task ProjectionCommitsTransactionallyPreservesSingleAggregateAuditAndEnforcesForcedRls()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, _, _) = await StudyWithPackAsync(ct);
        var (otherTenant, _, _, _) = await StudyWithPackAsync(ct);

        await using var auditProbe = fixture.CreateContext(tenant);
        var baselineAudits = await auditProbe.AuditEntries.AsNoTracking().CountAsync(ct);

        var r1 = await RunAsync(tenant, study, [Vitals("row-1", 90m, 100m)], ct);
        await using (var db = fixture.CreateContext(tenant))
        {
            var projectedAfterR1 = await db.Set<ValidationFindingProjectionRecord>().AsNoTracking()
                .Where(p => p.StudyId == study)
                .OrderBy(p => p.RuleId)
                .ToListAsync(ct);
            projectedAfterR1.Should().HaveCount(2);
            projectedAfterR1.Select(p => (p.RuleId, p.Status, p.Blocks, p.ProjectionVersion))
                .Should().Equal(
                    ("R1", FindingStatus.Open, true, 1L),
                    ("R2", FindingStatus.Open, false, 1L));

            var auditsAfterR1 = await db.AuditEntries.AsNoTracking().ToListAsync(ct);
            (auditsAfterR1.Count - baselineAudits).Should().Be(1, "K-TX-1 records exactly one aggregate audit entry for RecordValidationRun");
            auditsAfterR1.Should().NotContain(a => a.AggregateType != null && a.AggregateType.Contains("Projection", StringComparison.OrdinalIgnoreCase));
        }

        // Forced RLS: another tenant sees 0 rows, and an app-role connection with no tenant GUC sees 0 rows.
        await using (var otherDb = fixture.CreateContext(otherTenant))
        {
            (await otherDb.Set<ValidationFindingProjectionRecord>().AsNoTracking().CountAsync(ct)).Should().Be(0);
        }

        await using (var unboundConn = new NpgsqlConnection(fixture.ConnectionString))
        {
            await unboundConn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM kernel.validation_finding_projections", unboundConn);
            Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)).Should().Be(0, "forced RLS hides projection rows when cadence.tenant_id is unset");
        }

        var errorFinding = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study) { Severity = ValidationSeverity.Error }, ct))
            .Findings.Single();
        (await SendAsync(new InvestigateFinding(tenant, errorFinding.Id, Key(), Reason), ct)).Error.Should().BeNull();
        (await SendAsync(new ClassifyFinding(tenant, errorFinding.Id, FindingClassification.DataIssue, Key(), Reason), ct)).Error.Should().BeNull();
        const string ExplainText = "Confirmed cuff transposition with clinic.";
        var explainSig = await SignAsync(tenant, errorFinding.Id, r1.RunId, FindingStatus.Explained, ExplainText, AttestationMeaning.Approved, ct);
        (await SendAsync(new ExplainFinding(tenant, errorFinding.Id, ExplainText, explainSig, Key(), Reason), ct)).Error.Should().BeNull();

        await using (var db = fixture.CreateContext(tenant))
        {
            var row = await db.Set<ValidationFindingProjectionRecord>().AsNoTracking()
                .SingleAsync(p => p.FindingId == errorFinding.Id, ct);
            row.Status.Should().Be(FindingStatus.Explained);
            row.Blocks.Should().BeFalse();
            row.Classification.Should().Be(FindingClassification.DataIssue);
            row.DecisionInForce.Should().BeTrue();
            row.ProjectionVersion.Should().Be(4L);

            var allAudits = await db.AuditEntries.AsNoTracking().ToListAsync(ct);
            allAudits.Should().NotContain(a => a.AggregateType != null && a.AggregateType.Contains("Projection", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Validates deterministic projection rebuild (<see cref="RebuildValidationFindingProjections"/>,
    /// <see cref="IValidationFindingProjector"/>, and <see cref="ValidationFindingProjections.BackfillSql"/>) against
    /// <see cref="FindingLedger.Derive"/> after projection rows are wiped or corrupted.
    /// </summary>
    [Fact(DisplayName = "Projection_RebuildCommandProjectorAndBackfillSql_RestoreExactDeriveParity")]
    [KernelRule("K-FIND-1")]
    public async Task ProjectionRebuildCommandProjectorAndBackfillSqlRestoreExactDeriveParity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, _, _) = await StudyWithPackAsync(ct);

        var r1 = await RunAsync(tenant, study, [Vitals("row-a", 90m, 100m), Vitals("row-b", 92m, 99m), Vitals("row-c", 95m, 80m)], ct);
        var initial = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study), ct)).Findings;
        var rowAError = initial.Single(f => f.RuleId == "R1" && f.Target.Key == "row-a");
        var rowBError = initial.Single(f => f.RuleId == "R1" && f.Target.Key == "row-b");
        var rowCWarning = initial.Single(f => f.RuleId == "R2" && f.Target.Key == "row-c");

        // Explain row-a, accept row-b (which will resolve then recur -> must reopen!), investigate row-c.
        var sigA = await SignAsync(tenant, rowAError.Id, r1.RunId, FindingStatus.Explained, "Explained A.", AttestationMeaning.Approved, ct);
        (await SendAsync(new ExplainFinding(tenant, rowAError.Id, "Explained A.", sigA, Key(), Reason), ct)).Error.Should().BeNull();
        var sigB = await SignAsync(tenant, rowBError.Id, r1.RunId, FindingStatus.Accepted, "Accepted B.", AttestationMeaning.Responsibility, ct);
        (await SendAsync(new AcceptFinding(tenant, rowBError.Id, "Accepted B.", sigB, Key(), Reason), ct)).Error.Should().BeNull();
        (await SendAsync(new InvestigateFinding(tenant, rowCWarning.Id, Key(), Reason), ct)).Error.Should().BeNull();
        (await SendAsync(new ClassifyFinding(tenant, rowCWarning.Id, FindingClassification.MappingIssue, Key(), Reason), ct)).Error.Should().BeNull();

        // r2 resolves row-b (110/80) while keeping row-a (90/100) and row-c (95/80).
        await RunAsync(tenant, study, [Vitals("row-a", 90m, 100m), Vitals("row-b", 110m, 80m), Vitals("row-c", 95m, 80m)], ct);
        // r3 resolves row-a (110/80), while row-b recurs (92/99 -> voids acceptance -> Open!).
        await RunAsync(tenant, study, [Vitals("row-a", 110m, 80m), Vitals("row-b", 92m, 99m), Vitals("row-c", 95m, 80m)], ct);

        var expectedDerive = await DeriveTruthFromDbAsync(tenant, study, ct);

        await using (var db = fixture.CreateContext(tenant))
        {
            var liveStates = (await db.Set<ValidationFindingProjectionRecord>().AsNoTracking()
                .Where(p => p.StudyId == study)
                .OrderBy(p => p.FindingId)
                .ToListAsync(ct))
                .Select(p => p.ToFindingState())
                .ToList();
            liveStates.Should().Equal(expectedDerive);
        }

        // 1. Delete all projection rows as superuser, then rebuild via RebuildValidationFindingProjections command:
        await ExecuteAdminSqlAsync($"DELETE FROM kernel.validation_finding_projections WHERE study_id = '{study}'", ct);
        (await fixture.Findings.ListAsync(tenant, new FindingQuery(study), ct)).Total.Should().Be(0);

        var commandRebuilt = await SendAsync(new RebuildValidationFindingProjections(tenant, study, Key(), Reason), ct);
        commandRebuilt.Error.Should().BeNull();
        commandRebuilt.Value.Should().Be(expectedDerive.Count);

        await using (var db = fixture.CreateContext(tenant))
        {
            var rebuiltStates = (await db.Set<ValidationFindingProjectionRecord>().AsNoTracking()
                .Where(p => p.StudyId == study)
                .OrderBy(p => p.FindingId)
                .ToListAsync(ct))
                .Select(p => p.ToFindingState())
                .ToList();
            rebuiltStates.Should().Equal(expectedDerive);
        }

        // 2. Corrupt projection statuses, then rebuild via migration BackfillSql:
        await ExecuteAdminSqlAsync(
            $"UPDATE kernel.validation_finding_projections SET status = 'Resolved', blocks = FALSE WHERE study_id = '{study}'",
            ct);
        await ExecuteAdminSqlAsync(ValidationFindingProjections.BackfillSql, ct);

        await using (var db = fixture.CreateContext(tenant))
        {
            var backfilledStates = (await db.Set<ValidationFindingProjectionRecord>().AsNoTracking()
                .Where(p => p.StudyId == study)
                .OrderBy(p => p.FindingId)
                .ToListAsync(ct))
                .Select(p => p.ToFindingState())
                .ToList();
            backfilledStates.Should().Equal(expectedDerive);
        }
    }

    /// <summary>
    /// Validates the core performance contract: after 15 historical runs where resolved detections outnumber active
    /// findings, <see cref="IValidationFindings"/>, <see cref="IWorkQueues"/>, <see cref="EvidenceReadiness"/>,
    /// <see cref="ICollectionInstanceRecords"/>, and <see cref="ILockRecords"/> never scan
    /// <c>kernel.validation_runs</c> for ledger replay and materialize zero historical detection rows beyond returned findings.
    /// </summary>
    [Fact(DisplayName = "Projection_ReadsAndReadinessGates_MaterializeZeroHistoricalDetectionsBeyondReturnedFindings")]
    [KernelRule("K-FIND-1")]
    public async Task ProjectionReadsAndReadinessGatesMaterializeZeroHistoricalDetectionsBeyondReturnedFindings()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var forms = new KernelCollectionScenario(fixture);
        var form = await forms.NewFormAsync(ct);
        var tenant = form.Subject.Tenant;
        var study = form.Subject.Study;

        var sdtmig = (await SendAsync(new RegisterStandardVersion(
            tenant, StandardVersionId.New(), StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package, null, Key(), Reason), ct)).Value;
        var sdtmigHash = (await fixture.Standards.FindStandardVersionAsync(tenant, sdtmig, ct))!.ContentHash;
        var standardApproval = await SignAsync(tenant, new AttestationTarget(NodeKinds.StandardVersion, sdtmig.Value, sdtmigHash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, sdtmig, standardApproval, Key(), Reason), ct)).Error.Should().BeNull();

        var pack = await DraftAsync(tenant, ct);
        var packApproval = await SignAsync(tenant, new AttestationTarget(NodeKinds.LibraryItem, pack.Id.Value, pack.ContentHash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveLibraryItem(tenant, pack.Id, packApproval, Key(), Reason), ct)).Error.Should().BeNull();
        (await SendAsync(new DeclareStudyStandards(tenant, study, StudyStandardsProfileId.New(), [sdtmig], [], [pack.Id], Key(), Reason), ct)).Error.Should().BeNull();

        // Append 14 historical runs with 10 violating records (20 detections per run = 280 historical detections),
        // then a 15th run where 9 records are clean and only row-0 remains active (1 Error + 1 Warning).
        for (var r = 0; r < 14; r++)
        {
            var records = Enumerable.Range(0, 10).Select(i => Vitals($"row-{i}", 90m, 100m)).ToList();
            await RunAsync(tenant, study, records, ct);
        }

        var finalRecords = Enumerable.Range(0, 10)
            .Select(i => i == 0 ? Vitals($"row-{i}", 90m, 100m) : Vitals($"row-{i}", 115m, 78m))
            .ToList();
        await RunAsync(tenant, study, finalRecords, ct);

        var activePage = await fixture.Findings.ListAsync(
            tenant,
            new FindingQuery(study) { Statuses = [FindingStatus.Open, FindingStatus.Investigating], Take = 20 },
            ct);
        activePage.Total.Should().Be(2);
        var warningId = activePage.Findings.Single(f => f.Severity == ValidationSeverity.Warning).Id;
        (await SendAsync(new InvestigateFinding(tenant, warningId, Key(), Reason), ct)).Error.Should().BeNull();

        // Also record one Collection-stage active error on the eCRF form so EvidenceReadiness / CollectionInstance / Lock readiness exercise active findings.
        var formRunAt = forms.Tick();
        (await SendAsync(
            new RecordValidationRun(
                tenant,
                ValidationRunId.New(),
                study,
                ValidationStage.Collection,
                [pack.Id],
                new DateOnly(2026, 9, 30),
                null,
                null,
                null,
                [],
                ["VITALS"],
                formRunAt.AddSeconds(-1),
                formRunAt,
                [
                    new ValidationDetection(
                        PackKey,
                        "R1",
                        1,
                        ValidationSeverity.Error,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "VITALS", form.Instance.Value.ToString("D"), KernelCollectionScenario.Systolic),
                        "Form systolic < diastolic.",
                        null,
                        [new ObservedVariable("SYS", "88")]),
                ],
                Key(),
                Reason),
            ct)).Error.Should().BeNull();

        var interceptor = new SqlCaptureInterceptor();
        var probeServices = new ServiceCollection()
            .AddSingleton<IClock>(fixture.Clock)
            .AddSingleton<ICurrentActor>(fixture.CurrentActor)
            .AddSingleton<IAuthorizer>(fixture.Authorizer)
            .AddSingleton<IAuthorizationProbe>(fixture.AuthorizationProbe)
            .AddSingleton<INodeVisibility>(fixture.Visibility)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddKernelPersistence(fixture.ConnectionString!)
            .AddKernelScopeGraph()
            .AddKernelNavigation()
            .AddKernelStandardsPersistence()
            .AddKernelStandards()
            .AddKernelValidationPersistence()
            .AddKernelValidation()
            .AddCadenceRules()
            .AddCadenceStandards()
            .AddKernelWorkQueues();
        var optionsBuilder = new DbContextOptionsBuilder<KernelDbContext>();
        KernelDatabase.UseKernelNpgsql(optionsBuilder, fixture.ConnectionString!);
        optionsBuilder.AddInterceptors(new KernelTenantConnectionInterceptor(), interceptor);
        probeServices.AddSingleton<IDbContextFactory<KernelDbContext>>(new InterceptedDbContextFactory(optionsBuilder.Options));

        await using var provider = probeServices.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var tenantContexts = scope.ServiceProvider.GetRequiredService<KernelTenantDbContextFactory>();
        var store = scope.ServiceProvider.GetRequiredService<IValidationFindings>();
        var workQueues = scope.ServiceProvider.GetRequiredService<IWorkQueues>();
        var collectionRecords = scope.ServiceProvider.GetRequiredService<ICollectionInstanceRecords>();
        var lockRecords = scope.ServiceProvider.GetRequiredService<ILockRecords>();

        // 1. ListAsync(Open + Investigating, Take = 20): returns 3 active findings, reads <= 3 detection rows (not 283!).
        interceptor.Reset();
        var listed = await store.ListAsync(
            tenant,
            new FindingQuery(study) { Statuses = [FindingStatus.Open, FindingStatus.Investigating], Take = 20 },
            ct);
        listed.Total.Should().Be(3);
        listed.Findings.Should().HaveCount(3);
        interceptor.Commands.Should().NotContain(sql => sql.Contains("validation_run_datasets", StringComparison.OrdinalIgnoreCase));
        interceptor.DetectionRowsRead.Should().Be(3);

        // 2. LoadActiveErrorFindingTargetsAsync + CollectionInstanceRecords.ListForStudyAsync: zero validation_detections or validation_runs queries!
        interceptor.Reset();
        await using (var db = tenantContexts.Create(tenant))
        {
            var targets = await KernelValidationStore.LoadActiveErrorFindingTargetsAsync(db, study, ct);
            targets.Should().HaveCount(2);
        }

        interceptor.Commands.Should().NotContain(sql => sql.Contains("validation_detections", StringComparison.OrdinalIgnoreCase));
        interceptor.Commands.Should().NotContain(sql => sql.Contains("validation_runs", StringComparison.OrdinalIgnoreCase));

        interceptor.Reset();
        var studyForms = await collectionRecords.ListForStudyAsync(tenant, study, ct);
        studyForms.Should().ContainSingle();
        interceptor.Commands.Should().NotContain(sql => sql.Contains("validation_detections", StringComparison.OrdinalIgnoreCase));
        interceptor.Commands.Should().NotContain(sql => sql.Contains("validation_runs", StringComparison.OrdinalIgnoreCase));

        // 3. EvidenceReadiness (form signing check) + LockRecords.ReadReadinessAsync + WorkQueues: read only active error findings!
        interceptor.Reset();
        var formFindings = await store.ListAsync(
            tenant,
            new FindingQuery(study)
            {
                Severity = ValidationSeverity.Error,
                Statuses = [FindingStatus.Open, FindingStatus.Investigating],
                TargetKeys = [form.Instance.Value.ToString("D")],
            },
            ct);
        formFindings.Findings.Should().ContainSingle();
        interceptor.Commands.Should().NotContain(sql => sql.Contains("validation_run_datasets", StringComparison.OrdinalIgnoreCase));
        interceptor.DetectionRowsRead.Should().Be(1);

        interceptor.Reset();
        var lockReadiness = await lockRecords.ReadReadinessAsync(tenant, study, NodeRef.For(study), ct);
        lockReadiness!.ReadyForLock.Should().BeFalse();
        lockReadiness.OpenErrorFindingCount.Should().Be(2);
        interceptor.Commands.Should().NotContain(sql => sql.Contains("validation_run_datasets", StringComparison.OrdinalIgnoreCase));
        interceptor.DetectionRowsRead.Should().Be(2);

        interceptor.Reset();
        var queues = await workQueues.ReadAsync(tenant, new WorkQueueQuery(NodeRef.For(study)), ct);
        queues.IsSuccess.Should().BeTrue();
        interceptor.Commands.Should().NotContain(sql => sql.Contains("validation_run_datasets", StringComparison.OrdinalIgnoreCase));
        interceptor.DetectionRowsRead.Should().BeLessThanOrEqualTo(3);
    }

    private async Task<IReadOnlyList<FindingState>> DeriveTruthFromDbAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var runs = await db.Set<ValidationRun>().AsNoTracking()
            .Where(r => r.StudyId == study)
            .ToListAsync(ct);
        var runIds = runs.Select(r => r.Id).ToList();
        var packs = await db.Set<ValidationRunPack>().AsNoTracking()
            .Where(p => runIds.Contains(p.RunId))
            .ToListAsync(ct);
        var datasets = await db.Set<ValidationRunDataset>().AsNoTracking()
            .Where(d => runIds.Contains(d.RunId))
            .ToListAsync(ct);
        var detections = await db.Set<ValidationDetectionRecord>().AsNoTracking()
            .Where(d => runIds.Contains(d.RunId))
            .ToListAsync(ct);
        var decisions = await db.Set<ValidationFinding>().AsNoTracking()
            .Where(f => f.StudyId == study)
            .ToListAsync(ct);

        var packsByRun = packs.ToLookup(p => p.RunId, p => p.PackKey);
        var datasetsByRun = datasets.ToLookup(d => d.RunId, d => d.Dataset);
        var runById = runs.ToDictionary(r => r.Id);

        return FindingLedger.Derive(
            runs.Select(r => new LedgerRun(
                r.Id,
                r.Stage,
                new DateTimeOffset(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)),
                packsByRun[r.Id].ToHashSet(StringComparer.Ordinal),
                datasetsByRun[r.Id].ToHashSet(StringComparer.Ordinal))),
            detections.Select(d => new LedgerDetection(
                d.RunId,
                d.FindingId,
                runById[d.RunId].Stage,
                d.PackKey,
                d.Dataset,
                d.Severity)),
            decisions.Select(d => new LedgerDecision(d.Id, d.Decision, d.DecidedAt, d.Classification)));
    }

    private async Task ExecuteAdminSqlAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
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

    private sealed class InterceptedDbContextFactory(DbContextOptions<KernelDbContext> options) : IDbContextFactory<KernelDbContext>
    {
        public KernelDbContext CreateDbContext() => new(options);
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();
        private int _detectionRowsRead;

        public IReadOnlyList<string> Commands => [.. _commands];

        public int DetectionRowsRead => _detectionRowsRead;

        public void Reset()
        {
            _commands.Clear();
            Interlocked.Exchange(ref _detectionRowsRead, 0);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText ?? string.Empty;
            if (!sql.Contains("set_config('cadence.tenant_id'", StringComparison.OrdinalIgnoreCase))
            {
                _commands.Enqueue(sql);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken).ConfigureAwait(false);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText ?? string.Empty;
            var trimmed = sql.AsSpan().TrimStart();
            if ((trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
                && sql.Contains("validation_detections", StringComparison.OrdinalIgnoreCase))
            {
                return new ValueTask<DbDataReader>(new CountingDataReader(result, count => Interlocked.Add(ref _detectionRowsRead, count)));
            }

            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class CountingDataReader(DbDataReader inner, Action<int> onClose) : DbDataReader
    {
        private int _rows;
        private int _reported;

        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;

        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override System.Collections.IEnumerator GetEnumerator() => inner.GetEnumerator();
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) => inner.GetFieldValueAsync<T>(ordinal, cancellationToken);
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => inner.IsDBNullAsync(ordinal, cancellationToken);
        public override bool NextResult() => inner.NextResult();
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => inner.NextResultAsync(cancellationToken);

        public override bool Read()
        {
            var ok = inner.Read();
            if (ok)
            {
                _rows++;
            }

            return ok;
        }

        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            var ok = await inner.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (ok)
            {
                _rows++;
            }

            return ok;
        }

        public override void Close()
        {
            ReportOnce();
            inner.Close();
        }

        public override async Task CloseAsync()
        {
            ReportOnce();
            await inner.CloseAsync().ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReportOnce();
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            ReportOnce();
            await inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }

        private void ReportOnce()
        {
            if (Interlocked.Exchange(ref _reported, 1) == 0)
            {
                onClose(_rows);
            }
        }
    }
}
