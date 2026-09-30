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
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Trust;
using Cadence.Testing.Time;

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
    /// reports that arm, with the study as scope and both versions pinned.
    /// </summary>
    [Fact]
    public async Task Simulate_StoredDraftAgainstItsParent()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Base));
        var same = inputs.Add(Draft(parent, Base));
        var renamed = inputs.Add(Draft(parent, Base.Replace("\"Drug\"", "\"Low dose\"", StringComparison.Ordinal)));
        var engine = Engine(inputs);

        var unchanged = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(same.Id));
        var changed = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(renamed.Id));

        unchanged.IsEmpty.Should().BeTrue();
        changed.Scope.Should().Be(NodeRef.For(Study));
        changed.Candidate.Should().Be(new CandidateSummary(
            CandidateKind.ProtocolVersion,
            NodeRef.For(renamed.Id, renamed.VersionIndex),
            NodeRef.For(parent.Id, parent.VersionIndex),
            ContentHash.Compute(renamed.Content),
            parent.ContentHash));
        changed.StructuralDiff.Should().ContainSingle().Which.ElementId.Should().Be("Arm_A");
        changed.EngineVersions.Should().Equal(ProtocolStructuralDiff.Engine);
        changed.AsOf.Should().Be(new SimulationAsOf(Now, Now));
    }

    /// <summary>
    /// The same inputs give the same input and result hashes (K-SIM-3); a different as-of or candidate changes them.
    /// </summary>
    [Fact]
    public async Task Simulate_HashesAreReproducible()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Base));
        var engine = Engine(inputs);
        var candidate = SimulationCandidate.UnsavedProtocolVersion(parent.Id, Base.Replace("P2D", "P3D", StringComparison.Ordinal));

        var first = await SimulateAsync(engine, candidate);
        var second = await SimulateAsync(engine, candidate);
        var later = await SimulateAsync(engine, candidate, Now.AddDays(1));
        var other = await SimulateAsync(engine, SimulationCandidate.UnsavedProtocolVersion(parent.Id, Base));

        second.InputHash.Should().Be(first.InputHash);
        second.ResultHash.Should().Be(first.ResultHash);
        ContentHash.IsWellFormed(first.ResultHash).Should().BeTrue();
        later.InputHash.Should().NotBe(first.InputHash);
        other.ResultHash.Should().NotBe(first.ResultHash);
        other.IsEmpty.Should().BeTrue();
        first.Candidate.Version.Should().BeNull("unsaved content has no stored version");
    }

    /// <summary>
    /// A reader who may not see the candidate gets the diff withheld with the reason, no content hashes, and a result
    /// hash that does not move when the hidden content changes (K-SIM-4).
    /// </summary>
    [Fact]
    public async Task Simulate_MasksWhatTheReaderMayNotSee()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Base));
        var draft = inputs.Add(Draft(parent, Base.Replace("\"Drug\"", "\"Low dose\"", StringComparison.Ordinal)));
        var visibility = new Hide(NodeRef.For(draft.Id, draft.VersionIndex), MaskReason.Blinded);
        var engine = Engine(inputs, visibility);

        var hidden = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(draft.Id));
        inputs.Replace(draft with { Content = Base.Replace("\"Placebo\"", "\"Active comparator\"", StringComparison.Ordinal) });
        var hiddenAfterChange = await SimulateAsync(engine, SimulationCandidate.StoredProtocolVersion(draft.Id));

        hidden.StructuralDiff.Should().BeEmpty();
        hidden.Masked.Should().Equal(new MaskedPart(SimulationEngine.StructuralDiffPart, MaskReason.Blinded));
        hidden.Candidate.Should().Be(new CandidateSummary(CandidateKind.ProtocolVersion, null, null, null, null));
        hidden.IsEmpty.Should().BeFalse("a withheld diff is not an empty one");
        hiddenAfterChange.ResultHash.Should().Be(hidden.ResultHash);
        visibility.Asked.Should().HaveCount(2, "one visibility question per run").And.AllSatisfy(nodes => nodes.Should().BeEquivalentTo(
            [NodeRef.For(Study), NodeRef.For(draft.Id, draft.VersionIndex), NodeRef.For(parent.Id, parent.VersionIndex)]));
    }

    [Fact]
    public async Task Simulate_NeedsSimulationRunAtTheStudy()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Base));
        var probe = new Probe(permitted: false);
        var engine = Engine(inputs, probe: probe);

        var result = await engine.SimulateAsync(
            Tenant, new SimulationRequest(SimulationCandidate.UnsavedProtocolVersion(parent.Id, Base)), TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
        result.Error.Explanation.Should().NotBeNull();
        probe.Asked.Should().Equal((SimulationCapabilities.Run, NodeRef.For(Study)));
        SimulationCapabilities.Run.VerbClass.Should().Be(VerbClass.Read);
    }

    [Fact]
    public async Task Simulate_RejectsUnknownVersionsDraftBasesAndInvalidContent()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Base));
        var draft = inputs.Add(Draft(parent, Base));
        var engine = Engine(inputs);
        var ct = TestContext.Current.CancellationToken;

        async Task<string?> Code(SimulationCandidate candidate) =>
            (await engine.SimulateAsync(Tenant, new SimulationRequest(candidate), ct)).Error?.Code;

        (await Code(SimulationCandidate.StoredProtocolVersion(ProtocolVersionId.New()))).Should().Be(SimulationErrors.VersionUnknown.Code);
        (await Code(SimulationCandidate.UnsavedProtocolVersion(ProtocolVersionId.New(), Base))).Should().Be(SimulationErrors.VersionUnknown.Code);
        (await Code(SimulationCandidate.UnsavedProtocolVersion(draft.Id, Base))).Should().Be(SimulationErrors.BaseNotFrozen.Code);
        (await Code(SimulationCandidate.UnsavedProtocolVersion(parent.Id, "not json"))).Should().Be(SimulationErrors.ContentInvalid.Code);
        (await Code(SimulationCandidate.UnsavedProtocolVersion(parent.Id, "[]"))).Should().Be(SimulationErrors.ContentInvalid.Code);
    }

    [Fact]
    public async Task Simulate_NeedsAnAccountableActor()
    {
        var inputs = new Inputs();
        var parent = inputs.Add(Approved(Base));
        var engine = new SimulationEngine(inputs, new Probe(permitted: true), new Hide(null, MaskReason.NotPermitted), new FixedActor(null), new FakeClock(Now));

        var result = await engine.SimulateAsync(
            Tenant, new SimulationRequest(SimulationCandidate.UnsavedProtocolVersion(parent.Id, Base)), TestContext.Current.CancellationToken);

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

        public Task<ProtocolVersionSnapshot?> FindProtocolVersionAsync(TenantId tenant, ProtocolVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult(_versions.GetValueOrDefault(id));
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
