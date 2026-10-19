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
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Trust;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Kernel.Protocol;

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

    private static SimulationEngine Engine(Inputs inputs, INodeVisibility? visibility = null, IAuthorizationProbe? probe = null) =>
        new(inputs, probe ?? new Probe(permitted: true), visibility ?? new Hide(null, MaskReason.NotPermitted),
            new FixedActor(Actor.Person("designer@cadence.test")), new FakeClock(Now));

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

    private static ProtocolVersionSnapshot Draft(ProtocolVersionSnapshot parent, string content) =>
        new(ProtocolVersionId.New(), Study, parent.Number + 1, ProtocolVersionStatus.Draft, parent.Id, content, null, 2);

    private sealed class Inputs : ISimulationInputs
    {
        private readonly Dictionary<ProtocolVersionId, ProtocolVersionSnapshot> _versions = [];

        public ProtocolVersionSnapshot Add(ProtocolVersionSnapshot version)
        {
            _versions.Add(version.Id, version);
            return version;
        }

        public void Replace(ProtocolVersionSnapshot version) => _versions[version.Id] = version;

        public List<(ProtocolVersionId Base, ProtocolVersionId? Candidate, IReadOnlySet<NodeRef> Impacted, SimulationAsOf AsOf)> DependentsAsked { get; } = [];

        public Task<ProtocolVersionSnapshot?> FindProtocolVersionAsync(TenantId tenant, ProtocolVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult(_versions.GetValueOrDefault(id));

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
