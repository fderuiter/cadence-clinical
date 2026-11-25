using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Standards.Mapping;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Kernel.Protocol;
using Cadence.TestSupport.Kernel.Standards;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The simulation engine without a database (ADR-2480, spec §11.1): the structural diff by stable element id
/// (K-PROT-1), empty results for unchanged candidates (K-SIM-2), reproducible hashes (K-SIM-3), masking (K-SIM-4)
/// and the <c>simulation.run</c> check.
/// </summary>
public sealed class SimulationTests
{
    private const string Base = """
        {"id":"Study_1","instanceType":"Study","name":"CARDIO-1","versions":[{"id":"StudyVersion_1","instanceType":"StudyVersion",
          "arms":[{"id":"Arm_A","instanceType":"StudyArm","name":"Drug"},{"id":"Arm_B","instanceType":"StudyArm","name":"Placebo"}],
          "window":{"early":"P1D","late":"P2D"}}]}
        """;

    /// <summary>A protocol design (ADR-2472), as stored and unsaved candidates must be.</summary>
    private static readonly string Design = ProtocolDesignSamples.Minimal;

    private static readonly TenantId Tenant = TenantId.New();
    private static readonly StudyId Study = StudyId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Diff_OfIdenticalContentIsEmpty()
    {
        var reordered = """{"name":"CARDIO-1","instanceType":"Study","id":"Study_1","versions":[{"window":{"late":"P2D","early":"P1D"},"id":"StudyVersion_1","instanceType":"StudyVersion","arms":[{"name":"Drug","id":"Arm_A","instanceType":"StudyArm"},{"id":"Arm_B","instanceType":"StudyArm","name":"Placebo"}]}]}""";

        ProtocolStructuralDiff.Compare(Base, reordered).Value.Should().BeEmpty("key order is not a change");
    }

