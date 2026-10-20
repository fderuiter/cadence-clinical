using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Validation;

/// <summary>
/// Validation runs and findings without a database (spec §10.3, #4812): deterministic finding identity, the ledger
/// that derives status from runs and decisions (K-FIND-1), stage blocking (§10.2), run validation and the decision
/// record's transitions.
/// </summary>
public class ValidationFindingTests
{
    private const string Pack = "sdtm-core";
    private const string Vitals = "VS";

    private static readonly TenantId Tenant = TenantId.New();
    private static readonly StudyId Study = StudyId.New();
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "FindingId_IsDerivedFromTenantStudyStagePackRuleAndTarget")]
    [Trait("Category", "Unit")]
    public void FindingIdIsDerivedFromTenantStudyStagePackRuleAndTarget()
    {
        var identity = Identity("VS1", "row-1");
        var id = ValidationFindingId.For(Tenant, identity);

        ValidationFindingId.For(Tenant, identity with { }).Should().Be(id);
        id.Value.Version.Should().Be(8);
        new[]
        {
            ValidationFindingId.For(TenantId.New(), identity),
            ValidationFindingId.For(Tenant, identity with { Study = StudyId.New() }),
            ValidationFindingId.For(Tenant, identity with { Stage = ValidationStage.Analysis }),
            ValidationFindingId.For(Tenant, identity with { PackKey = "sdtm-fda" }),
            ValidationFindingId.For(Tenant, identity with { RuleId = "VS2" }),
            ValidationFindingId.For(Tenant, identity with { Target = identity.Target with { Key = "row-2" } }),
            ValidationFindingId.For(Tenant, identity with { Target = identity.Target with { Variable = "VSORRES" } }),
        }.Should().OnlyHaveUniqueItems().And.NotContain(id);
    }

    [Fact(DisplayName = "FindingId_ComponentsAreLengthPrefixed")]
    [Trait("Category", "Unit")]
    public void FindingIdComponentsAreLengthPrefixed()
    {
        // Without length prefixes these two targets would serialize to the same text.
        var a = Identity("VS1", "a|b") with { Target = new FindingTarget(FindingTargetKind.DatasetRecord, Vitals, "a|b", "c") };
        var b = Identity("VS1", "a") with { Target = new FindingTarget(FindingTargetKind.DatasetRecord, Vitals, "a", "b|c") };

        ValidationFindingId.For(Tenant, a).Should().NotBe(ValidationFindingId.For(Tenant, b));
    }

    /// <summary>
    /// Validates K-FIND-1: a finding detected again keeps its identity and moves its last detection; a covering run that
    /// no longer detects it resolves it, with that run as the evidence; a run over other datasets leaves it alone.
    /// @req:PRD-CRF-004
    /// </summary>
    [Fact(DisplayName = "Ledger_RerunWithoutDetection_ResolvesWithTheRunAsEvidence")]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-CRF-004")]
    [KernelRule("K-FIND-1")]
    public void LedgerRerunWithoutDetectionResolvesWithTheRunAsEvidence()
    {
        var finding = FindingId("VS1", "row-1");
        var r1 = Run(0);
        var r2 = Run(1);
        var other = Run(2, datasets: ["DM"]);
        var r3 = Run(3);

        var afterTwo = Single(FindingLedger.Derive([r1, r2], [Detect(r1, finding), Detect(r2, finding)], []));
        afterTwo.Status.Should().Be(FindingStatus.Open);
        afterTwo.FirstDetectedRun.Should().Be(r1.Id);
        afterTwo.LastDetectedRun.Should().Be(r2.Id);

        Single(FindingLedger.Derive([r1, r2, other], [Detect(r1, finding), Detect(r2, finding)], [])).Status.Should().Be(FindingStatus.Open);

        var resolved = Single(FindingLedger.Derive([r1, r2, other, r3], [Detect(r1, finding), Detect(r2, finding)], []));
        resolved.Status.Should().Be(FindingStatus.Resolved);
        resolved.ResolvedBy.Should().Be(r3.Id);
        resolved.LastDetectedRun.Should().Be(r2.Id);
        resolved.Blocks.Should().BeFalse();
    }

    [Fact(DisplayName = "Ledger_RunWithoutThePack_DoesNotResolve")]
    [Trait("Category", "Unit")]
    public void LedgerRunWithoutThePackDoesNotResolve()
    {
        var finding = FindingId("VS1", "row-1");
        var r1 = Run(0);
        var otherPack = Run(1, packs: ["sdtm-fda"]);
        var otherStage = Run(2, stage: ValidationStage.Analysis);

        Single(FindingLedger.Derive([r1, otherPack, otherStage], [Detect(r1, finding)], [])).Status.Should().Be(FindingStatus.Open);
    }

    [Fact(DisplayName = "Ledger_DecisionHoldsWhileDetected_AndIsVoidedByAResolutionAndRecurrence")]
    [Trait("Category", "Unit")]
    public void LedgerDecisionHoldsWhileDetectedAndIsVoidedByAResolutionAndRecurrence()
    {
        var finding = FindingId("VS1", "row-1");
        var r1 = Run(0);
        var r2 = Run(2);
        var r3 = Run(3);
        var r4 = Run(4);
        var accepted = new LedgerDecision(finding, FindingStatus.Accepted, T0.AddHours(1), FindingClassification.DataIssue);

        // Accepted after r1, detected again by r2: the acceptance holds.
        var held = Single(FindingLedger.Derive([r1, r2], [Detect(r1, finding), Detect(r2, finding)], [accepted]));
        held.Status.Should().Be(FindingStatus.Accepted);
        held.DecisionInForce.Should().BeTrue();
        held.Classification.Should().Be(FindingClassification.DataIssue);

        // r3 no longer detects it (resolved), r4 detects it again: it recurred after the acceptance, so it is open.
        var recurred = Single(FindingLedger.Derive([r1, r2, r3, r4], [Detect(r1, finding), Detect(r2, finding), Detect(r4, finding)], [accepted]));
        recurred.Status.Should().Be(FindingStatus.Open);
        recurred.DecisionInForce.Should().BeFalse();
        recurred.FirstDetectedRun.Should().Be(r1.Id);
        recurred.Classification.Should().Be(FindingClassification.DataIssue);

        // A decision made after the recurrence holds again.
        var reaccepted = accepted with { DecidedAt = T0.AddHours(5) };
        Single(FindingLedger.Derive([r1, r2, r3, r4], [Detect(r1, finding), Detect(r2, finding), Detect(r4, finding)], [reaccepted]))
            .Status.Should().Be(FindingStatus.Accepted);
    }

    [Fact(DisplayName = "Ledger_ClassificationOnly_LeavesTheFindingOpen")]
    [Trait("Category", "Unit")]
    public void LedgerClassificationOnlyLeavesTheFindingOpen()
    {
        var finding = FindingId("VS1", "row-1");
        var r1 = Run(0);

        var state = Single(FindingLedger.Derive([r1], [Detect(r1, finding)], [new LedgerDecision(finding, null, null, FindingClassification.MappingIssue)]));

        state.Status.Should().Be(FindingStatus.Open);
        state.DecisionInForce.Should().BeFalse();
        state.Classification.Should().Be(FindingClassification.MappingIssue);
    }

    /// <summary>
    /// Validates K-FIND-1 over generated histories: for any sequence of covering runs, a finding is resolved exactly when
    /// the latest run did not detect it, is never duplicated, and keeps its first detection.
    /// @req:PRD-CRF-004
    /// </summary>
    [Fact(DisplayName = "Ledger_GeneratedHistories_ResolveExactlyWhenTheLatestRunMissesTheFinding")]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-CRF-004")]
    [KernelRule("K-FIND-1")]
    public void LedgerGeneratedHistoriesResolveExactlyWhenTheLatestRunMissesTheFinding()
    {
        var findings = Enumerable.Range(0, 6).Select(i => FindingId("VS1", $"row-{i}")).ToArray();
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var runs = Enumerable.Range(0, random.Next(1, 8)).Select(i => Run(i)).ToList();
            var detections = runs.SelectMany(r => findings.Where(_ => random.Next(2) == 0).Select(f => Detect(r, f))).ToList();

            var states = FindingLedger.Derive(runs.OrderBy(_ => random.Next()), detections, []);

            var detected = detections.Select(d => d.Finding).Distinct().ToList();
            states.Select(s => s.Finding).Should().BeEquivalentTo(detected, $"seed {seed}");
            foreach (var state in states)
            {
                var runsOfFinding = detections.Where(d => d.Finding == state.Finding).Select(d => runs.IndexOf(runs.Single(r => r.Id == d.Run))).ToList();
                state.FirstDetectedRun.Should().Be(runs[runsOfFinding.Min()].Id, $"seed {seed}");
                state.LastDetectedRun.Should().Be(runs[runsOfFinding.Max()].Id, $"seed {seed}");
                var latestMissed = runsOfFinding.Max() < runs.Count - 1;
                state.Status.Should().Be(latestMissed ? FindingStatus.Resolved : FindingStatus.Open, $"seed {seed}");
                state.ResolvedBy.Should().Be(latestMissed ? runs[runsOfFinding.Max() + 1].Id : null, $"seed {seed}");
            }
        }
    }

    [Fact(DisplayName = "Ledger_DetectionOfUnknownRun_Throws")]
    [Trait("Category", "Unit")]
    public void LedgerDetectionOfUnknownRunThrows()
    {
        var act = () => FindingLedger.Derive([Run(0)], [Detect(Run(1), FindingId("VS1", "row-1"))], []);

        act.Should().Throw<ArgumentException>().WithMessage("*unknown run*");
    }

    [Theory(DisplayName = "Blocks_ErrorsBlockEveryStageButCollection_UntilDispositioned")]
    [Trait("Category", "Unit")]
    [InlineData(ValidationStage.Design, ValidationSeverity.Error, FindingStatus.Open, true)]
    [InlineData(ValidationStage.Standardization, ValidationSeverity.Error, FindingStatus.Investigating, true)]
    [InlineData(ValidationStage.Submission, ValidationSeverity.Error, FindingStatus.Open, true)]
    [InlineData(ValidationStage.Collection, ValidationSeverity.Error, FindingStatus.Open, false)]
    [InlineData(ValidationStage.Analysis, ValidationSeverity.Warning, FindingStatus.Open, false)]
    [InlineData(ValidationStage.Analysis, ValidationSeverity.Notice, FindingStatus.Open, false)]
    [InlineData(ValidationStage.Analysis, ValidationSeverity.Error, FindingStatus.Accepted, false)]
    [InlineData(ValidationStage.Analysis, ValidationSeverity.Error, FindingStatus.Explained, false)]
    [InlineData(ValidationStage.Analysis, ValidationSeverity.Error, FindingStatus.Resolved, false)]
    public void BlocksErrorsBlockEveryStageButCollectionUntilDispositioned(
        ValidationStage stage, ValidationSeverity severity, FindingStatus status, bool blocks) =>
        FindingBlocking.Blocks(stage, severity, status).Should().Be(blocks);

    [Fact(DisplayName = "DecisionHash_CoversFindingRunDecisionAndText")]
    [Trait("Category", "Unit")]
    public void DecisionHashCoversFindingRunDecisionAndText()
    {
        var finding = FindingId("VS1", "row-1");
        var run = ValidationRunId.New();
        var hash = FindingDecisionContent.Hash(finding, run, FindingStatus.Explained, "Collected before the unit change.");

        hash.Should().StartWith("sha256:");
        FindingDecisionContent.Hash(finding, run, FindingStatus.Explained, "Collected before the unit change.").Should().Be(hash);
        new[]
        {
            FindingDecisionContent.Hash(FindingId("VS1", "row-2"), run, FindingStatus.Explained, "Collected before the unit change."),
            FindingDecisionContent.Hash(finding, ValidationRunId.New(), FindingStatus.Explained, "Collected before the unit change."),
            FindingDecisionContent.Hash(finding, run, FindingStatus.Accepted, "Collected before the unit change."),
            FindingDecisionContent.Hash(finding, run, FindingStatus.Explained, "Collected before the unit change"),
        }.Should().OnlyHaveUniqueItems().And.NotContain(hash);
    }

    [Fact(DisplayName = "Record_WellFormedRun_KeepsPacksDatasetsInputsAndDetections")]
    [Trait("Category", "Unit")]
    public void RecordWellFormedRunKeepsPacksDatasetsInputsAndDetections()
    {
        var input = NodeRef.For(StandardVersionId.New(), 2);
        var command = Command([Detection("VS1", "row-1"), Detection("VS2", "row-1")]) with { Inputs = [input, input] };

        var (run, error) = ValidationRun.Record(Tenant, command, [RulePack()], T0.AddHours(1));

        error.Should().BeNull();
        run!.Packs.Should().ContainSingle().Which.PackKey.Should().Be(Pack);
        run.Datasets.Select(d => d.Dataset).Should().Equal("DM", Vitals);
        run.Inputs.Select(i => i.Input).Should().Equal(input.ToString());
        run.Detections.Should().HaveCount(2);
        run.Detections[0].FindingId.Should().Be(ValidationFindingId.For(Tenant, ValidationRun.Identity(command, command.Detections[0])));
        run.Detections[0].ObservedValues.Should().Equal(new ObservedVariable("VSORRES", "300"), new ObservedVariable("VSORRESU", null));
        run.Detections[0].Target.Should().Be(command.Detections[0].Target);
    }

    [Theory(DisplayName = "Record_MalformedRun_IsRefused")]
    [Trait("Category", "Unit")]
    [InlineData("unknown pack", "is not one the run evaluated")]
    [InlineData("dataset outside the run", "is not one the run covers")]
    [InlineData("duplicate detection", "already detected rule VS1")]
    [InlineData("completes after recording", "cannot complete after it is recorded")]
    [InlineData("completes before starting", "cannot complete before it starts")]
    [InlineData("unversioned input", "must be a versioned reference")]
    [InlineData("versioned target", "names the object, not one version")]
    [InlineData("observation target naming a study", "must name an observation")]
    [InlineData("no datasets", "covers 1 to")]
    [InlineData("rule version 0", "rule versions start at 1")]
    [InlineData("standard version without standard", "needs its standard")]
    public void RecordMalformedRunIsRefused(string problem, string message)
    {
        var command = Command([Detection("VS1", "row-1")]);
        var observation = ObservationId.New();
        command = problem switch
        {
            "unknown pack" => command with { Detections = [Detection("VS1", "row-1") with { PackKey = "other" }] },
            "dataset outside the run" => command with { Detections = [Detection("VS1", "row-1") with { Target = new(FindingTargetKind.DatasetRecord, "LB", "row-1") }] },
            "duplicate detection" => command with { Detections = [Detection("VS1", "row-1"), Detection("VS1", "row-1")] },
            "completes after recording" => command with { CompletedAt = T0.AddHours(2) },
            "completes before starting" => command with { StartedAt = T0.AddMinutes(10), CompletedAt = T0.AddMinutes(5) },
            "unversioned input" => command with { Inputs = [NodeRef.For(StandardVersionId.New())] },
            "versioned target" => command with
            {
                Detections = [Detection("VS1", "row-1") with { Target = new(FindingTargetKind.Observation, Vitals, NodeRef.For(observation, 1).ToString()) }],
            },
            "observation target naming a study" => command with
            {
                Detections = [Detection("VS1", "row-1") with { Target = new(FindingTargetKind.Observation, Vitals, NodeRef.For(Study).ToString()) }],
            },
            "no datasets" => command with { Datasets = [], Detections = [] },
            "rule version 0" => command with { Detections = [Detection("VS1", "row-1") with { RuleVersion = 0 }] },
            "standard version without standard" => command with { Standard = null },
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };

        var (run, error) = ValidationRun.Record(Tenant, command, [RulePack()], T0.AddHours(1));

        run.Should().BeNull();
        error!.Code.Should().Be(ValidationErrors.RunInvalid.Code);
        error.Message.Should().Contain(message);
    }

    [Fact(DisplayName = "Record_ObservationTarget_WithoutVersion_IsAccepted")]
    [Trait("Category", "Unit")]
    public void RecordObservationTargetWithoutVersionIsAccepted()
    {
        var target = new FindingTarget(FindingTargetKind.Observation, Vitals, NodeRef.For(ObservationId.New()).ToString(), "VSORRES");
        var command = Command([Detection("VS1", "row-1") with { Target = target }]);

        ValidationRun.Record(Tenant, command, [RulePack()], T0.AddHours(1)).Error.Should().BeNull();
        target.Node!.Kind.Should().Be(NodeKinds.Observation);
    }

    [Fact(DisplayName = "Decisions_FollowTheFindingStatus")]
    [Trait("Category", "Unit")]
    public void DecisionsFollowTheFindingStatus()
    {
        var finding = ValidationFinding.Start(FindingId("VS1", "row-1"), Tenant, Study);
        var run = ValidationRunId.New();
        var attestation = AttestationId.New();

        finding.Investigate(FindingStatus.Resolved, "dm-1", T0).Should().NotBeNull();
        finding.Investigate(FindingStatus.Open, "dm-1", T0).Should().BeNull();
        finding.Decision.Should().Be(FindingStatus.Investigating);
        finding.Classify(FindingStatus.Resolved, FindingClassification.DataIssue)!.Code.Should().Be(ValidationErrors.InvalidTransition.Code);
        finding.Classify(FindingStatus.Investigating, FindingClassification.DataIssue).Should().BeNull();
        finding.Conclude(FindingStatus.Investigating, FindingStatus.Explained, " ", run, attestation, "dm-1", T0)!.Code
            .Should().Be(ValidationErrors.DecisionTextInvalid.Code);
        finding.Conclude(FindingStatus.Investigating, FindingStatus.Explained, "Collected before the unit change.", run, attestation, "dm-1", T0.AddMinutes(1))
            .Should().BeNull();
        finding.Decision.Should().Be(FindingStatus.Explained);
        finding.DecidedOnRun.Should().Be(run);
        finding.AttestationId.Should().Be(attestation);
        finding.Conclude(FindingStatus.Explained, FindingStatus.Accepted, "Accepted.", run, attestation, "dm-1", T0)!.Code
            .Should().Be(ValidationErrors.InvalidTransition.Code);

        finding.Reopen(FindingStatus.Open, "dm-2", T0).Should().NotBeNull();
        finding.Reopen(FindingStatus.Explained, "dm-2", T0.AddMinutes(2)).Should().BeNull();
        finding.Decision.Should().Be(FindingStatus.Open);
        finding.DecisionText.Should().BeNull();
        finding.AttestationId.Should().BeNull();
        finding.Classification.Should().Be(FindingClassification.DataIssue);
    }

    [Fact(DisplayName = "View_Explain_NamesRuleEvidenceAndBlockingCondition")]
    [Trait("Category", "Unit")]
    public void ViewExplainNamesRuleEvidenceAndBlockingCondition()
    {
        var run = ValidationRunId.New();
        var observation = NodeRef.For(ObservationId.New());
        var view = new ValidationFindingView(
            FindingId("VS1", "row-1"), Study, ValidationStage.Standardization, Pack, LibraryItemId.New(), "VS1", 3, ValidationSeverity.Error,
            new FindingTarget(FindingTargetKind.Observation, Vitals, observation.ToString(), "VSORRES"), "VSORRES is out of range.", "0 to 250",
            [new ObservedVariable("VSORRES", "300")], "SDTMIG", "3.4", null, FindingStatus.Open, run, run, null, null, null);

        var explanation = view.Explain();

        view.Blocks.Should().BeTrue();
        explanation.Producer.Should().Be("quality.finding/VS1@3");
        explanation.Claim.Should().Contain("blocks Standardization");
        explanation.Steps[1].Evidence.Should().Equal(observation);
        explanation.Conditions.Select(c => (c.Code, c.Met)).Should().Equal(("finding.detected", true), ("finding.dispositioned", false));
        explanation.NextActions.Select(a => a.Capability).Should().Equal(null, "validation_finding.explain", "validation_finding.accept");
    }

    [Fact(DisplayName = "Runner_Detection_MapsAViolationAndShortensLongValues")]
    [Trait("Category", "Unit")]
    public void RunnerDetectionMapsAViolationAndShortensLongValues()
    {
        var violation = new RuleViolation("VS1", 2, ValidationSeverity.Warning, Vitals, "row-9", "VSORRES is out of range.",
            [new ObservedValue("VSORRES", RuleValue.FromText(new string('x', 3000))), new ObservedValue("VSORRESU", RuleValue.Null)]);

        var detection = ValidationRunner.Detection(Pack, FindingTargetKind.DatasetRecord, violation);

        detection.Should().BeEquivalentTo(new
        {
            PackKey = Pack,
            RuleId = "VS1",
            RuleVersion = 2,
            Severity = ValidationSeverity.Warning,
            Target = new FindingTarget(FindingTargetKind.DatasetRecord, Vitals, "row-9"),
            Message = "VSORRES is out of range.",
        });
        detection.Observed[0].Value!.Length.Should().Be(ValidationDetection.MaxTextLength);
        detection.Observed[0].Value.Should().EndWith("…");
        detection.Observed[1].Value.Should().BeNull();
    }

    private static FindingIdentity Identity(string rule, string key) =>
        new(Study, ValidationStage.Standardization, Pack, rule, new FindingTarget(FindingTargetKind.DatasetRecord, Vitals, key));

    private static ValidationFindingId FindingId(string rule, string key) => ValidationFindingId.For(Tenant, Identity(rule, key));

    private static LedgerRun Run(int hour, string[]? packs = null, string[]? datasets = null, ValidationStage stage = ValidationStage.Standardization) =>
        new(ValidationRunId.New(T0.AddHours(hour)), stage, T0.AddHours(hour),
            (packs ?? [Pack]).ToHashSet(StringComparer.Ordinal), (datasets ?? [Vitals]).ToHashSet(StringComparer.Ordinal));

    private static LedgerDetection Detect(LedgerRun run, ValidationFindingId finding) =>
        new(run.Id, finding, ValidationStage.Standardization, Pack, Vitals, ValidationSeverity.Error);

    private static FindingState Single(IReadOnlyList<FindingState> states) => states.Should().ContainSingle().Subject;

    private static RunRulePack RulePack() => new(LibraryItemId.New(), Pack, 1, "sha256:" + new string('0', 64), 2);

    private static ValidationDetection Detection(string rule, string key) =>
        new(Pack, rule, 1, ValidationSeverity.Error, new FindingTarget(FindingTargetKind.DatasetRecord, Vitals, key), $"{rule} failed.", null,
            [new ObservedVariable("VSORRES", "300"), new ObservedVariable("VSORRESU", null)]);

    private static RecordValidationRun Command(IReadOnlyList<ValidationDetection> detections) =>
        new(Tenant, ValidationRunId.New(), Study, ValidationStage.Standardization, [LibraryItemId.New()], new DateOnly(2026, 9, 30),
            "SDTMIG", "3.4", null, [], [Vitals, "DM"], T0, T0.AddMinutes(5), detections, "key", "Validation test");
}
