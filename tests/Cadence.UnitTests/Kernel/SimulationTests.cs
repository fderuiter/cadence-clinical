using System;
using System.Collections.Generic;
using System.Linq;
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
        changed.EngineVersions.Should().Equal(SimulationEngine.DependentsEngine, SimulationEngine.ScheduleEngine, ProtocolStructuralDiff.Engine);
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
        hidden.Masked.Should().Equal(
            new MaskedPart(SimulationEngine.DependentsPart, MaskReason.Blinded),
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
}