    /// <summary>
    /// A new arm is added, a renamed arm is changed at its own property, and the version that holds them changes only
    /// in its list of arms; a nested value change is reported at its JSON Pointer.
    /// </summary>
    [Fact]
    public void Diff_ReportsEachElementOnceByStableId()
    {
        var candidate = Base
            .Replace("\"Placebo\"}]", "\"Placebo\"},{\"id\":\"Arm_C\",\"instanceType\":\"StudyArm\",\"name\":\"High dose\"}]", StringComparison.Ordinal)
            .Replace("\"Drug\"", "\"Low dose\"", StringComparison.Ordinal)
            .Replace("\"late\":\"P2D\"", "\"late\":\"P3D\"", StringComparison.Ordinal);

        var diff = ProtocolStructuralDiff.Compare(Base, candidate).Value;

        diff.Should().BeEquivalentTo(
            [
                new ElementChange("Arm_A", "StudyArm", ElementChangeKind.Changed, ["/name"]),
                new ElementChange("Arm_C", "StudyArm", ElementChangeKind.Added, []),
                new ElementChange("StudyVersion_1", "StudyVersion", ElementChangeKind.Changed, ["/arms", "/window/late"]),
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Diff_ReportsRemovedElementsAndReorderedChildren()
    {
        var withoutB = Base.Replace(",{\"id\":\"Arm_B\",\"instanceType\":\"StudyArm\",\"name\":\"Placebo\"}", string.Empty, StringComparison.Ordinal);
        var swapped = Base.Replace(
            "{\"id\":\"Arm_A\",\"instanceType\":\"StudyArm\",\"name\":\"Drug\"},{\"id\":\"Arm_B\",\"instanceType\":\"StudyArm\",\"name\":\"Placebo\"}",
            "{\"id\":\"Arm_B\",\"instanceType\":\"StudyArm\",\"name\":\"Placebo\"},{\"id\":\"Arm_A\",\"instanceType\":\"StudyArm\",\"name\":\"Drug\"}",
            StringComparison.Ordinal);

        ProtocolStructuralDiff.Compare(Base, withoutB).Value.Should().BeEquivalentTo(
            [
                new ElementChange("Arm_B", "StudyArm", ElementChangeKind.Removed, []),
                new ElementChange("StudyVersion_1", "StudyVersion", ElementChangeKind.Changed, ["/arms"]),
            ]);
        ProtocolStructuralDiff.Compare(Base, swapped).Value.Should().BeEquivalentTo(
            [new ElementChange("StudyVersion_1", "StudyVersion", ElementChangeKind.Changed, ["/arms"])],
            "reordering children changes only the parent's list");
    }

    /// <summary>A root without an id is the <c>$root</c> element; with no base, every element is added.</summary>
    [Fact]
    public void Diff_TreatsAnAnonymousRootAsAnElement()
    {
        var diff = ProtocolStructuralDiff.Compare("""{"usdmVersion":"3.0","study":{"id":"S"}}""", """{"usdmVersion":"4.0","study":{"id":"S"}}""").Value;
        var first = ProtocolStructuralDiff.Compare(null, """{"study":{"id":"S"}}""").Value!;

        diff.Should().ContainSingle().Which.Should().Match<ElementChange>(c =>
            c.ElementId == ProtocolStructuralDiff.RootId && c.Change == ElementChangeKind.Changed && c.Properties.SequenceEqual(new[] { "/usdmVersion" }));
        first.Select(c => (c.ElementId, c.Change)).Should().Equal((ProtocolStructuralDiff.RootId, ElementChangeKind.Added), ("S", ElementChangeKind.Added));
    }

    [Theory]
    [InlineData("""{"id":"S","arms":[{"id":"A"},{"id":"A"}]}""", "K.Simulation.ElementIdDuplicated")]
    [InlineData("""{"id":"S","child":{"id":"S"}}""", "K.Simulation.ElementIdDuplicated")]
    [InlineData("""[{"id":"S"}]""", "K.Simulation.ContentInvalid")]
    [InlineData("""{"id":""", "K.Simulation.ContentInvalid")]
    public void Diff_RejectsContentWithoutUniqueElementIds(string content, string code) =>
        ProtocolStructuralDiff.Compare(Base, content).Error!.Code.Should().Be(code);

    /// <summary>
    /// A draft identical to its approved parent gives an empty result (K-SIM-2), and a draft that renames an arm
    /// reports that arm, with the study as scope and both versions pinned, and reads the dependents of that arm only.
    /// </summary>
    [Fact]
    [KernelRule("K-SIM-2")]
    public async Task Simulate_StoredDraftAgainstItsParent()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Design));
        var same = inputs.Add(Draft(parent, Design));
        var renamed = inputs.Add(Draft(parent, Renamed("arm.A", "Low dose")));
        var engine = Engine(inputs);

        var unchanged = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(same.Id));
        var changed = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(renamed.Id));

        unchanged.IsEmpty.Should().BeTrue();
        unchanged.Dependents.Should().BeNull("a candidate that changes nothing affects nothing");
        changed.Scope.Should().Be(NodeRef.For(Study));
        changed.Candidate.Should().Be(new CandidateSummary(
            CandidateKind.ProtocolVersion,
            NodeRef.For(renamed.Id, renamed.VersionIndex),
            NodeRef.For(parent.Id, parent.VersionIndex),
            ContentHash.Compute(renamed.Content),
            parent.ContentHash));
        changed.StructuralDiff.Should().ContainSingle().Which.ElementId.Should().Be("arm.A");
        changed.ScheduleChanges.Should().BeEmpty("renaming an arm moves no visit");
        changed.EngineVersions.Should().Equal(
            SimulationEngine.DependentsEngine,
            SimulationEngine.ScheduleEngine,
            SimulationEngine.MatrixGapEngine,
            ProtocolStructuralDiff.Engine);
        changed.AsOf.Should().Be(new SimulationAsOf(Now, Now));
        changed.Dependents!.Total.Should().Be(1);
        var asked = inputs.DependentsAsked.Should().ContainSingle().Subject;
        (asked.Base, asked.Candidate, asked.AsOf).Should().Be((parent.Id, (ProtocolVersionId?)renamed.Id, new SimulationAsOf(Now, Now)));
        asked.Impacted.Should().BeEquivalentTo([NodeRef.For(ProtocolElementId.For(Study, "arm.A"))], "the walk enters only the renamed arm (ADR-2474)");
    }

    /// <summary>
    /// The same inputs give the same input and result hashes (K-SIM-3); a different as-of or candidate changes them.
    /// </summary>
    [Fact]
    [KernelRule("K-SIM-3")]
    public async Task Simulate_HashesAreReproducible()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Design));
        var engine = Engine(inputs);
        var candidate = SimulationCandidate.UnsavedProtocolVersion(parent.Id, ProtocolDesignDocument.Write(ProtocolDesignSamples.MinimalDesign() with { Title = "CARDIO-2" }));

        var first = await SimulateAsync(engine, candidate);
        var second = await SimulateAsync(engine, candidate);
        var later = await SimulateAsync(engine, candidate, Now.AddDays(1));
        var other = await SimulateAsync(engine, SimulationCandidate.UnsavedProtocolVersion(parent.Id, Design));

        second.InputHash.Should().Be(first.InputHash);
        second.ResultHash.Should().Be(first.ResultHash);
        ContentHash.IsWellFormed(first.ResultHash).Should().BeTrue();
        later.InputHash.Should().NotBe(first.InputHash);
        other.ResultHash.Should().NotBe(first.ResultHash);
        other.IsEmpty.Should().BeTrue();
        first.Candidate.Version.Should().BeNull("unsaved content has no stored version");
        inputs.DependentsAsked.Should().AllSatisfy(asked => asked.Candidate.Should().BeNull("unsaved content has nothing to exclude"));
    }

    /// <summary>
    /// A reader who may not see the candidate gets the diff withheld with the reason, no content hashes, and a result
    /// hash that does not move when the hidden content changes (K-SIM-4).
    /// </summary>
    [Fact]
    [KernelRule("K-SIM-4")]
    public async Task Simulate_MasksWhatTheReaderMayNotSee()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Design));
        var draft = inputs.Add(Draft(parent, Renamed("arm.A", "Low dose")));
        var visibility = new Hide(NodeRef.For(draft.Id, draft.VersionIndex), MaskReason.Blinded);
        var engine = Engine(inputs, visibility);

        var hidden = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(draft.Id));
        inputs.Replace(draft with { Content = Renamed("arm.B", "Active comparator") });
        var hiddenAfterChange = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(draft.Id));

        hidden.StructuralDiff.Should().BeEmpty();
        hidden.MatrixGaps.Should().BeEmpty();
        hidden.Masked.Should().Equal(
            new MaskedPart(SimulationEngine.DependentsPart, MaskReason.Blinded),
            new MaskedPart(SimulationEngine.MatrixGapsPart, MaskReason.Blinded),
            new MaskedPart(SimulationEngine.ScheduleChangesPart, MaskReason.Blinded),
            new MaskedPart(SimulationEngine.StructuralDiffPart, MaskReason.Blinded));
        hidden.ScheduleChanges.Should().BeEmpty();
        hidden.Dependents.Should().BeNull();
        inputs.DependentsAsked.Should().BeEmpty("nothing is read about what depends on a hidden version");
        hidden.Candidate.Should().Be(new CandidateSummary(CandidateKind.ProtocolVersion, null, null, null, null));
        hidden.IsEmpty.Should().BeFalse("a withheld diff is not an empty one");
        hiddenAfterChange.ResultHash.Should().Be(hidden.ResultHash);
        visibility.Asked.Should().HaveCount(2, "one visibility question per run").And.AllSatisfy(nodes => nodes.Should().BeEquivalentTo(
            [NodeRef.For(Study), NodeRef.For(draft.Id, draft.VersionIndex), NodeRef.For(parent.Id, parent.VersionIndex)]));
    }

    /// <summary>
    /// A candidate that moves a visit, adds a timed one and drops another reports each on the study-day axis
    /// (K-TIME-7); renaming a visit is a structural change only.
    /// </summary>
    [Fact]
    public async Task Simulate_ReportsScheduleChangesOnTheStudyDayAxis()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Scheduled(
            Visit("act.screening", "Screening", "ep.screening", -14, 0, 0),
            Visit("act.week4", "Week 4", "ep.treatment", 28, 2, 2),
            Visit("act.week8", "Week 8", "ep.treatment", 56, 3, 3))));
        var candidate = Scheduled(
            Visit("act.screening", "Screening visit", "ep.screening", -14, 0, 0),
            Visit("act.week4", "Week 4", "ep.treatment", 29, 2, 3),
            Visit("act.week12", "Week 12", "ep.treatment", 84, 3, 3));
        var engine = Engine(inputs);

        var result = await SimulateAsync(engine, SimulationCandidate.UnsavedProtocolVersion(parent.Id, candidate));
        var dayLater = await SimulateAsync(engine, SimulationCandidate.UnsavedProtocolVersion(parent.Id, candidate.Replace("\"day\":84", "\"day\":85", StringComparison.Ordinal)));

        result.ScheduleChanges.Should().BeEquivalentTo(
            [
                new ScheduleChange("act.week12", ElementChangeKind.Added, null, new PlannedVisit("act.week12", "Week 12", "ep.treatment", 2, 84, 3, 3)),
                new ScheduleChange(
                    "act.week4",
                    ElementChangeKind.Changed,
                    new PlannedVisit("act.week4", "Week 4", "ep.treatment", 2, 28, 2, 2),
                    new PlannedVisit("act.week4", "Week 4", "ep.treatment", 2, 29, 2, 3)),
                new ScheduleChange("act.week8", ElementChangeKind.Removed, new PlannedVisit("act.week8", "Week 8", "ep.treatment", 2, 56, 3, 3), null),
            ],
            options => options.WithStrictOrdering());
        result.StructuralDiff.Should().Contain(c => c.ElementId == "act.screening", "a renamed visit is a structural change");
        dayLater.ScheduleChanges.Should().ContainSingle(c => c.ActivityId == "act.week12").Which.After!.StudyDay.Should().Be(85);
        dayLater.ResultHash.Should().NotBe(result.ResultHash);
    }

    /// <summary>
    /// Normalized SDTM study days treat Day 0 and Day 1 as the same study day on <see cref="SimulationResult.ScheduleChanges"/>
    /// while <see cref="ProtocolStructuralDiff"/> preserves raw JSON property changes.
    /// </summary>
    [Fact]
    public async Task Simulate_NormalizesDayZeroAndDayOneOnScheduleChangesWhilePreservingStructuralDiff()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Scheduled(Visit("act.baseline", "Baseline", "ep.treatment", 0, 0, 1))));
        var dayOneCandidate = Scheduled(Visit("act.baseline", "Baseline", "ep.treatment", 1, 0, 1));
        var addedDayZeroCandidate = Scheduled(
            Visit("act.baseline", "Baseline", "ep.treatment", 0, 0, 1),
            Visit("act.day0_extra", "Extra Day 0", "ep.treatment", 0, 0, 0));
        var engine = Engine(inputs);

        var normalized = await SimulateAsync(engine, SimulationCandidate.UnsavedProtocolVersion(parent.Id, dayOneCandidate));
        var added = await SimulateAsync(engine, SimulationCandidate.UnsavedProtocolVersion(parent.Id, addedDayZeroCandidate));

        normalized.ScheduleChanges.Should().BeEmpty("Day 0 and Day 1 resolve to the same SDTM study day");
        normalized.StructuralDiff.Should().ContainSingle(c => c.ElementId == "act.baseline")
            .Which.Properties.Should().Equal("/timing/day");
        added.ScheduleChanges.Should().ContainSingle().Which.Should().Be(
            new ScheduleChange("act.day0_extra", ElementChangeKind.Added, null, new PlannedVisit("act.day0_extra", "Extra Day 0", "ep.treatment", 2, 1, 0, 0)));
    }

    [Fact]
    public async Task Simulate_NeedsSimulationRunAtTheStudy()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Design));
        var probe = new Probe(permitted: false);
        var engine = Engine(inputs, probe: probe);

        var result = await engine.SimulateAsync(
            Tenant, new SimulationRequest(SimulationCandidate.UnsavedProtocolVersion(parent.Id, Design)), TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
        result.Error.Explanation.Should().NotBeNull();
        probe.Asked.Should().Equal((SimulationCapabilities.Run, NodeRef.For(Study)));
        SimulationCapabilities.Run.VerbClass.Should().Be(VerbClass.Read);
    }

    [Fact]
    public async Task Simulate_RejectsUnknownVersionsDraftBasesAndInvalidContent()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Design));
        var draft = inputs.Add(Draft(parent, Design));
        var engine = Engine(inputs);
        var ct = TestContext.Current.CancellationToken;

        async Task<string?> Code(SimulationCandidate candidate) =>
            (await engine.SimulateAsync(Tenant, new SimulationRequest(candidate), ct)).Error?.Code;

        (await Code(SimulationCandidate.StoredProtocolVersion(ProtocolVersionId.New()))).Should().Be(SimulationErrors.VersionUnknown.Code);
        (await Code(SimulationCandidate.UnsavedProtocolVersion(ProtocolVersionId.New(), Design))).Should().Be(SimulationErrors.VersionUnknown.Code);
        (await Code(SimulationCandidate.UnsavedProtocolVersion(draft.Id, Design))).Should().Be(SimulationErrors.BaseNotFrozen.Code);
        (await Code(SimulationCandidate.UnsavedProtocolVersion(parent.Id, "not json"))).Should().Be(SimulationErrors.ContentInvalid.Code);
        (await Code(SimulationCandidate.UnsavedProtocolVersion(parent.Id, "[]"))).Should().Be(SimulationErrors.ContentInvalid.Code);
    }

    /// <summary>
    /// Unsaved content is held to the rule a stored draft is held to (ADR-2472): JSON that is not a protocol design,
    /// such as a USDM document, is refused with the design problems named.
    /// </summary>
    [Fact]
    public async Task Simulate_RejectsUnsavedContentThatIsNotAProtocolDesign()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Design));
        var danglingArm = ProtocolDesignDocument.Write(ProtocolDesignSamples.MinimalDesign() with
        {
            Arms = [new Arm("arm.A", "Active", ArmType.Experimental, ["int.missing"])],
        });

        var usdm = await Engine(inputs).SimulateAsync(
            Tenant, new SimulationRequest(SimulationCandidate.UnsavedProtocolVersion(parent.Id, Base)), TestContext.Current.CancellationToken);
        var dangling = await Engine(inputs).SimulateAsync(
            Tenant, new SimulationRequest(SimulationCandidate.UnsavedProtocolVersion(parent.Id, danglingArm)), TestContext.Current.CancellationToken);

        usdm.Error!.Code.Should().Be(SimulationErrors.ContentInvalid.Code);
        dangling.Error!.Code.Should().Be(SimulationErrors.ContentInvalid.Code);
        dangling.Error.Message.Should().Contain("int.missing");
        inputs.DependentsAsked.Should().BeEmpty();
    }

    [Fact]
    public async Task Simulate_NeedsAnAccountableActor()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Design));
        var engine = new SimulationEngine(inputs, new Probe(permitted: true), new Hide(null, MaskReason.NotPermitted), new FixedActor(null), new FakeClock(Now));

        var result = await engine.SimulateAsync(
            Tenant, new SimulationRequest(SimulationCandidate.UnsavedProtocolVersion(parent.Id, Design)), TestContext.Current.CancellationToken);

        result.Error.Should().Be(KernelErrors.ActorUnresolved);
    }

    [Fact]
    [KernelRule("K-SIM-2")]
    [KernelRule("K-SIM-3")]
    public async Task Simulate_MultiKindCandidates_AreReproducibleAcrossProtocolMappingAndRulePack()
    {
        var inputs = new Inputs();
        var sbp = new ConceptRef(ConceptId.New(), 1);
        var stdId = StandardVersionId.New();
        var baseMapSpec = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(sbp));
        var changedMapSpec = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(sbp) with { Decimals = 1 });
        var baseMapItem = ReleasedLibraryItem(LibraryItemKind.MappingSpecification, "map-vitals", baseMapSpec.Title, MappingSpecificationJson.ToJson(baseMapSpec).GetRawText(), [stdId], version: 1);
        var draftMapItem = DraftLibraryItemView(LibraryItemKind.MappingSpecification, "map-vitals", changedMapSpec.Title, MappingSpecificationJson.ToJson(changedMapSpec).GetRawText(), [stdId], version: 2);

        var baseRulePack = BuildRulePack(("R-LOW-SBP", 1, 100m, "<"));
        var changedRulePack = BuildRulePack(("R-LOW-SBP", 2, 95m, "<"));
        var baseRuleItem = ReleasedLibraryItem(LibraryItemKind.RulePack, "rules-vitals", baseRulePack.Title, RulePackJson.ToJson(baseRulePack).GetRawText(), [], version: 1);
        var draftRuleItem = DraftLibraryItemView(LibraryItemKind.RulePack, "rules-vitals", changedRulePack.Title, RulePackJson.ToJson(changedRulePack).GetRawText(), [], version: 2);

        inputs.AddLibraryItem(baseMapItem);
        inputs.AddLibraryItem(draftMapItem);
        inputs.AddLibraryItem(baseRuleItem);
        inputs.AddLibraryItem(draftRuleItem);
        inputs.Snapshot = SimulationStudySnapshot.Empty with
        {
            Loaded = true,
            PinnedLibraryItems = [baseMapItem],
            PinnedRulePacks = [baseRuleItem],
            DeclaredStandardVersions = [stdId],
        };

        var parent = inputs.Add(Approved(Design));
        var draftProtocol = inputs.Add(Draft(parent, Renamed("arm.A", "Low dose")));
        var engine = Engine(inputs);

        var candidates = new[]
        {
            SimulationCandidate.StoredProtocolVersion(draftProtocol.Id),
            SimulationCandidate.UnsavedProtocolVersion(parent.Id, Renamed("arm.A", "Low dose")),
            SimulationCandidate.StoredMappingSpecification(Study, draftMapItem.Id, baseMapItem.Id),
            SimulationCandidate.UnsavedMappingSpecification(Study, MappingSpecificationJson.ToJson(changedMapSpec).GetRawText(), baseMapItem.Id, stdId),
            SimulationCandidate.StoredRulePack(Study, draftRuleItem.Id, baseRuleItem.Id),
            SimulationCandidate.UnsavedRulePack(Study, RulePackJson.ToJson(changedRulePack).GetRawText(), baseRuleItem.Id),
        };

        foreach (var candidate in candidates)
        {
            var first = await SimulateAsync(engine, candidate, Now);
            var second = await SimulateAsync(engine, candidate, Now);
            var later = await SimulateAsync(engine, candidate, Now.AddHours(6));

            second.InputHash.Should().Be(first.InputHash, $"{candidate.Kind} input hash must be reproducible");
            second.ResultHash.Should().Be(first.ResultHash, $"{candidate.Kind} result hash must be reproducible");
            later.InputHash.Should().NotBe(first.InputHash, $"{candidate.Kind} input hash pins (valid_at, known_at)");
            first.IsEmpty.Should().BeFalse($"{candidate.Kind} has real candidate changes");
        }

        var unchangedMap = await SimulateAsync(
            engine,
            SimulationCandidate.UnsavedMappingSpecification(Study, MappingSpecificationJson.ToJson(baseMapSpec).GetRawText(), baseMapItem.Id, stdId),
            Now);
        var unchangedRules = await SimulateAsync(
            engine,
            SimulationCandidate.UnsavedRulePack(Study, RulePackJson.ToJson(baseRulePack).GetRawText(), baseRuleItem.Id),
            Now);

        unchangedMap.IsEmpty.Should().BeTrue("an identical MappingSpecification candidate is empty (K-SIM-2)");
        unchangedRules.IsEmpty.Should().BeTrue("an identical RulePack candidate is empty (K-SIM-2)");
    }

    [Fact]
    [KernelRule("K-PROT-2")]
    [KernelRule("K-TIME-7")]
    public async Task Simulate_ReportsOperationalCounts_ParticipantScheduleShifts_AndTransitionCohorts()
    {
        var inputs = new Inputs();
        var baseContent = Scheduled(
            Visit("act.screening", "Screening", "ep.screening", 1, 0, 0),
            Visit("act.week4", "Week 4", "ep.treatment", 28, 2, 2));
        var candidateContent = Scheduled(
            Visit("act.screening", "Screening", "ep.screening", 1, 0, 0),
            Visit("act.week4", "Week 4", "ep.treatment", 30, 2, 3),
            Visit("act.week8", "Week 8", "ep.treatment", 56, 2, 2));

        var parent = inputs.Add(Approved(baseContent));
        var amendment = new ProtocolAmendment(
            "Extend treatment schedule",
            "Align Week 4 window and add Week 8 follow-up",
            AmendmentClassification.Substantial,
            "Moves Week 4 from Day 28 to Day 30 and adds Week 8",
            ["act.week4", "act.week8"],
            TransitionPolicy.RequiresReconsent);
        var draft = inputs.Add(Draft(parent, candidateContent, amendment));

        var siteActive1 = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "US", "Active", IsActive: true, Now.AddDays(-30));
        var siteActive2 = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "DE", "Active", IsActive: true, Now.AddDays(-25));
        var sitePending = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "JP", "Selected", IsActive: false, Now.AddDays(-5));
        var effStudy = new SimulationEffectivitySnapshot(EffectivityId.New(), parent.Id, EffectivityScope.Study(), EffectivityStatus.Effective);
        var effPendingSite = new SimulationEffectivitySnapshot(EffectivityId.New(), parent.Id, EffectivityScope.At(sitePending.Id), EffectivityStatus.Pending);

        var start1 = new DateOnly(2026, 9, 1);
        var start2 = new DateOnly(2026, 9, 10);
        var enr1 = new SimulationEnrollmentSnapshot(EnrollmentId.New(), siteActive1.Id, "SUBJ-001", EnrollmentStatus.Enrolled, start1);
        var enr2 = new SimulationEnrollmentSnapshot(EnrollmentId.New(), siteActive2.Id, "SUBJ-002", EnrollmentStatus.Screening, start2);
        var enrWithdrawn = new SimulationEnrollmentSnapshot(EnrollmentId.New(), siteActive1.Id, "SUBJ-003", EnrollmentStatus.Withdrawn, start1);

        var completedScreening = new SimulationCollectionSnapshot(
            CollectionInstanceId.New(),
            enr1.Id,
            siteActive1.Id,
            parent.Id,
            "cs.screening",
            "act.screening",
            Now.AddDays(-20),
            Now.AddDays(-20),
            new Dictionary<string, SimulationCollectedItemSnapshot>(StringComparer.Ordinal));

        inputs.Snapshot = SimulationStudySnapshot.Empty with
        {
            Loaded = true,
            Sites = [siteActive1, siteActive2, sitePending],
            Effectivities = [effStudy, effPendingSite],
            Enrollments = [enr1, enr2, enrWithdrawn],
            Collections = [completedScreening],
            ProtocolDesigns = new Dictionary<ProtocolVersionId, ProtocolDesign>
            {
                [parent.Id] = ProtocolDesignDocument.Read(baseContent).Design!,
            },
        };

        var result = await SimulateAsync(Engine(inputs), SimulationCandidate.StoredProtocolVersion(draft.Id), Now);

        result.OperationalImpact.Should().NotBeNull();
        result.OperationalImpact!.ActiveSiteCount.Should().Be(2);
        result.OperationalImpact.PendingSiteCount.Should().Be(1);
        result.OperationalImpact.TotalParticipantCount.Should().Be(3);
        result.OperationalImpact.AffectedParticipantCount.Should().Be(2, "enrolled and screened participants are non-terminal; withdrawn is unaffected");
        result.OperationalImpact.ReconsentParticipantCount.Should().Be(2, "TransitionPolicy.RequiresReconsent flags non-terminal impacted participants");
        result.OperationalImpact.Hidden.Should().Be(HiddenObjects.None);

        var p1 = result.OperationalImpact.ParticipantImpacts.Single(p => p.SubjectCode == "SUBJ-001");
        p1.Cohort.Should().Be(ParticipantImpactCohort.RequiresReconsent);
        p1.RequiresReconsent.Should().BeTrue();
        p1.ScheduleShifts.Select(s => s.ActivityId).Should().Equal("act.week4", "act.week8");
        var week4Shift = p1.ScheduleShifts.Single(s => s.ActivityId == "act.week4");
        week4Shift.Before!.TargetDate.Should().Be(start1.AddDays(27));
        week4Shift.After!.TargetDate.Should().Be(start1.AddDays(29));

        var withdrawn = result.OperationalImpact.ParticipantImpacts.Single(p => p.SubjectCode == "SUBJ-003");
        withdrawn.Cohort.Should().Be(ParticipantImpactCohort.Unaffected);
        withdrawn.ScheduleShifts.Should().BeEmpty();
    }

    [Fact]
    [KernelRule("K-PROJ-2")]
    [KernelRule("K-RULE-4")]
    [KernelRule("K-FIND-1")]
    public async Task Simulate_EvaluatesProjectionChangesAndValidationDelta_ThroughPurePorts()
    {
        var inputs = new Inputs();
        var sbp = new ConceptRef(ConceptId.New(), 1);
        var stdId = StandardVersionId.New();

        var baseMap = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(sbp));
        var candidateMap = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(sbp) with { Decimals = 1 });
        var baseMapItem = ReleasedLibraryItem(LibraryItemKind.MappingSpecification, "map-vs", baseMap.Title, MappingSpecificationJson.ToJson(baseMap).GetRawText(), [stdId], version: 1);

        // Baseline rule flags VSSTRESN < 100 (row 1 = 12 kPa = 90.0 mmHg violates; row 2 = 18 kPa = 135.0 mmHg passes).
        // Candidate rule flags VSSTRESN > 130 (row 1 resolves; row 2 opens!).
        var basePack = BuildRulePack(("R-VS-RANGE", 1, 100m, "<"));
        var candidatePack = BuildRulePack(("R-VS-RANGE", 2, 130m, ">"));
        var basePackItem = ReleasedLibraryItem(LibraryItemKind.RulePack, "rules-vs", basePack.Title, RulePackJson.ToJson(basePack).GetRawText(), [], version: 1);

        var site = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "US", "Active", IsActive: true, Now.AddDays(-10));
        var enr = new SimulationEnrollmentSnapshot(EnrollmentId.New(), site.Id, "SUBJ-101", EnrollmentStatus.Enrolled, new DateOnly(2026, 9, 1));
        var obsLow = new SimulationObservationSnapshot(ObservationId.New(), enr.Id, site.Id, sbp, new QuantityValue(12m, "kPa"));
        var obsHigh = new SimulationObservationSnapshot(ObservationId.New(), enr.Id, site.Id, sbp, new QuantityValue(18m, "kPa"));

        inputs.AddLibraryItem(baseMapItem);
        inputs.AddLibraryItem(basePackItem);
        inputs.Snapshot = SimulationStudySnapshot.Empty with
        {
            Loaded = true,
            Sites = [site],
            Enrollments = [enr],
            Observations = [obsLow, obsHigh],
            PinnedLibraryItems = [baseMapItem],
            PinnedRulePacks = [basePackItem],
            DeclaredStandardVersions = [stdId],
        };

        var engine = Engine(inputs);

        var mapSim = await SimulateAsync(
            engine,
            SimulationCandidate.UnsavedMappingSpecification(Study, MappingSpecificationJson.ToJson(candidateMap).GetRawText(), baseMapItem.Id, stdId),
            Now);

        var vsChange = mapSim.ProjectionChanges.Should().ContainSingle(d => d.Dataset == "VS").Subject;
        vsChange.ChangedCount.Should().Be(2, "changing Decimals from 0 to 1 updates converted kPa -> mmHg VSSTRESC/VSSTRESN values");
        vsChange.Samples.Should().HaveCount(2);
        vsChange.Samples.Select(s => (Before: s.Before!["VSSTRESC"], After: s.After!["VSSTRESC"]))
            .Should().BeEquivalentTo([("90", "90.0"), ("135", "135.0")]);

        var ruleSim = await SimulateAsync(
            engine,
            SimulationCandidate.UnsavedRulePack(Study, RulePackJson.ToJson(candidatePack).GetRawText(), basePackItem.Id),
            Now);

        ruleSim.ValidationDelta.Should().NotBeNull();
        ruleSim.ValidationDelta!.Resolving.Should().ContainSingle()
            .Which.RuleId.Should().Be("R-VS-RANGE");
        ruleSim.ValidationDelta.Opening.Should().ContainSingle()
            .Which.RuleId.Should().Be("R-VS-RANGE");
        ruleSim.ValidationDelta.UnchangedCount.Should().Be(0);
    }

    [Fact]
    [KernelRule("K-SIM-4")]
    [KernelRule("K-AUTH-4")]
    [KernelRule("K-IDN-1")]
    public async Task Simulate_MasksUnauthorizedSitesAndBlindedTreatmentBeforeHashing()
    {
        var inputs = new Inputs();
        var sbp = new ConceptRef(ConceptId.New(), 1);
        var armConceptId = ConceptId.New();
        var armConcept = new ConceptRef(armConceptId, 1);
        var stdId = StandardVersionId.New();

        var armMapping = new VariableMapping(
            "dm-armcd",
            armConcept,
            "VS",
            new SortedDictionary<string, string>(StringComparer.Ordinal) { ["VSTEST"] = "Assigned Arm", ["VSTESTCD"] = "ARMCD" },
            new ResultVariables("VSORRES"),
            null,
            [],
            null,
            null);
        var mapSpec = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(sbp), armMapping);
        var mapItem = ReleasedLibraryItem(LibraryItemKind.MappingSpecification, "map-blinded", mapSpec.Title, MappingSpecificationJson.ToJson(mapSpec).GetRawText(), [stdId], version: 1);
        inputs.AddLibraryItem(mapItem);

        var parent = inputs.Add(Approved(Design));
        var draft = inputs.Add(Draft(parent, Renamed("arm.A", "Low dose")));

        var visibleSite = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "US", "Active", IsActive: true, Now.AddDays(-15));
        var hiddenSite = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "FR", "Active", IsActive: true, Now.AddDays(-15));
        var visibleEnr = new SimulationEnrollmentSnapshot(EnrollmentId.New(), visibleSite.Id, "SUBJ-VIS-1", EnrollmentStatus.Enrolled, new DateOnly(2026, 9, 1));
        var hiddenEnr = new SimulationEnrollmentSnapshot(EnrollmentId.New(), hiddenSite.Id, "SUBJ-HID-1", EnrollmentStatus.Enrolled, new DateOnly(2026, 9, 2));
        var visibleObs = new SimulationObservationSnapshot(ObservationId.New(), visibleEnr.Id, visibleSite.Id, sbp, new QuantityValue(120m, "mm[Hg]"));
        var blindedObs = new SimulationObservationSnapshot(ObservationId.New(), visibleEnr.Id, visibleSite.Id, armConcept, new TextValue("arm.A"));
        var hiddenObs = new SimulationObservationSnapshot(ObservationId.New(), hiddenEnr.Id, hiddenSite.Id, sbp, new QuantityValue(140m, "mm[Hg]"));

        SimulationStudySnapshot BuildSnapshot(string blindedArmCode, string hiddenSubjectCode, decimal hiddenSbp) =>
            SimulationStudySnapshot.Empty with
            {
                Loaded = true,
                Sites = [visibleSite, hiddenSite],
                Enrollments = [visibleEnr, hiddenEnr with { SubjectCode = hiddenSubjectCode }],
                Observations =
                [
                    visibleObs,
                    blindedObs with { Value = new TextValue(blindedArmCode) },
                    hiddenObs with { Value = new QuantityValue(hiddenSbp, "mm[Hg]") },
                ],
                Concepts = new Dictionary<ConceptId, BlindedConceptMetadata>
                {
                    [armConceptId] = new("ARMCD", "Treatment Arm Code", "Randomized treatment arm assignment"),
                },
                PinnedLibraryItems = [mapItem],
                DeclaredStandardVersions = [stdId],
            };

        inputs.Snapshot = BuildSnapshot("arm.A", "SUBJ-HID-1", 140m);
        var visibility = new Hide(NodeRef.For(hiddenSite.Id), MaskReason.NotPermitted);
        var probe = new BlindedProbe();
        var engine = Engine(inputs, visibility, probe);

        var first = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(draft.Id), Now);

        // Mutate hidden site enrollment and blinded treatment assignment — hashes and visible counts must not move!
        inputs.Snapshot = BuildSnapshot("arm.B", "SUBJ-HID-999", 195m);
        var afterHiddenMutation = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(draft.Id), Now);

        first.OperationalImpact.Should().NotBeNull();
        first.OperationalImpact!.ActiveSiteCount.Should().Be(1, "unauthorized site is excluded from reader-scoped counts (ADR-2590)");
        first.OperationalImpact.TotalParticipantCount.Should().Be(1);
        first.OperationalImpact.Hidden.MoreExist.Should().BeTrue();
        first.OperationalImpact.Hidden.Reasons.Should().Contain([MaskReason.NotPermitted, MaskReason.Blinded]);
        first.Masked.Should().Contain([
            new MaskedPart(SimulationSnapshotMasking.SitesPart, MaskReason.NotPermitted),
            new MaskedPart(SimulationSnapshotMasking.ParticipantsPart, MaskReason.NotPermitted),
            new MaskedPart(SimulationSnapshotMasking.BlindedTreatmentPart, MaskReason.Blinded),
        ]);
        first.OperationalImpact.ParticipantImpacts.Select(p => p.SubjectCode).Should().Equal("SUBJ-VIS-1");

        afterHiddenMutation.InputHash.Should().Be(first.InputHash, "mutating hidden site or blinded arm data must not alter input_hash (K-SIM-4)");
        afterHiddenMutation.ResultHash.Should().Be(first.ResultHash, "mutating hidden site or blinded arm data must not alter result_hash (K-SIM-4)");
    }

    [Fact]
    public async Task Simulate_EnforcesVersionPinsAndAllowsUnversionedLatestReads()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Design));
        var draft = inputs.Add(Draft(parent, Renamed("arm.A", "Low dose")));
        var probe = new Probe(permitted: true);
        var visibility = new Hide(null, MaskReason.NotPermitted);
        var engine = Engine(inputs, visibility, probe);
        var ct = TestContext.Current.CancellationToken;

        var matchingStored = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(draft.Id, draft.VersionIndex));
        matchingStored.Candidate.Version.Should().Be(NodeRef.For(draft.Id, draft.VersionIndex));

        var matchingUnsaved = await SimulateAsync(engine, SimulationCandidate.UnsavedProtocolVersion(parent.Id, Design, parent.VersionIndex));
        matchingUnsaved.Candidate.BaseVersion.Should().Be(NodeRef.For(parent.Id, parent.VersionIndex));

        probe.Asked.Clear();
        visibility.Asked.Clear();
        inputs.DependentsAsked.Clear();

        var staleStored = await engine.SimulateAsync(
            Tenant, new SimulationRequest(SimulationCandidate.StoredProtocolVersion(draft.Id, draft.VersionIndex + 1)), ct);
        var staleBase = await engine.SimulateAsync(
            Tenant, new SimulationRequest(SimulationCandidate.UnsavedProtocolVersion(parent.Id, Design, parent.VersionIndex - 1)), ct);

        staleStored.Error!.Code.Should().Be(SimulationErrors.VersionStale.Code);
        staleStored.Error.HttpStatus.Should().Be(409);
        staleBase.Error!.Code.Should().Be(SimulationErrors.VersionStale.Code);
        staleBase.Error.HttpStatus.Should().Be(409);
        probe.Asked.Should().BeEmpty("stale @version pins fail fast before authorization or evaluation");
        visibility.Asked.Should().BeEmpty();
        inputs.DependentsAsked.Should().BeEmpty();

        // Advancing VersionIndex keeps unversioned SimulationCandidates evaluating the latest snapshot.
        inputs.Replace(draft with { VersionIndex = 7 });
        inputs.Replace(parent with { VersionIndex = 9 });
        var latestStored = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(draft.Id));
        var latestUnsaved = await SimulateAsync(engine, SimulationCandidate.UnsavedProtocolVersion(parent.Id, Design));

        latestStored.Candidate.Version.Should().Be(NodeRef.For(draft.Id, 7));
        latestStored.Candidate.BaseVersion.Should().Be(NodeRef.For(parent.Id, 9));
        latestUnsaved.Candidate.BaseVersion.Should().Be(NodeRef.For(parent.Id, 9));
    }

    [Fact]
    [KernelRule("K-SIM-3")]
    public async Task Simulate_RecordsMatrixGapEngineAndSealsIchM11ViolationsInHashes()
    {
        var concept = new ConceptRef(ConceptId.New(), 1);
        var codeSystem = CodeSystemVersionId.New();
        var complete = ProtocolDesignSamples.Complete(concept, codeSystem) with
        {
            Estimands = [],
            EligibilityCriteria =
            [
                new EligibilityCriterion("ic.1", CriterionCategory.Inclusion, "SBP >= 140 mmHg.", [concept], "sbp >= 140"),
                new EligibilityCriterion("ec.1", CriterionCategory.Exclusion, "Recent MI.", [concept]),
            ],
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(
                    "act.week12",
                    "Week 12 visit",
                    "ep.treatment",
                    [ActivityLocation.Site, ActivityLocation.Home],
                    ["cs.vitals"],
                    new ActivityTiming(84, 3, 3),
                    Purpose: new DataPurpose { EndpointIds = ["ep.1"], SafetyRuleIds = ["safety.1"] }),
            ],
        };
        var completeJson = ProtocolDesignDocument.Write(complete);
        var ichViolationOnlyJson = ProtocolDesignDocument.Write(complete with { Populations = [] });

        var inputs = new Inputs();
        var cleanParent = inputs.Add(Approved(completeJson));
        var violationParent = inputs.Add(Approved(ichViolationOnlyJson));
        var engine = Engine(inputs);

        var cleanRun = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(cleanParent.Id));
        var ichOnlyRun = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(violationParent.Id));

        cleanRun.MatrixGaps.Should().BeEmpty();
        cleanRun.EngineVersions.Should().Contain(SimulationEngine.MatrixGapEngine);

        ichOnlyRun.MatrixGaps.Should().NotBeEmpty()
            .And.OnlyContain(g => g.Kind == MatrixGapKind.IchM11Violation);
        ichOnlyRun.EngineVersions.Should().Equal(cleanRun.EngineVersions);
        ichOnlyRun.ResultHash.Should().NotBe(cleanRun.ResultHash);
    }

    [Fact]
    [KernelRule("K-SIM-3")]
    [KernelRule("K-SIM-5")]
    public async Task SimulationResultHashing_StreamingHashMatchesCanonicalJsonAndPreservesArtifactBinding()
    {
        var sbp = new ConceptRef(ConceptId.New(), 1);
        var stdId = StandardVersionId.New();
        var mapSpec = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(sbp));
        var mapItem = ReleasedLibraryItem(
            LibraryItemKind.MappingSpecification,
            "sdtm-vs",
            mapSpec.Title,
            MappingSpecificationJson.ToJson(mapSpec).GetRawText(),
            [stdId],
            1);
        var rulePack = BuildRulePack(("R-VS-HIGH", 1, 130m, ">"));
        var ruleItem = ReleasedLibraryItem(
            LibraryItemKind.RulePack,
            "rules-vs",
            rulePack.Title,
            RulePackJson.ToJson(rulePack).GetRawText(),
            [],
            1);

        var baseDesign = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities = [new PlannedActivity("act.day1", "Day 1", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(1, 1, 1))],
            CollectionSpecs = [new CollectionSpec("cs.vitals", "Vitals", [new CollectionItem("sbp", sbp, Unit: "mm[Hg]", Required: true)])],
        };
        var candDesign = baseDesign with
        {
            PlannedActivities = [new PlannedActivity("act.day1", "Day 1", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(2, 1, 2))],
        };

        var inputs = new Inputs();
        var parent = inputs.Add(Approved(ProtocolDesignDocument.Write(baseDesign)));
        var amendment = new ProtocolAmendment(
            "Shift Day 1",
            "Shift visit window",
            AmendmentClassification.Substantial,
            "Move target day",
            ["act.day1"],
            TransitionPolicy.RequiresReconsent);
        var draftContent = ProtocolDesignDocument.Write(candDesign);
        var draftHash = ContentHash.Compute(draftContent);
        var draft = inputs.Add(Draft(parent, draftContent, amendment) with { ContentHash = draftHash });

        var site = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "US", "Active", IsActive: true, Now.AddDays(-30));
        var hiddenSite = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "DE", "Active", IsActive: true, Now.AddDays(-30));
        var enrollment = new SimulationEnrollmentSnapshot(EnrollmentId.New(), site.Id, "SUBJ-01", EnrollmentStatus.Enrolled, new DateOnly(2026, 9, 1));
        var collectionId = CollectionInstanceId.New();
        var obsId = ObservationId.New();
        var obsVal = new QuantityValue(145m, "mm[Hg]");

        inputs.Snapshot = new SimulationStudySnapshot(
            Loaded: true,
            Sites: [site, hiddenSite],
            Effectivities: [new SimulationEffectivitySnapshot(EffectivityId.New(), parent.Id, EffectivityScope.Study(), EffectivityStatus.Effective)],
            Enrollments: [enrollment],
            Observations: [new SimulationObservationSnapshot(obsId, enrollment.Id, site.Id, sbp, obsVal, collectionId, parent.Id, "cs.vitals", "act.day1", "sbp")],
            Collections:
            [
                new SimulationCollectionSnapshot(
                    collectionId,
                    enrollment.Id,
                    site.Id,
                    parent.Id,
                    "cs.vitals",
                    "act.day1",
                    Now.AddDays(-5),
                    Now.AddDays(-5),
                    new Dictionary<string, SimulationCollectedItemSnapshot>(StringComparer.Ordinal)
                    {
                        ["sbp"] = new(obsId, sbp, obsVal),
                    }),
            ],
            ProtocolDesigns: new Dictionary<ProtocolVersionId, ProtocolDesign> { [parent.Id] = baseDesign },
            Concepts: new Dictionary<ConceptId, BlindedConceptMetadata> { [sbp.ConceptId] = new("SBP", "Systolic BP", null) },
            PinnedLibraryItems: [mapItem],
            PinnedRulePacks: [ruleItem],
            DeclaredStandardVersions: [stdId],
            CodelistDecodes: new Dictionary<(string Codelist, string Code), string>(),
            DeclaredStandards: [("SDTMIG", "3.4")]);

        var engine = Engine(inputs, visibility: new Hide(NodeRef.For(hiddenSite.Id), MaskReason.NotPermitted));
        var result = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(draft.Id));

        var canonicalJson = SimulationResultHashing.ToCanonicalJson(result);
        result.ResultHash.Should().Be(ContentHash.Compute(canonicalJson));
        SimulationResultHashing.ComputeResultHash(result).Should().Be(result.ResultHash);

        var artifactContent = SimulationArtifactBinding.ToArtifactContent(result);
        artifactContent.Should().NotBeNull();
        artifactContent!.ContentHash.Should().Be(result.ResultHash);
        artifactContent.SizeBytes.Should().Be(Encoding.UTF8.GetByteCount(canonicalJson));
        SimulationArtifactBinding.TryParseStorageRef(artifactContent.StorageRef, out var binding).Should().BeTrue();
        binding!.MatchesProtocolAmendment(parent.Id, parent.ContentHash!, draft.Id, draftHash).Should().BeTrue();
    }

    [Fact]
    [KernelRule("K-SIM-3")]
    [KernelRule("K-SIM-4")]
    public async Task MaskAsync_SkipsDigestWhenOmittedAndMatchesCanonicalDigestAcrossValueKinds()
    {
        var siteA = new SimulationSiteSnapshot(
            new StudySiteId(Guid.Parse("00000000-0000-0000-0000-000000000020")),
            new SiteId(Guid.Parse("00000000-0000-0000-0000-000000000120")),
            "DE <&>",
            "Active",
            IsActive: true,
            Now.AddDays(-20));
        var siteB = new SimulationSiteSnapshot(
            new StudySiteId(Guid.Parse("00000000-0000-0000-0000-000000000010")),
            new SiteId(Guid.Parse("00000000-0000-0000-0000-000000000110")),
            "US",
            "Closed",
            IsActive: false,
            Now.AddDays(-15));
        var hiddenSite = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "JP", "Active", IsActive: true, Now.AddDays(-10));

        var protoId = new ProtocolVersionId(Guid.Parse("00000000-0000-0000-0000-000000000300"));
        var effNull = new SimulationEffectivitySnapshot(
            new EffectivityId(Guid.Parse("00000000-0000-0000-0000-000000000050")),
            protoId,
            EffectivityScope.Study(),
            StatusAsOf: null);
        var effActive = new SimulationEffectivitySnapshot(
            new EffectivityId(Guid.Parse("00000000-0000-0000-0000-000000000040")),
            protoId,
            EffectivityScope.Study(),
            EffectivityStatus.Effective);

        var enr = new SimulationEnrollmentSnapshot(
            new EnrollmentId(Guid.Parse("00000000-0000-0000-0000-000000000060")),
            siteA.Id,
            "SUBJ-\"01\" <&>",
            EnrollmentStatus.Enrolled,
            new DateOnly(2026, 8, 15));

        var concept1 = new ConceptRef(new ConceptId(Guid.Parse("00000000-0000-0000-0000-000000000701")), 2);
        var concept2 = new ConceptRef(new ConceptId(Guid.Parse("00000000-0000-0000-0000-000000000702")), 1);
        var codeSys = new CodeSystemVersionId(Guid.Parse("00000000-0000-0000-0000-000000000801"));
        var colId = new CollectionInstanceId(Guid.Parse("00000000-0000-0000-0000-000000000901"));

        var obsValues = new (Guid Id, string ItemKey, ConceptRef Concept, ObservationValue Value, string? ActivityId)[]
        {
            (Guid.Parse("00000000-0000-0000-0000-000000000a05"), "z_qty", concept1, new QuantityValue(12.50m, "mg/dL <&>", QuantityComparator.GreaterOrEqual), "act.1"),
            (Guid.Parse("00000000-0000-0000-0000-000000000a04"), "m_bool", concept2, new BooleanValue(true), null),
            (Guid.Parse("00000000-0000-0000-0000-000000000a03"), "c_coded", concept1, new CodedValue(codeSys, "POS <&>"), "act.1"),
            (Guid.Parse("00000000-0000-0000-0000-000000000a02"), "b_dt", concept2, new DateTimeValue("2026-09-01T14:30:00+02:00"), "act.1"),
            (Guid.Parse("00000000-0000-0000-0000-000000000a01"), "a_miss_detail", concept1, new MissingValue(NullFlavor.NotDone, "equip <&>"), "act.1"),
            (Guid.Parse("00000000-0000-0000-0000-000000000a00"), "0_miss_plain", concept2, new MissingValue(NullFlavor.Unknown), null),
        };

        var observations = obsValues
            .Select(o => new SimulationObservationSnapshot(
                new ObservationId(o.Id),
                enr.Id,
                siteA.Id,
                o.Concept,
                o.Value,
                colId,
                protoId,
                "cs.lab",
                o.ActivityId,
                o.ItemKey))
            .ToList();

        var itemMap = obsValues.ToDictionary(
            o => o.ItemKey,
            o => new SimulationCollectedItemSnapshot(new ObservationId(o.Id), o.Concept, o.Value),
            StringComparer.Ordinal);
        var collection = new SimulationCollectionSnapshot(colId, enr.Id, siteA.Id, protoId, "cs.lab", null, Now, Now, itemMap);

        var snapshot = new SimulationStudySnapshot(
            Loaded: true,
            Sites: [siteA, siteB, hiddenSite],
            Effectivities: [effNull, effActive],
            Enrollments: [enr],
            Observations: observations,
            Collections: [collection],
            ProtocolDesigns: new Dictionary<ProtocolVersionId, ProtocolDesign>(),
            Concepts: new Dictionary<ConceptId, BlindedConceptMetadata>(),
            PinnedLibraryItems: [],
            PinnedRulePacks: [],
            DeclaredStandardVersions: [],
            CodelistDecodes: new Dictionary<(string Codelist, string Code), string>(),
            DeclaredStandards: []);

        var visibility = new Hide(NodeRef.For(hiddenSite.Id), MaskReason.NotPermitted);
        var probe = new Probe(permitted: true);
        var actor = Actor.Person("reviewer@cadence.test");

        var withDigest = await SimulationSnapshotMasking.MaskAsync(
            Tenant,
            Study,
            actor,
            snapshot,
            extraDesigns: [],
            visibility,
            BlindedTreatmentClassifier.Instance,
            probe,
            TestContext.Current.CancellationToken,
            includeVisibleDigest: true);

        var withoutDigest = await SimulationSnapshotMasking.MaskAsync(
            Tenant,
            Study,
            actor,
            snapshot,
            extraDesigns: [],
            visibility,
            BlindedTreatmentClassifier.Instance,
            probe,
            TestContext.Current.CancellationToken,
            includeVisibleDigest: false);

        withoutDigest.VisibleStateDigest.Should().BeEmpty();
        withoutDigest.VisibleSites.Should().Equal(withDigest.VisibleSites);
        withoutDigest.VisibleObservations.Should().Equal(withDigest.VisibleObservations);
        withoutDigest.Hidden.Should().BeEquivalentTo(withDigest.Hidden);

        var expectedReferenceDigest = ComputeReferenceJsonNodeDigest(
            withDigest.VisibleSites,
            withDigest.VisibleEffectivities,
            withDigest.VisibleEnrollments,
            withDigest.VisibleObservations,
            withDigest.VisibleCollections);
        withDigest.VisibleStateDigest.Should().Be(expectedReferenceDigest);
    }

    [Fact]
    [KernelRule("K-SIM-2")]
    [KernelRule("K-SIM-3")]
    public async Task Simulate_SinglePassProjection_RunsOnceWhenMappingsAndConceptBindingsMatchAndTwiceWhenChanged()
    {
        var sbp = new ConceptRef(ConceptId.New(), 1);
        var hr = new ConceptRef(ConceptId.New(), 1);
        var stdId = StandardVersionId.New();

        var baseMapSpec = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(sbp));
        var candMapSpec = SampleMappingSpecifications.Specification(
            SampleMappingSpecifications.SystolicBloodPressure(sbp) with { Decimals = 2 });
        var baseMapJson = MappingSpecificationJson.ToJson(baseMapSpec).GetRawText();
        var candMapJson = MappingSpecificationJson.ToJson(candMapSpec).GetRawText();

        var mapItem = ReleasedLibraryItem(LibraryItemKind.MappingSpecification, "sdtm-vs", baseMapSpec.Title, baseMapJson, [stdId], 1);
        var baseRulePack = BuildRulePack(("R-VS-1", 1, 140m, ">"));
        var candRulePack = BuildRulePack(("R-VS-1", 2, 130m, ">"));
        var ruleItem = ReleasedLibraryItem(
            LibraryItemKind.RulePack,
            "rules-vs",
            baseRulePack.Title,
            RulePackJson.ToJson(baseRulePack).GetRawText(),
            [],
            1);

        var baseDesign = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities = [new PlannedActivity("act.v1", "Visit 1", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(1, 1, 1))],
            CollectionSpecs = [new CollectionSpec("cs.vitals", "Vitals", [new CollectionItem("sbp", sbp, Unit: "mm[Hg]", Required: true)])],
        };
        var scheduleOnlyDesign = baseDesign with
        {
            PlannedActivities = [new PlannedActivity("act.v1", "Visit 1", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(3, 1, 2))],
        };
        var reboundConceptDesign = baseDesign with
        {
            CollectionSpecs = [new CollectionSpec("cs.vitals", "Vitals", [new CollectionItem("sbp", hr, Unit: "mm[Hg]", Required: true)])],
        };

        var inputs = new Inputs();
        var parent = inputs.Add(Approved(ProtocolDesignDocument.Write(baseDesign)));
        var scheduleDraft = inputs.Add(Draft(parent, ProtocolDesignDocument.Write(scheduleOnlyDesign)));
        var reboundDraft = inputs.Add(Draft(parent, ProtocolDesignDocument.Write(reboundConceptDesign)));
        inputs.AddLibraryItem(mapItem);
        inputs.AddLibraryItem(ruleItem);

        var site = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "US", "Active", IsActive: true, Now.AddDays(-20));
        var enrollment = new SimulationEnrollmentSnapshot(EnrollmentId.New(), site.Id, "SUBJ-01", EnrollmentStatus.Enrolled, new DateOnly(2026, 9, 1));
        var observations = Enumerable.Range(0, 5)
            .Select(i => new SimulationObservationSnapshot(
                ObservationId.New(),
                enrollment.Id,
                site.Id,
                sbp,
                new QuantityValue(125m + (i * 5m), "mm[Hg]"),
                CollectionInstanceId.New(),
                parent.Id,
                "cs.vitals",
                "act.v1",
                "sbp"))
            .ToList();

        inputs.Snapshot = new SimulationStudySnapshot(
            Loaded: true,
            Sites: [site],
            Effectivities: [new SimulationEffectivitySnapshot(EffectivityId.New(), parent.Id, EffectivityScope.Study(), EffectivityStatus.Effective)],
            Enrollments: [enrollment],
            Observations: observations,
            Collections: [],
            ProtocolDesigns: new Dictionary<ProtocolVersionId, ProtocolDesign> { [parent.Id] = baseDesign },
            Concepts: new Dictionary<ConceptId, BlindedConceptMetadata>(),
            PinnedLibraryItems: [mapItem],
            PinnedRulePacks: [ruleItem],
            DeclaredStandardVersions: [stdId],
            CodelistDecodes: new Dictionary<(string Codelist, string Code), string>(),
            DeclaredStandards: [("SDTMIG", "3.4")]);

        var countingMapper = new CountingMappingEngine(new MappingEngine());
        var engine = new SimulationEngine(
            inputs,
            new Probe(permitted: true),
            new Hide(null, MaskReason.NotPermitted),
            new FixedActor(Actor.Person("designer@cadence.test")),
            new FakeClock(Now),
            mappingEngine: countingMapper);

        countingMapper.MapCalls = 0;
        var ruleSim = await SimulateAsync(
            engine,
            SimulationCandidate.UnsavedRulePack(Study, RulePackJson.ToJson(candRulePack).GetRawText(), ruleItem.Id));
        countingMapper.MapCalls.Should().Be(observations.Count, "unchanged mappings and concept bindings project once");
        ruleSim.ValidationDelta!.Opening.Should().NotBeEmpty();

        countingMapper.MapCalls = 0;
        _ = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(scheduleDraft.Id));
        countingMapper.MapCalls.Should().Be(observations.Count, "schedule-only protocol changes keep identical concept bindings");

        countingMapper.MapCalls = 0;
        _ = await SimulateAsync(
            engine,
            SimulationCandidate.UnsavedMappingSpecification(Study, candMapJson, mapItem.Id, stdId));
        countingMapper.MapCalls.Should().Be(observations.Count * 2, "changed mapping specifications project baseline and candidate separately");

        countingMapper.MapCalls = 0;
        _ = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(reboundDraft.Id));
        countingMapper.MapCalls.Should().Be(observations.Count, "rebound concept has no mapping spec on candidate so baseline maps 5 and candidate skips unmapped");
    }

    [Fact]
    [KernelRule("K-SIM-2")]
    [KernelRule("K-SIM-3")]
    public async Task Simulate_SiblingFormIndexAndResettableCursor_MatchesCrossFormPrecedenceAndReusesUnchangedStage()
    {
        var conceptA = new ConceptRef(ConceptId.New(), 1);
        var conceptB = new ConceptRef(ConceptId.New(), 1);

        var baseDesign = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity("act.base", "Baseline", "ep.treatment", [ActivityLocation.Site], ["cs.baseline"], new ActivityTiming(1, 1, 1)),
                new PlannedActivity("act.fu", "Follow-up", "ep.treatment", [ActivityLocation.Site], ["cs.followup"], new ActivityTiming(8, 1, 1)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.baseline",
                    "Baseline Form",
                    [
                        new CollectionItem("base_only", conceptA, Unit: "mg", Required: true),
                    ]),
                new CollectionSpec(
                    "cs.followup",
                    "Follow-up Form",
                    [
                        new CollectionItem("fu_val", conceptA, Unit: "mg", Required: true),
                        new CollectionItem("shared_item", conceptB, Unit: "mg", Required: false),
                    ]),
            ],
        };
        var scheduleAmendedDesign = baseDesign with
        {
            PlannedActivities =
            [
                baseDesign.PlannedActivities[0],
                baseDesign.PlannedActivities[1] with { Timing = new ActivityTiming(10, 2, 2) },
            ],
        };

        var guidanceUri = new UriBuilder(Uri.UriSchemeHttps, "example.org") { Path = "collection-rules" }.Uri.AbsoluteUri;
        var citation = new SourceCitation("Collection Spec", "1.0", guidanceUri);
        using var shadowExpr = JsonDocument.Parse("""{"op":"==","args":[{"var":"shared_item"},999]}""");
        using var qualifiedSiblingExpr = JsonDocument.Parse("""{"op":">","args":[{"var":"fu_val"},{"var":"cs.baseline.shared_item"}]}""");
        using var unqualifiedSiblingExpr = JsonDocument.Parse("""{"op":">","args":[{"var":"fu_val"},{"var":"base_only"}]}""");
        using var candidateTightExpr = JsonDocument.Parse("""{"op":">","args":[{"var":"fu_val"},100]}""");

        var packOne = new RulePack(
            RuleAuthority.Sponsor,
            "Collection Pack 1",
            [
                new Rule(
                    "R-SHADOW-NULL",
                    1,
                    RuleAuthority.Sponsor,
                    citation,
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "cs.followup"),
                    ValidationSeverity.Error,
                    RuleExpressionJson.Parse(shadowExpr.RootElement),
                    "Unrecorded target item must be Null rather than falling through to sibling.",
                    "Check shadowing.",
                    EffectivePeriod.Always),
                new Rule(
                    "R-QUAL-SIBLING",
                    1,
                    RuleAuthority.Sponsor,
                    citation,
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "cs.followup"),
                    ValidationSeverity.Warning,
                    RuleExpressionJson.Parse(qualifiedSiblingExpr.RootElement),
                    "Follow-up exceeds latest baseline shared_item.",
                    "Check baseline.",
                    EffectivePeriod.Always),
            ]);
        var packTwo = new RulePack(
            RuleAuthority.Sponsor,
            "Collection Pack 2",
            [
                new Rule(
                    "R-UNQUAL-SIBLING",
                    1,
                    RuleAuthority.Sponsor,
                    citation,
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "cs.followup"),
                    ValidationSeverity.Warning,
                    RuleExpressionJson.Parse(unqualifiedSiblingExpr.RootElement),
                    "Follow-up exceeds latest baseline base_only.",
                    "Check base_only.",
                    EffectivePeriod.Always),
            ]);
        var packTwoCandidate = new RulePack(
            RuleAuthority.Sponsor,
            "Collection Pack 2",
            [
                packTwo.Rules[0],
                new Rule(
                    "R-FU-TIGHT",
                    1,
                    RuleAuthority.Sponsor,
                    citation,
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "cs.followup"),
                    ValidationSeverity.Warning,
                    RuleExpressionJson.Parse(candidateTightExpr.RootElement),
                    "Follow-up exceeds 100.",
                    "Verify follow-up.",
                    EffectivePeriod.Always),
            ]);

        var packItem1 = ReleasedLibraryItem(LibraryItemKind.RulePack, "col-pack-1", packOne.Title, RulePackJson.ToJson(packOne).GetRawText(), [], 1);
        var packItem2 = ReleasedLibraryItem(LibraryItemKind.RulePack, "col-pack-2", packTwo.Title, RulePackJson.ToJson(packTwo).GetRawText(), [], 1);

        var inputs = new Inputs();
        var parent = inputs.Add(Approved(ProtocolDesignDocument.Write(baseDesign)));
        var draft = inputs.Add(Draft(parent, ProtocolDesignDocument.Write(scheduleAmendedDesign)));
        inputs.AddLibraryItem(packItem1);
        inputs.AddLibraryItem(packItem2);

        var site = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "US", "Active", IsActive: true, Now.AddDays(-30));
        var enrollment = new SimulationEnrollmentSnapshot(EnrollmentId.New(), site.Id, "SUBJ-01", EnrollmentStatus.Enrolled, new DateOnly(2026, 9, 1));

        // Older baseline form has shared_item = 999 and base_only = 500; newer baseline form has shared_item = 120 and base_only = 110.
        var olderBaselineId = new CollectionInstanceId(Guid.Parse("00000000-0000-0000-0000-000000000010"));
        var newerTieLoserId = new CollectionInstanceId(Guid.Parse("00000000-0000-0000-0000-000000000020"));
        var newerTieWinnerId = new CollectionInstanceId(Guid.Parse("00000000-0000-0000-0000-000000000030"));
        var followupId = new CollectionInstanceId(Guid.Parse("00000000-0000-0000-0000-000000000040"));

        var collections = new List<SimulationCollectionSnapshot>
        {
            new(
                olderBaselineId,
                enrollment.Id,
                site.Id,
                parent.Id,
                "cs.baseline",
                "act.base",
                Now.AddDays(-10),
                Now.AddDays(-10),
                new Dictionary<string, SimulationCollectedItemSnapshot>(StringComparer.Ordinal)
                {
                    ["base_only"] = new(ObservationId.New(), conceptA, new QuantityValue(500m, "mg")),
                    ["shared_item"] = new(ObservationId.New(), conceptB, new QuantityValue(999m, "mg")),
                }),
            new(
                newerTieWinnerId,
                enrollment.Id,
                site.Id,
                parent.Id,
                "cs.baseline",
                "act.base",
                Now.AddDays(-5),
                Now.AddDays(-5),
                new Dictionary<string, SimulationCollectedItemSnapshot>(StringComparer.Ordinal)
                {
                    ["base_only"] = new(ObservationId.New(), conceptA, new QuantityValue(110m, "mg")),
                    ["shared_item"] = new(ObservationId.New(), conceptB, new QuantityValue(120m, "mg")),
                }),
            new(
                newerTieLoserId,
                enrollment.Id,
                site.Id,
                parent.Id,
                "cs.baseline",
                "act.base",
                Now.AddDays(-5),
                Now.AddDays(-5),
                new Dictionary<string, SimulationCollectedItemSnapshot>(StringComparer.Ordinal)
                {
                    ["base_only"] = new(ObservationId.New(), conceptA, new QuantityValue(800m, "mg")),
                    ["shared_item"] = new(ObservationId.New(), conceptB, new QuantityValue(999m, "mg")),
                }),
            new(
                followupId,
                enrollment.Id,
                site.Id,
                parent.Id,
                "cs.followup",
                "act.fu",
                Now.AddDays(-1),
                Now.AddDays(-1),
                new Dictionary<string, SimulationCollectedItemSnapshot>(StringComparer.Ordinal)
                {
                    // Note: shared_item is intentionally omitted here; design declares it on cs.followup so it must bind to RuleValue.Null!
                    ["fu_val"] = new(ObservationId.New(), conceptA, new QuantityValue(150m, "mg")),
                }),
        };

        inputs.Snapshot = new SimulationStudySnapshot(
            Loaded: true,
            Sites: [site],
            Effectivities: [new SimulationEffectivitySnapshot(EffectivityId.New(), parent.Id, EffectivityScope.Study(), EffectivityStatus.Effective)],
            Enrollments: [enrollment],
            Observations: [],
            Collections: collections,
            ProtocolDesigns: new Dictionary<ProtocolVersionId, ProtocolDesign> { [parent.Id] = baseDesign },
            Concepts: new Dictionary<ConceptId, BlindedConceptMetadata>(),
            PinnedLibraryItems: [],
            PinnedRulePacks: [packItem1, packItem2],
            DeclaredStandardVersions: [],
            CodelistDecodes: new Dictionary<(string Codelist, string Code), string>(),
            DeclaredStandards: [("SDTMIG", "3.4"), ("CDASHIG", "2.2")]);

        var countingEvaluator = new CountingRuleEvaluator(new DotNetRuleEvaluator());
        var engine = new SimulationEngine(
            inputs,
            new Probe(permitted: true),
            new Hide(null, MaskReason.NotPermitted),
            new FixedActor(Actor.Person("designer@cadence.test")),
            new FakeClock(Now),
            ruleEvaluator: countingEvaluator);

        // 1. Schedule-only protocol candidate has matching CollectionSpecs and RulePacks: collection stage runs once (2 standards * 2 packs = 4 calls, not 8).
        countingEvaluator.StageCalls.Clear();
        var protoResult = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(draft.Id));
        countingEvaluator.StageCalls.GetValueOrDefault(ValidationStage.Collection).Should().Be(4);
        protoResult.ValidationDelta!.Opening.Should().BeEmpty();
        protoResult.ValidationDelta.Resolving.Should().BeEmpty();
        protoResult.ValidationDelta.UnchangedCount.Should().Be(
            2,
            "R-QUAL-SIBLING and R-UNQUAL-SIBLING fire against the tie-winner baseline while R-SHADOW-NULL stays false because unrecorded shared_item on cs.followup shadows sibling");

        // 2. RulePack candidate evaluates baseline and candidate collection cursors across both declared standards (8 collection calls) and opens R-FU-TIGHT.
        countingEvaluator.StageCalls.Clear();
        var ruleResult = await SimulateAsync(
            engine,
            SimulationCandidate.UnsavedRulePack(Study, RulePackJson.ToJson(packTwoCandidate).GetRawText(), packItem2.Id));
        countingEvaluator.StageCalls.GetValueOrDefault(ValidationStage.Collection).Should().Be(8);
        ruleResult.ValidationDelta!.Opening.Should().ContainSingle(f => f.RuleId == "R-FU-TIGHT" && f.Subject == followupId.Value.ToString());
        ruleResult.ValidationDelta.UnchangedCount.Should().Be(2);
    }

    [Fact]
    [KernelRule("K-SIM-4")]
    [KernelRule("K-AUTH-4")]
    public async Task Simulate_CustomBlindedTreatmentClassifier_DelegatesObservationSpecItemAndFindingChecks()
    {
        var conceptOpen = new ConceptRef(ConceptId.New(), 1);
        var conceptCustomSecret = new ConceptRef(ConceptId.New(), 1);

        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities = [new PlannedActivity("act.v1", "Visit 1", "ep.treatment", [ActivityLocation.Site], ["cs.open", "cs.custom_secret"], new ActivityTiming(1, 1, 1))],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.open",
                    "Open Form",
                    [
                        new CollectionItem("open_val", conceptOpen, Unit: "mg", Required: true),
                        new CollectionItem("secret_item", conceptCustomSecret, Unit: "mg", Required: false),
                    ]),
                new CollectionSpec(
                    "cs.custom_secret",
                    "Custom Secret Form",
                    [new CollectionItem("dose_val", conceptCustomSecret, Unit: "mg", Required: true)]),
            ],
        };

        var guidanceUri = new UriBuilder(Uri.UriSchemeHttps, "example.org") { Path = "blinded-rules" }.Uri.AbsoluteUri;
        var citation = new SourceCitation("Blinding Spec", "1.0", guidanceUri);
        using var benignExpr = JsonDocument.Parse("""{"op":">","args":[{"var":"open_val"},1000]}""");
        using var openExpr = JsonDocument.Parse("""{"op":">","args":[{"var":"open_val"},10]}""");
        var basePack = new RulePack(
            RuleAuthority.Sponsor,
            "Open Check",
            [
                new Rule(
                    "R-BENIGN",
                    1,
                    RuleAuthority.Sponsor,
                    citation,
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "cs.open"),
                    ValidationSeverity.Warning,
                    RuleExpressionJson.Parse(benignExpr.RootElement),
                    "Benign baseline check.",
                    "No action.",
                    EffectivePeriod.Always),
            ]);
        var candPack = new RulePack(
            RuleAuthority.Sponsor,
            "Open Check",
            [
                new Rule(
                    "R-CUSTOM-BLINDED-FINDING",
                    1,
                    RuleAuthority.Sponsor,
                    citation,
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "cs.open"),
                    ValidationSeverity.Warning,
                    RuleExpressionJson.Parse(openExpr.RootElement),
                    "Custom classifier marks this finding blinded.",
                    "Mask for blinded reader.",
                    EffectivePeriod.Always),
            ]);
        var packItem = ReleasedLibraryItem(LibraryItemKind.RulePack, "rules-blind", basePack.Title, RulePackJson.ToJson(basePack).GetRawText(), [], 1);

        var inputs = new Inputs();
        var parent = inputs.Add(Approved(ProtocolDesignDocument.Write(design)));
        inputs.AddLibraryItem(packItem);

        var site = new SimulationSiteSnapshot(StudySiteId.New(), SiteId.New(), "US", "Active", IsActive: true, Now.AddDays(-10));
        var enrollment = new SimulationEnrollmentSnapshot(EnrollmentId.New(), site.Id, "SUBJ-01", EnrollmentStatus.Enrolled, new DateOnly(2026, 9, 1));
        var openColId = CollectionInstanceId.New();
        var secretColId = CollectionInstanceId.New();
        var openObsId = ObservationId.New();
        var secretObsId = ObservationId.New();

        inputs.Snapshot = new SimulationStudySnapshot(
            Loaded: true,
            Sites: [site],
            Effectivities: [new SimulationEffectivitySnapshot(EffectivityId.New(), parent.Id, EffectivityScope.Study(), EffectivityStatus.Effective)],
            Enrollments: [enrollment],
            Observations:
            [
                new SimulationObservationSnapshot(openObsId, enrollment.Id, site.Id, conceptOpen, new QuantityValue(50m, "mg"), openColId, parent.Id, "cs.open", "act.v1", "open_val"),
                new SimulationObservationSnapshot(secretObsId, enrollment.Id, site.Id, conceptCustomSecret, new QuantityValue(25m, "mg"), openColId, parent.Id, "cs.open", "act.v1", "secret_item"),
            ],
            Collections:
            [
                new SimulationCollectionSnapshot(
                    openColId,
                    enrollment.Id,
                    site.Id,
                    parent.Id,
                    "cs.open",
                    "act.v1",
                    Now.AddDays(-2),
                    Now.AddDays(-2),
                    new Dictionary<string, SimulationCollectedItemSnapshot>(StringComparer.Ordinal)
                    {
                        ["open_val"] = new(openObsId, conceptOpen, new QuantityValue(50m, "mg")),
                        ["secret_item"] = new(secretObsId, conceptCustomSecret, new QuantityValue(25m, "mg")),
                    }),
                new SimulationCollectionSnapshot(
                    secretColId,
                    enrollment.Id,
                    site.Id,
                    parent.Id,
                    "cs.custom_secret",
                    "act.v1",
                    Now.AddDays(-2),
                    Now.AddDays(-2),
                    new Dictionary<string, SimulationCollectedItemSnapshot>(StringComparer.Ordinal)
                    {
                        ["dose_val"] = new(ObservationId.New(), conceptCustomSecret, new QuantityValue(25m, "mg")),
                    }),
            ],
            ProtocolDesigns: new Dictionary<ProtocolVersionId, ProtocolDesign> { [parent.Id] = design },
            Concepts: new Dictionary<ConceptId, BlindedConceptMetadata>(),
            PinnedLibraryItems: [],
            PinnedRulePacks: [packItem],
            DeclaredStandardVersions: [],
            CodelistDecodes: new Dictionary<(string Codelist, string Code), string>(),
            DeclaredStandards: []);

        var customClassifier = new CustomBlindingClassifierSpy(conceptCustomSecret.ConceptId);
        var engine = new SimulationEngine(
            inputs,
            new BlindedProbe(),
            new Hide(null, MaskReason.NotPermitted),
            new FixedActor(Actor.Person("blinded-monitor@cadence.test")),
            new FakeClock(Now),
            blindingClassifier: customClassifier);

        var result = await SimulateAsync(
            engine,
            SimulationCandidate.UnsavedRulePack(Study, RulePackJson.ToJson(candPack).GetRawText(), packItem.Id));

        customClassifier.ObservationCalls.Should().BeGreaterThan(0);
        customClassifier.SpecCalls.Should().BeGreaterThan(0);
        customClassifier.ItemCalls.Should().BeGreaterThan(0);
        customClassifier.FindingCalls.Should().BeGreaterThan(0);
        result.ValidationDelta!.Opening.Should().BeEmpty("the custom classifier flagged the finding as blinded");
        result.Masked.Should().Contain(new MaskedPart(SimulationSnapshotMasking.BlindedTreatmentPart, MaskReason.Blinded));
    }

    private static string ComputeReferenceJsonNodeDigest(
        IReadOnlyList<SimulationSiteSnapshot> sites,
        IReadOnlyList<SimulationEffectivitySnapshot> effectivities,
        IReadOnlyList<SimulationEnrollmentSnapshot> enrollments,
        IReadOnlyList<SimulationObservationSnapshot> observations,
        IReadOnlyList<SimulationCollectionSnapshot> collections)
    {
        var doc = new JsonObject
        {
            ["sites"] = new JsonArray(sites.OrderBy(s => s.Id.Value).Select(s => (JsonNode)new JsonObject
            {
                ["id"] = s.Id.Value.ToString(),
                ["country"] = s.Country,
                ["status"] = s.Status,
                ["active"] = s.IsActive,
            }).ToArray()),
            ["effectivities"] = new JsonArray(effectivities.OrderBy(e => e.Id.Value).Select(e => (JsonNode)new JsonObject
            {
                ["id"] = e.Id.Value.ToString(),
                ["protocolVersion"] = e.ProtocolVersionId.Value.ToString(),
                ["status"] = e.StatusAsOf?.ToString(),
            }).ToArray()),
            ["enrollments"] = new JsonArray(enrollments.OrderBy(e => e.Id.Value).Select(e => (JsonNode)new JsonObject
            {
                ["id"] = e.Id.Value.ToString(),
                ["subjectCode"] = e.SubjectCode,
                ["status"] = e.StatusAsOf.ToString(),
                ["referenceStart"] = e.ReferenceStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }).ToArray()),
            ["observations"] = new JsonArray(observations.OrderBy(o => o.Id.Value).Select(o => (JsonNode)new JsonObject
            {
                ["id"] = o.Id.Value.ToString(),
                ["concept"] = o.Concept.ToString(),
                ["value"] = o.Value.ToDisplayString(),
            }).ToArray()),
            ["collections"] = new JsonArray(collections.OrderBy(c => c.Id.Value).Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id.Value.ToString(),
                ["spec"] = c.CollectionSpecId,
                ["activity"] = c.PlannedActivityId,
                ["items"] = new JsonArray(c.ValuesByItemId
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => (JsonNode)new JsonObject
                    {
                        ["item"] = kv.Key,
                        ["value"] = kv.Value.Value.ToDisplayString(),
                    })
                    .ToArray()),
            }).ToArray()),
        };
        return ContentHash.Compute(doc.ToJsonString());
    }

    private static SimulationEngine Engine(Inputs inputs, INodeVisibility? visibility = null, IAuthorizationProbe? probe = null) =>
        new(
            inputs,
            probe ?? new Probe(permitted: true),
            visibility ?? new Hide(null, MaskReason.NotPermitted),
            new FixedActor(Actor.Person("designer@cadence.test")),
            new FakeClock(Now));

    private static async Task<SimulationResult> SimulateAsync(ISimulationEngine engine, SimulationCandidate candidate, DateTimeOffset? at = null)
    {
        var result = await engine.SimulateAsync(Tenant, new SimulationRequest(candidate, at, at), TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }

    private static string Renamed(string armId, string name) =>
        ProtocolDesignDocument.Write(ProtocolDesignSamples.MinimalDesign() with
        {
            Arms = [.. ProtocolDesignSamples.MinimalDesign().Arms.Select(a => a.Id == armId ? a with { Name = name } : a)],
        });

    private static string Scheduled(params PlannedActivity[] activities) =>
        ProtocolDesignDocument.Write(ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.screening", "Screening", 1), new Epoch("ep.treatment", "Treatment", 2)],
            PlannedActivities = [.. activities],
        });

    private static PlannedActivity Visit(string id, string name, string epochId, int day, int before, int after) =>
        new(id, name, epochId, [ActivityLocation.Site], [], new ActivityTiming(day, before, after));

    private static ProtocolVersionSnapshot Approved(string content) =>
        new(ProtocolVersionId.New(), Study, 1, ProtocolVersionStatus.Approved, null, content, ContentHash.Compute(content), 3);

    private static ProtocolVersionSnapshot Draft(ProtocolVersionSnapshot parent, string content, ProtocolAmendment? amendment = null) =>
        new(ProtocolVersionId.New(), Study, parent.Number + 1, ProtocolVersionStatus.Draft, parent.Id, content, null, 2, amendment);

    private static LibraryItemView ReleasedLibraryItem(
        LibraryItemKind kind,
        string key,
        string title,
        string content,
        IReadOnlyList<StandardVersionId> standards,
        int version) =>
        new(
            LibraryItemId.New(),
            kind,
            key,
            version,
            title,
            System.Text.Json.JsonDocument.Parse(content).RootElement.Clone(),
            ContentHash.Compute(content),
            GovernanceStatus.Approved,
            standards,
            AttestationId.New(),
            null)
        {
            VersionIndex = version,
        };

    private static LibraryItemView DraftLibraryItemView(
        LibraryItemKind kind,
        string key,
        string title,
        string content,
        IReadOnlyList<StandardVersionId> standards,
        int version) =>
        new(
            LibraryItemId.New(),
            kind,
            key,
            version,
            title,
            System.Text.Json.JsonDocument.Parse(content).RootElement.Clone(),
            ContentHash.Compute(content),
            GovernanceStatus.Draft,
            standards,
            null,
            null)
        {
            VersionIndex = version,
        };

    private static RulePack BuildRulePack(params (string Id, int Version, decimal Threshold, string Op)[] specs)
    {
        var guidanceUri = new UriBuilder(Uri.UriSchemeHttps, "example.org") { Path = "rules" }.Uri.AbsoluteUri;
        var citation = new SourceCitation("Sponsor Edit Check Specification", "2.1", guidanceUri);
        var applicability = new RuleApplicability(ValidationStage.Standardization, null, null, null, "VS");
        var rules = specs.Select(s =>
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                $$"""{"op":"{{s.Op}}","args":[{"var":"VSSTRESN"},{{s.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)}}]}""");
            return new Rule(
                s.Id,
                s.Version,
                RuleAuthority.Sponsor,
                citation,
                applicability,
                ValidationSeverity.Warning,
                RuleExpressionJson.Parse(doc.RootElement),
                $"Rule {s.Id} triggered.",
                "Verify blood pressure entry.",
                EffectivePeriod.Always);
        }).ToList();
        return new RulePack(RuleAuthority.Sponsor, "Vitals standardization checks", rules);
    }

    private sealed class Inputs : ISimulationInputs
    {
        private readonly Dictionary<ProtocolVersionId, ProtocolVersionSnapshot> _versions = [];
        private readonly Dictionary<LibraryItemId, LibraryItemView> _libraryItems = [];

        public SimulationStudySnapshot Snapshot { get; set; } = SimulationStudySnapshot.Empty;

        public ProtocolVersionSnapshot Add(ProtocolVersionSnapshot version)
        {
            _versions.Add(version.Id, version);
            return version;
        }

        public void AddLibraryItem(LibraryItemView item) => _libraryItems[item.Id] = item;

        public void Replace(ProtocolVersionSnapshot version) => _versions[version.Id] = version;

        public List<(ProtocolVersionId Base, ProtocolVersionId? Candidate, IReadOnlySet<NodeRef> Impacted, SimulationAsOf AsOf)> DependentsAsked { get; } = [];

        public Task<ProtocolVersionSnapshot?> FindProtocolVersionAsync(TenantId tenant, ProtocolVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult(_versions.GetValueOrDefault(id));

        public Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken) =>
            Task.FromResult(_libraryItems.GetValueOrDefault(id));

        public Task<SimulationStudySnapshot> LoadStudySnapshotAsync(
            TenantId tenant, StudyId studyId, SimulationAsOf asOf, CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public Task<DependentsReport> DependentsAsync(
            TenantId tenant,
            Actor reader,
            ProtocolVersionId baseVersion,
            ProtocolVersionId? candidate,
            IReadOnlySet<NodeRef> impactedElements,
            SimulationAsOf asOf,
            CancellationToken cancellationToken)
        {
            DependentsAsked.Add((baseVersion, candidate, impactedElements, asOf));
            var dependent = NodeRef.For(Study, 1);
            return Task.FromResult(new DependentsReport(
                NodeRef.For(baseVersion).ToString(), LineageQuery.DefaultDepth, asOf.ValidAt, asOf.KnownAt,
                [new DependentGroup(NodeKinds.Study, 1, [new LineageNode(dependent.ToString(), NodeKinds.Study, dependent, null, 1)])],
                1, false));
        }
    }

    private sealed class BlindedProbe : IAuthorizationProbe
    {
        public ValueTask<AuthorizationDecision> CanIAsync(TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(string.Equals(capability.Token, "enrollment.unblind", StringComparison.Ordinal)
                ? AuthorizationDecision.Deny("Actor is blinded.")
                : AuthorizationDecision.Permit());

        public ValueTask<IReadOnlyList<CommandAvailability>> GetCommandsAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Probe(bool permitted) : IAuthorizationProbe
    {
        public List<(Capability Capability, NodeRef Resource)> Asked { get; } = [];

        public ValueTask<AuthorizationDecision> CanIAsync(TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken)
        {
            Asked.Add((capability, resource));
            return ValueTask.FromResult(permitted ? AuthorizationDecision.Permit() : AuthorizationDecision.Deny("No simulation.run grant at this study."));
        }

        public ValueTask<IReadOnlyList<CommandAvailability>> GetCommandsAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Hide(NodeRef? node, MaskReason reason) : INodeVisibility
    {
        public List<IReadOnlyCollection<NodeRef>> Asked { get; } = [];

        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
        {
            Asked.Add(nodes);
            return ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(nodes.Where(n => n == node).ToDictionary(n => n, _ => reason));
        }
    }

    private sealed class FixedActor(Actor? actor) : ICurrentActor
    {
        public Actor? Actor { get; } = actor;
    }

    private sealed class CountingMappingEngine(IMappingEngine inner) : IMappingEngine
    {
        public int MapCalls { get; set; }

        public MappingResult Map(MappingSpecification specification, MappingInput input, ICodelistDecoder? decoder = null)
        {
            MapCalls++;
            return inner.Map(specification, input, decoder);
        }
    }

    private sealed class CountingRuleEvaluator(IRuleEvaluator inner) : IRuleEvaluator
    {
        public Dictionary<ValidationStage, int> StageCalls { get; } = [];

        public IReadOnlyList<RuleViolation> Evaluate(RulePack pack, RuleEvaluationContext context, IEnumerable<RuleRecord> records)
        {
            StageCalls[context.Stage] = StageCalls.GetValueOrDefault(context.Stage) + 1;
            return inner.Evaluate(pack, context, records);
        }

        public void Evaluate(RulePack pack, RuleEvaluationContext context, IRuleRecordCursor records, IRuleViolationSink sink)
        {
            StageCalls[context.Stage] = StageCalls.GetValueOrDefault(context.Stage) + 1;
            inner.Evaluate(pack, context, records, sink);
        }
    }

    private sealed class CustomBlindingClassifierSpy(ConceptId secretConceptId) : IBlindedTreatmentClassifier
    {
        public int ObservationCalls { get; private set; }

        public int SpecCalls { get; private set; }

        public int ItemCalls { get; private set; }

        public int FindingCalls { get; private set; }

        public bool IsBlindedObservation(
            ConceptId conceptId,
            ObservationValue? value,
            BlindedConceptMetadata? concept,
            string? collectionSpecId,
            string? itemId,
            IReadOnlyList<ProtocolDesign> designs)
        {
            ObservationCalls++;
            return conceptId == secretConceptId;
        }

        public bool IsBlindedCollectionSpec(
            string? collectionSpecId,
            string? collectionSpecName = null,
            string? operationalNeed = null,
            IReadOnlyList<ProtocolDesign>? designs = null)
        {
            SpecCalls++;
            return string.Equals(collectionSpecId, "cs.custom_secret", StringComparison.Ordinal);
        }

        public bool IsBlindedCollectionItem(
            string? collectionSpecId,
            string? collectionTitle,
            CollectionItem? item,
            ObservationValue? value = null,
            BlindedConceptMetadata? concept = null,
            IReadOnlyList<ProtocolDesign>? designs = null)
        {
            ItemCalls++;
            return item?.Concept.ConceptId == secretConceptId;
        }

        public bool IsBlindedVariable(
            string? defaultDataset,
            string? variable,
            IReadOnlyList<ProtocolDesign> designs,
            Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null) =>
            false;

        public bool IsBlindedFinding(
            FindingTarget target,
            IReadOnlyList<ObservedVariable> observedValues,
            IReadOnlyList<ProtocolDesign> designs,
            Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null)
        {
            FindingCalls++;
            return string.Equals(target.Dataset, "cs.open", StringComparison.Ordinal);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SimulationMemoryQuotaCollection
{
    public const string Name = "SimulationMemoryQuota";

    private SimulationMemoryQuotaCollection()
    {
    }
}

/// <summary>
/// Isolated memory-quota verification for <see cref="SimulationSnapshotMasking.ComputeVisibleDigest"/> across
/// 300 enrollments, 9,000 forms, and 135,000 observations so parallel xUnit tests cannot allocate on the process-wide
/// Large Object Heap during measurement.
/// </summary>
[Trait("Category", "Unit")]
[Collection(SimulationMemoryQuotaCollection.Name)]
public sealed class SimulationLohQuotaTests
{
    [Fact(DisplayName = "ComputeVisibleDigest_AllocatesZeroBytesOnLargeObjectHeapAcross300Enrollments9000FormsAnd135000Observations")]
    [KernelRule("K-SIM-3")]
    [KernelRule("K-SIM-4")]
    public void ComputeVisibleDigestAllocatesZeroBytesOnLargeObjectHeapAcross300Enrollments9000FormsAnd135000Observations()
    {
        const int enrollmentsCount = 300;
        const int formsPerEnrollment = 30;
        const int itemsPerForm = 15;

        var now = new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
        var protoId = new ProtocolVersionId(DeterministicGuid(1, 0, 1));
        var site = new SimulationSiteSnapshot(
            new StudySiteId(DeterministicGuid(2, 0, 1)),
            new SiteId(DeterministicGuid(2, 1, 1)),
            "US",
            "Active",
            IsActive: true,
            now.AddDays(-60));
        var effectivity = new SimulationEffectivitySnapshot(
            new EffectivityId(DeterministicGuid(3, 0, 1)),
            protoId,
            EffectivityScope.Study(),
            EffectivityStatus.Effective);

        var concepts = new ConceptRef[itemsPerForm];
        for (var i = 0; i < itemsPerForm; i++)
        {
            concepts[i] = new ConceptRef(new ConceptId(DeterministicGuid(4, 0, i + 1)), 1);
        }

        var enrollments = new List<SimulationEnrollmentSnapshot>(enrollmentsCount);
        var collections = new List<SimulationCollectionSnapshot>(enrollmentsCount * formsPerEnrollment);
        var observations = new List<SimulationObservationSnapshot>(enrollmentsCount * formsPerEnrollment * itemsPerForm);

        var colCounter = 1;
        var obsCounter = 1;
        for (var e = 0; e < enrollmentsCount; e++)
        {
            var enrId = new EnrollmentId(DeterministicGuid(5, 0, enrollmentsCount - e));
            enrollments.Add(new SimulationEnrollmentSnapshot(
                enrId,
                site.Id,
                $"SUBJ-{e + 1:D4}",
                EnrollmentStatus.Enrolled,
                new DateOnly(2026, 8, 1).AddDays(e % 25)));

            for (var f = 0; f < formsPerEnrollment; f++)
            {
                var colId = new CollectionInstanceId(DeterministicGuid(6, 0, (enrollmentsCount * formsPerEnrollment) - colCounter + 1));
                colCounter++;
                var specId = $"cs.visit_{f:D2}";
                var actId = $"act.visit_{f:D2}";
                var itemMap = new Dictionary<string, SimulationCollectedItemSnapshot>(itemsPerForm, StringComparer.Ordinal);

                for (var i = 0; i < itemsPerForm; i++)
                {
                    var obsId = new ObservationId(DeterministicGuid(7, 0, (enrollmentsCount * formsPerEnrollment * itemsPerForm) - obsCounter + 1));
                    obsCounter++;
                    var val = new QuantityValue(100m + ((e + f + i) % 50), "mg");
                    var itemKey = $"f{f:D2}_itm_{i:D2}";
                    observations.Add(new SimulationObservationSnapshot(
                        obsId,
                        enrId,
                        site.Id,
                        concepts[i],
                        val,
                        colId,
                        protoId,
                        specId,
                        actId,
                        itemKey));
                    itemMap[itemKey] = new SimulationCollectedItemSnapshot(obsId, concepts[i], val);
                }

                collections.Add(new SimulationCollectionSnapshot(
                    colId,
                    enrId,
                    site.Id,
                    protoId,
                    specId,
                    actId,
                    now.AddDays(-30 + f),
                    now.AddDays(-30 + f),
                    itemMap));
            }
        }

        IReadOnlyList<SimulationSiteSnapshot> sites = [site];
        IReadOnlyList<SimulationEffectivitySnapshot> effectivities = [effectivity];

        // Warm up JIT and shared ArrayPool buckets on a tiny slice before measuring LOH delta.
        _ = SimulationSnapshotMasking.ComputeVisibleDigest(
            sites,
            effectivities,
            [enrollments[0]],
            [observations[0]],
            [collections[0]]);

#pragma warning disable S1215 // Explicit GC collection is required to measure deterministic Large Object Heap allocation delta.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var lohBefore = GC.GetGCMemoryInfo().GenerationInfo[3].SizeAfterBytes;
        var threadBefore = GC.GetAllocatedBytesForCurrentThread();

        var digest = SimulationSnapshotMasking.ComputeVisibleDigest(
            sites,
            effectivities,
            enrollments,
            observations,
            collections);

        var threadAllocated = GC.GetAllocatedBytesForCurrentThread() - threadBefore;
        GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: false);
#pragma warning restore S1215
        var lohAfter = GC.GetGCMemoryInfo().GenerationInfo[3].SizeBeforeBytes;
        var lohDelta = Math.Max(0L, lohAfter - lohBefore);

        ContentHash.IsWellFormed(digest).Should().BeTrue();
        lohDelta.Should().Be(0L, "ComputeVisibleDigest must allocate zero objects on the Large Object Heap across 300 enrollments, 9,000 forms, and 135,000 observations");
        threadAllocated.Should().BeLessThan(16L * 1024 * 1024);
    }

    private static Guid DeterministicGuid(int a, int b, int c) =>
        new(
            a,
            (short)(b >> 16),
            (short)(b & 0xFFFF),
            0x10,
            0x20,
            0x30,
            0x40,
            (byte)((c >> 24) & 0xFF),
            (byte)((c >> 16) & 0xFF),
            (byte)((c >> 8) & 0xFF),
            (byte)(c & 0xFF));
}
