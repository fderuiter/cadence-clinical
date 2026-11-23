using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Protocol;

/// <summary>
/// Protocol design elements as lineage nodes (ADR-2474): stable node ids per study and element, element versions
/// numbered by the protocol version that last changed them, and the edges an approval writes.
/// </summary>
public sealed class ProtocolElementLineageTests
{
    private static readonly StudyId Study = StudyId.FromGuid(Guid.Parse("0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e"));
    private static readonly ConceptRef Concept = new(ConceptId.FromGuid(Guid.Parse("0192f3c4-0000-7000-8000-000000000001")), 2);
    private static readonly CodeSystemVersionId CodeSystemVersion = CodeSystemVersionId.FromGuid(Guid.Parse("0192f3c4-0000-7000-8000-000000000002"));

    private static ProtocolDesign Complete() => ProtocolDesignSamples.Complete(Concept, CodeSystemVersion);

    /// <summary>
    /// An element's node id depends only on its study and element id, so every protocol version names the same object,
    /// and it is a well-formed name-based UUID (version 8, RFC 9562 variant).
    /// </summary>
    [Fact(DisplayName = "ProtocolElementId_IsStablePerStudyAndElement")]
    public void ProtocolElementIdIsStablePerStudyAndElement()
    {
        var id = ProtocolElementId.For(Study, "obj.1");

        ProtocolElementId.For(Study, "obj.1").Should().Be(id);
        ProtocolElementId.For(Study, "obj.2").Should().NotBe(id);
        ProtocolElementId.For(StudyId.New(), "obj.1").Should().NotBe(id);
        id.Value.Version.Should().Be(8);
        (id.Value.ToByteArray(bigEndian: true)[8] & 0xC0).Should().Be(0x80);
        NodeRef.For(id, 3).ToString().Should().StartWith("protocol_element:");
        FluentActions.Invoking(() => ProtocolElementId.For(StudyId.Empty, "obj.1")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ProtocolElementId.For(Study, " ")).Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Approving a first version writes every element as a new version numbered by the protocol version, the references
    /// between elements (the scientific chain as <c>RequiredBy</c>, what defines an element as <c>DefinedBy</c>), and
    /// the concept and code-system versions that define elements.
    /// </summary>
    [Fact(DisplayName = "Plan_FirstVersion_WritesEveryElementAndTheirReferences")]
    public void PlanFirstVersionWritesEveryElementAndTheirReferences()
    {
        var plan = ProtocolElementLineage.Plan(Study, [(1, ProtocolDesignDocument.Write(Complete()))]);

        plan.Parts.Should().OnlyContain(p => p.NewVersion && p.Node.Version == 1);
        plan.Parts.Select(p => p.Node).Should().BeEquivalentTo(Ids(
            "obj.1", "ep.1", "est.1", "pop.itt", "ic.1", "arm.A", "arm.B", "ep.screening", "ep.treatment", "coh.1", "int.1",
            "act.week12", "cs.vitals", "item.sbp", "item.position", "safety.1", "disc.1", "rand.1", "sap.1").Select(n => n.AtVersion(1)));
        Describe(plan.Edges).Should().BeEquivalentTo(
            "obj.1 -RequiredBy-> ep.1",
            "ep.1 -RequiredBy-> est.1",
            "pop.itt -DefinedBy-> est.1",
            "arm.A -DefinedBy-> est.1",
            "arm.B -DefinedBy-> est.1",
            "ic.1 -DefinedBy-> pop.itt",
            "int.1 -DefinedBy-> arm.A",
            "arm.A -DefinedBy-> coh.1",
            "arm.B -DefinedBy-> coh.1",
            "ep.treatment -DefinedBy-> act.week12",
            "act.week12 -RequiredBy-> cs.vitals",
            "cs.vitals -RequiredBy-> item.sbp",
            "cs.vitals -RequiredBy-> item.position",
            "ep.1 -RequiredBy-> item.sbp",
            "concept -DefinedBy-> ep.1",
            "concept -DefinedBy-> ic.1",
            "concept -DefinedBy-> item.sbp",
            "concept -DefinedBy-> item.position",
            "code_system_version -DefinedBy-> item.position",
            "concept -DefinedBy-> safety.1");
        plan.Edges.Where(e => e.From.Kind == NodeKinds.Concept).Should().OnlyContain(e => e.From == NodeRef.For(Concept.ConceptId, Concept.Version));
    }

    /// <summary>
    /// Approving an amendment that rewords an objective writes only that objective as a new version, superseding its
    /// previous one, with its edge to the unchanged endpoint; every other element stays at the version that wrote it.
    /// </summary>
    [Fact(DisplayName = "Plan_Amendment_WritesOnlyTheChangedElements")]
    public void PlanAmendmentWritesOnlyTheChangedElements()
    {
        var first = Complete();
        var reworded = first with { Objectives = [first.Objectives[0] with { Text = "Show that the drug lowers blood pressure." }] };

        var plan = ProtocolElementLineage.Plan(Study, [(1, ProtocolDesignDocument.Write(first)), (2, ProtocolDesignDocument.Write(reworded))]);

        plan.Parts.Where(p => p.NewVersion).Select(p => p.Node).Should().Equal(Id("obj.1").AtVersion(2));
        plan.Parts.Where(p => !p.NewVersion).Should().OnlyContain(p => p.Node.Version == 1).And.HaveCount(18);
        Describe(plan.Edges).Should().BeEquivalentTo("obj.1 -Supersedes-> obj.1", "obj.1 -RequiredBy-> ep.1");
        plan.Edges.Single(e => e.Kind == LineageEdgeKind.Supersedes).Should().Be(
            new ElementEdge(Id("obj.1").AtVersion(1), Id("obj.1").AtVersion(2), LineageEdgeKind.Supersedes));
        plan.Edges.Single(e => e.Kind == LineageEdgeKind.RequiredBy).Should().Be(
            new ElementEdge(Id("obj.1").AtVersion(2), Id("ep.1").AtVersion(1), LineageEdgeKind.RequiredBy));
    }

    /// <summary>
    /// Along a chain of amendments each element keeps the number of the version that last changed it; a removed element
    /// is no longer a part, and an added one is new with its references and concepts.
    /// </summary>
    [Fact(DisplayName = "Plan_Chain_NumbersEachElementByItsLastChange")]
    public void PlanChainNumbersEachElementByItsLastChange()
    {
        var first = Complete();
        var second = first with { Objectives = [first.Objectives[0] with { Text = "Reworded." }] };
        var third = second with
        {
            Endpoints = [.. second.Endpoints, new Endpoint("ep.2", "obj.1", ObjectiveLevel.Secondary, "Diastolic pressure at week 12.", Concept)],
            Cohorts = [],
        };

        var plan = ProtocolElementLineage.Plan(Study, [(1, Write(first)), (4, Write(second)), (7, Write(third))]);

        var versions = plan.Parts.ToDictionary(p => p.Node.Unversioned(), p => (p.Node.Version, p.NewVersion));
        versions[Id("obj.1")].Should().Be((4L, false));
        versions[Id("ep.1")].Should().Be((1L, false));
        versions[Id("ep.2")].Should().Be((7L, true));
        versions.Should().NotContainKey(Id("coh.1"));
        Describe(plan.Edges).Should().BeEquivalentTo("obj.1 -RequiredBy-> ep.2", "concept -DefinedBy-> ep.2");
    }

    /// <summary>
    /// Looking up the governing elements of a protocol version returns each present element pinned to the version that
    /// last added or changed it, and editing a single <see cref="CollectionItem"/> advances only that item's version
    /// while leaving its parent <see cref="CollectionSpec"/> and sibling items untouched.
    /// </summary>
    [Fact(DisplayName = "Elements_Chain_MapsEachPresentElementToItsLastChangingVersion")]
    public void ElementsChainMapsEachPresentElementToItsLastChangingVersion()
    {
        var first = Complete();
        var second = first with { Objectives = [first.Objectives[0] with { Text = "Reworded." }] };
        var spec = second.CollectionSpecs[0];
        var third = second with
        {
            Cohorts = [],
            CollectionSpecs =
            [
                spec with
                {
                    Items = [spec.Items[0] with { Required = false }, .. spec.Items.Skip(1)],
                },
            ],
        };

        var elements = ProtocolElementLineage.Elements(Study, [(1, Write(first)), (4, Write(second)), (7, Write(third))]);

        elements["obj.1"].Should().Be(Id("obj.1").AtVersion(4));
        elements["act.week12"].Should().Be(Id("act.week12").AtVersion(1));
        elements["cs.vitals"].Should().Be(Id("cs.vitals").AtVersion(1));
        elements["item.sbp"].Should().Be(Id("item.sbp").AtVersion(7));
        elements["item.position"].Should().Be(Id("item.position").AtVersion(1));
        elements.Should().NotContainKey("coh.1");
    }

    /// <summary>
    /// The elements a change impacts are those it changes or removes, never the added ones or the design root, and the
    /// walk from the base version follows every edge except one into an element that is not impacted.
    /// </summary>
    [Fact(DisplayName = "Impact_FollowsOnlyChangedAndRemovedElementsFromTheBase")]
    public void ImpactFollowsOnlyChangedAndRemovedElementsFromTheBase()
    {
        ElementChange[] changes =
        [
            new("$root", null, ElementChangeKind.Changed, ["/arms"]),
            new("arm.A", null, ElementChangeKind.Changed, ["/name"]),
            new("arm.B", null, ElementChangeKind.Removed, []),
            new("arm.C", null, ElementChangeKind.Added, []),
        ];

        var impacted = ProtocolElementImpact.Impacted(Study, changes);
        var follow = ProtocolElementImpact.FollowFromBase(impacted);

        impacted.Should().BeEquivalentTo([Id("arm.A"), Id("arm.B")]);
        follow(Id("arm.A").AtVersion(1)).Should().BeTrue();
        follow(Id("arm.B").AtVersion(1)).Should().BeTrue();
        follow(Id("arm.D").AtVersion(1)).Should().BeFalse("an element the change leaves alone is not impacted");
        follow(NodeRef.For(ObservationId.New(), 1)).Should().BeTrue("what depends on the version as a whole is impacted");
    }

    private static string Write(ProtocolDesign design) => ProtocolDesignDocument.Write(design);

    private static NodeRef Id(string element) => NodeRef.For(ProtocolElementId.For(Study, element));

    private static IEnumerable<NodeRef> Ids(params string[] elements) => elements.Select(Id);

    /// <summary>Each edge as "from -Kind-> to", with elements by their element id and other objects by kind.</summary>
    private static IEnumerable<string> Describe(IEnumerable<ElementEdge> edges)
    {
        var names = Complete().Objectives.Select(o => o.Id)
            .Concat(["ep.1", "ep.2", "est.1", "pop.itt", "ic.1", "arm.A", "arm.B", "ep.screening", "ep.treatment", "coh.1", "int.1"])
            .Concat(["act.week12", "cs.vitals", "item.sbp", "item.position", "safety.1", "disc.1", "rand.1", "sap.1"])
            .ToDictionary(e => ProtocolElementId.For(Study, e).Value, e => e);
        string Name(NodeRef node) => names.TryGetValue(node.Id, out var element) ? element : node.Kind;
        return edges.Select(e => $"{Name(e.From)} -{e.Kind}-> {Name(e.To)}");
    }
}
