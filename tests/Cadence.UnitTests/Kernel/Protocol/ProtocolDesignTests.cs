using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Simulation;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Protocol;

/// <summary>
/// The protocol design document (spec §6.2, ADR-2472): strict reading, stable element ids and references by kind
/// (K-PROT-1), the structural diff applied to designs, the K-PROT-3 design findings and the references a design makes
/// outside itself (K-SEM-1).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ProtocolDesignTests
{
    private static readonly ConceptRef Concept = new(ConceptId.New(), 2);
    private static readonly CodeSystemVersionId CodeSystemVersion = CodeSystemVersionId.New();
    private static readonly ArtifactId Artifact = ArtifactId.New();

    private static ProtocolDesign Complete() => ProtocolDesignSamples.Complete(Concept, CodeSystemVersion, Artifact);

    private static IReadOnlyList<ElementChange> Diff(ProtocolDesign before, ProtocolDesign after) =>
        ProtocolStructuralDiff.Compare(ProtocolDesignDocument.Write(before), ProtocolDesignDocument.Write(after)).Value!;

    /// <summary>A design that uses every element kind survives writing and reading unchanged.</summary>
    [Fact(DisplayName = "Document_RoundTrips_EveryElementKind")]
    public void DocumentRoundTripsEveryElementKind()
    {
        var json = ProtocolDesignDocument.Write(Complete());

        var read = ProtocolDesignDocument.Read(json);

        read.Problems.Should().BeEmpty();
        read.IsValid.Should().BeTrue();
        ProtocolDesignDocument.Write(read.Design!).Should().Be(json);
        read.Design!.Elements().Select(e => e.Kind).Distinct().Should().BeEquivalentTo(Enum.GetValues<ProtocolElementKind>());
    }

    /// <summary>The document is camel case with enums by name, and leaves out what is not set.</summary>
    [Fact(DisplayName = "Document_IsCamelCase_WithEnumNames")]
    public void DocumentIsCamelCaseWithEnumNames()
    {
        var json = ProtocolDesignSamples.Minimal;

        json.Should().Contain("\"schemaVersion\":1").And.Contain("\"title\":\"CARDIO-1\"").And.Contain("\"type\":\"PlaceboComparator\"");
        json.Should().NotContain("randomizationScheme");
    }

    /// <summary>A list the document leaves out reads as empty.</summary>
    [Fact(DisplayName = "Read_MissingLists_AreEmpty")]
    public void ReadMissingListsAreEmpty()
    {
        var read = ProtocolDesignDocument.Read("""{"schemaVersion":1,"title":"CARDIO-1"}""");

        read.Problems.Should().BeEmpty();
        read.Design!.Arms.Should().BeEmpty();
        read.Design.CollectionSpecs.Should().BeEmpty();
        read.Design.Elements().Should().BeEmpty();
    }

    /// <summary>
    /// Reading is strict: a missing schema version, unknown properties, missing or null required values, null list
    /// entries, integer enums that name nothing and concept references without a concept or a version are refused.
    /// </summary>
    [Theory(DisplayName = "Read_RefusesMalformedDocuments")]
    [InlineData("""{"schemaVersion":1,"title":"T","arms":[{"id":"arm.A","name":"A","type":"Experimental","interventionIds":[],"colour":"red"}]}""")]
    [InlineData("""{"schemaVersion":1,"title":"T","arms":[{"id":"arm.A","name":"A","type":"Experimental"}]}""")]
    [InlineData("""{"schemaVersion":1,"title":"T","arms":[{"id":"arm.A","name":null,"type":"Experimental","interventionIds":[]}]}""")]
    [InlineData("""{"schemaVersion":1,"title":"T","arms":[null]}""")]
    [InlineData("""{"schemaVersion":1,"title":"T","arms":[{"id":"arm.A","name":"A","type":"Unknown","interventionIds":[]}]}""")]
    [InlineData("""{"schemaVersion":1,"title":"T","arms":[{"id":"arm.A","name":"A","type":42,"interventionIds":[]}]}""")]
    [InlineData("""{"schemaVersion":1,"title":"T","eligibilityCriteria":[{"id":"ic.1","category":"Inclusion","text":"Adult","concepts":[{"conceptId":"0192f000-0000-7000-8000-000000000001","version":0}]}]}""")]
    [InlineData("""{"schemaVersion":1,"title":"T","eligibilityCriteria":[{"id":"ic.1","category":"Inclusion","text":"Adult","concepts":[{"conceptId":"00000000-0000-0000-0000-000000000000","version":1}]}]}""")]
    [InlineData("""{"schemaVersion":1,"title":"T","epochs":[{"id":"ep.1","name":"Screening","order":1}],"plannedActivities":[{"id":"act.1","name":"Visit","epochId":"ep.1","locations":[null],"collectionSpecIds":[]}]}""")]
    [InlineData("""{"title":"T"}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    public void ReadRefusesMalformedDocuments(string content)
    {
        var read = ProtocolDesignDocument.Read(content);

        read.IsValid.Should().BeFalse();
        read.Design.Should().BeNull();
        read.Problems.Should().NotBeEmpty();
    }

    /// <summary>Every structural problem is reported at once, each naming the element it is about.</summary>
    [Fact(DisplayName = "Read_ReportsEveryStructuralProblem")]
    public void ReadReportsEveryStructuralProblem()
    {
        var design = Complete() with
        {
            SchemaVersion = 2,
            Objectives = [new Objective("1bad", ObjectiveLevel.Primary, "Starts with a digit")],
            Epochs = [new Epoch("ep.screening", "Screening", 1), new Epoch("ep.screening", "Again", 2), new Epoch("ep.treatment", "Treatment", 3)],
            Cohorts = [new Cohort("coh.1", "Main", ["int.1"])],
            Estimands = [Complete().Estimands[0] with { PopulationId = "pop.missing" }],
            Interventions = [new Intervention("int.1", "Drug X", Dose: new Dose(0, "mg"))],
            PlannedActivities = [Complete().PlannedActivities[0] with { Timing = new ActivityTiming(84, -1, 3) }],
            StatisticalDesign = new DesignReference("sap.1", " "),
        };

        var read = ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(design));

        read.IsValid.Should().BeFalse();
        read.Problems.Should().BeEquivalentTo(
            "schemaVersion 2 is not supported; expected 1.",
            "Objective id '1bad' must start with a letter and use only letters, digits and _ . : - (at most 128).",
            "Element id 'ep.screening' is used more than once.",
            "ep.1: objectiveId 'obj.1' is not an element of this design.",
            "est.1: populationId 'pop.missing' is not an element of this design.",
            "coh.1: armIds 'int.1' is of kind Intervention, not Arm.",
            "int.1: a dose needs a positive amount and a UCUM unit.",
            "act.week12: timing windows cannot be negative.",
            "sap.1: title is required.");
    }

    /// <summary>An id longer than the limit is refused, and one at the limit is accepted.</summary>
    [Fact(DisplayName = "Read_ElementIds_AreAtMost128Characters")]
    public void ReadElementIdsAreAtMost128Characters()
    {
        var atLimit = new string('a', ProtocolDesignDocument.MaxIdLength);
        var design = ProtocolDesignSamples.MinimalDesign() with { Epochs = [new Epoch(atLimit, "Screening", 1)] };

        ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(design)).IsValid.Should().BeTrue();
        ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(design with { Epochs = [new Epoch(atLimit + "a", "Screening", 1)] }))
            .IsValid.Should().BeFalse();
    }

    /// <summary>Data purposes, units, terminology bindings and artifacts must name something.</summary>
    [Fact(DisplayName = "Read_RefusesEmptyPurposesUnitsBindingsAndArtifacts")]
    public void ReadRefusesEmptyPurposesUnitsBindingsAndArtifacts()
    {
        var complete = Complete();
        var spec = complete.CollectionSpecs[0];
        var design = complete with
        {
            CollectionSpecs =
            [
                spec with
                {
                    Purpose = new DataPurpose { SafetyRuleIds = ["disc.1"] },
                    Items =
                    [
                        spec.Items[0] with { Unit = " ", Purpose = new DataPurpose { EstimandIds = ["ep.1"] } },
                        spec.Items[1] with { Terminology = new TerminologyBinding(default) },
                    ],
                },
            ],
            RandomizationScheme = new DesignReference("rand.1", "1:1", default(ArtifactId)),
        };

        var read = ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(design));

        read.Problems.Should().BeEquivalentTo(
            "cs.vitals: purpose.safetyRuleIds 'disc.1' is of kind DiscontinuationRule, not SafetyRule.",
            "item.sbp: unit is blank; leave it out or give a UCUM code.",
            "item.sbp: purpose.estimandIds 'ep.1' is of kind Endpoint, not Estimand.",
            "item.position: terminology names no code-system version.",
            "rand.1: artifact is empty; leave it out or name the artifact.");
    }

    /// <summary>
    /// An estimand's compared interventions must be interventions of the design, and an intercurrent-event strategy
    /// stated only in words (as USDM allows) must carry those words; the valid forms round trip.
    /// </summary>
    [Fact(DisplayName = "Read_ChecksEstimandInterventionsAndStrategiesStatedInWords")]
    public void ReadChecksEstimandInterventionsAndStrategiesStatedInWords()
    {
        var complete = Complete();
        var estimand = complete.Estimands[0];
        var invalid = complete with
        {
            Estimands =
            [
                estimand with
                {
                    InterventionIds = ["arm.A", "int.missing"],
                    IntercurrentEvents = [new IntercurrentEvent("Rescue medication", IntercurrentEventStrategy.Other, " ")],
                },
            ],
        };
        var valid = complete with
        {
            Estimands =
            [
                estimand with
                {
                    InterventionIds = ["int.1"],
                    IntercurrentEvents = [new IntercurrentEvent("Rescue medication", IntercurrentEventStrategy.Other, "Values after rescue are set to missing.")],
                },
            ],
        };

        ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(invalid)).Problems.Should().BeEquivalentTo(
            "est.1: interventionIds 'arm.A' is of kind Arm, not Intervention.",
            "est.1: interventionIds 'int.missing' is not an element of this design.",
            "est.1: intercurrentEvents.strategyText is required.");
        ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(valid)).Design.Should().BeEquivalentTo(valid);
    }

    /// <summary>
    /// The kernel's structural diff (ADR-2480 §4.6) applies to design documents as they are: a design has no differences
    /// from itself, and elements are matched by their stable ids, so a reworded objective is one changed element and
    /// an added arm or a removed rule is one change each (K-PROT-1).
    /// </summary>
    [Fact(DisplayName = "StructuralDiff_MatchesDesignElementsById")]
    [KernelRule("K-PROT-1")]
    public void StructuralDiffMatchesDesignElementsById()
    {
        var before = Complete();
        var after = before with
        {
            Objectives = [before.Objectives[0] with { Text = "Show that the drug lowers SBP at week 12." }],
            Arms = [.. before.Arms, new Arm("arm.C", "Cohort C", ArmType.Experimental, [])],
            DiscontinuationRules = [],
        };

        Diff(before, before).Should().BeEmpty();
        Diff(before, after).Should().BeEquivalentTo(
            [
                new ElementChange(ProtocolStructuralDiff.RootId, null, ElementChangeKind.Changed, ["/arms", "/discontinuationRules"]),
                new ElementChange("arm.C", null, ElementChangeKind.Added, []),
                new ElementChange("disc.1", null, ElementChangeKind.Removed, []),
                new ElementChange("obj.1", null, ElementChangeKind.Changed, ["/text"]),
            ],
            options => options.WithStrictOrdering());
    }

    /// <summary>A changed collection item is reported on the item; its specification changes only by its own fields.</summary>
    [Fact(DisplayName = "StructuralDiff_ItemChange_IsReportedOnTheItem")]
    public void StructuralDiffItemChangeIsReportedOnTheItem()
    {
        var before = Complete();
        var spec = before.CollectionSpecs[0];
        var itemChanged = before with { CollectionSpecs = [spec with { Items = [spec.Items[0] with { Unit = "kPa" }, spec.Items[1]] }] };
        var specRenamed = before with { CollectionSpecs = [spec with { Name = "Vitals" }] };

        Diff(before, itemChanged).Should().BeEquivalentTo([new ElementChange("item.sbp", null, ElementChangeKind.Changed, ["/unit"])]);
        Diff(before, specRenamed).Should().BeEquivalentTo([new ElementChange("cs.vitals", null, ElementChangeKind.Changed, ["/name"])]);
    }

    /// <summary>A design whose scientific chain is explicit has no findings (K-PROT-3).</summary>
    [Fact(DisplayName = "Findings_CompleteChain_HasNone")]
    [KernelRule("K-PROT-3")]
    public void FindingsCompleteChainHasNone() =>
        ProtocolDesignReview.Findings(Complete()).Should().BeEmpty();

    /// <summary>
    /// An objective without an endpoint, a primary endpoint without an estimand, an unscheduled specification and an
    /// item without a purpose are each a finding naming the element.
    /// </summary>
    [Fact(DisplayName = "Findings_ReportEachBrokenLink")]
    public void FindingsReportEachBrokenLink()
    {
        var complete = Complete();
        var design = complete with
        {
            Objectives = [.. complete.Objectives, new Objective("obj.2", ObjectiveLevel.Secondary, "Quality of life")],
            Endpoints = [.. complete.Endpoints, new Endpoint("ep.2", "obj.1", ObjectiveLevel.Primary, "Diastolic pressure")],
            CollectionSpecs =
            [
                .. complete.CollectionSpecs,
                new CollectionSpec("cs.extra", "Extra", [new CollectionItem("item.weight", Concept, "kg")]),
            ],
        };

        ProtocolDesignReview.Findings(design).Select(f => (f.ElementId, f.Code)).Should().Equal(
            ("obj.2", ProtocolDesignReview.ObjectiveWithoutEndpoint),
            ("ep.2", ProtocolDesignReview.PrimaryEndpointWithoutEstimand),
            ("cs.extra", ProtocolDesignReview.SpecNotScheduled),
            ("item.weight", ProtocolDesignReview.ItemWithoutPurpose));
    }

    /// <summary>An item inherits a purpose from its specification or from any activity that collects it.</summary>
    [Fact(DisplayName = "Findings_ItemPurpose_IsInherited")]
    public void FindingsItemPurposeIsInherited()
    {
        var complete = Complete();
        var bare = new CollectionSpec("cs.bare", "Bare", [new CollectionItem("item.weight", Concept, "kg")]);
        var collectedBy = complete.PlannedActivities[0] with { CollectionSpecIds = ["cs.vitals", "cs.bare"] };
        var withoutPurpose = complete with { CollectionSpecs = [.. complete.CollectionSpecs, bare], PlannedActivities = [collectedBy] };

        ProtocolDesignReview.Findings(withoutPurpose).Select(f => f.ElementId).Should().Equal("item.weight");
        ProtocolDesignReview.Findings(withoutPurpose with
        {
            CollectionSpecs = [.. complete.CollectionSpecs, bare with { Purpose = new DataPurpose { EndpointIds = ["ep.1"] } }],
        }).Should().BeEmpty();
        ProtocolDesignReview.Findings(withoutPurpose with
        {
            PlannedActivities = [collectedBy with { Purpose = new DataPurpose { OperationalNeed = "Dosing by weight" } }],
        }).Should().BeEmpty();
    }

    /// <summary>A design lists every concept, pinned code-system version and artifact it refers to (K-SEM-1).</summary>
    [Fact(DisplayName = "ExternalReferences_ListEveryOutsideReference")]
    public void ExternalReferencesListEveryOutsideReference()
    {
        var product = new ConceptRef(ConceptId.New(), 1);
        var complete = Complete();
        var design = complete with { Interventions = [complete.Interventions[0] with { Product = product }] };

        var references = design.ExternalReferences();

        references.Concepts.Should().BeEquivalentTo([Concept, product]);
        references.CodeSystemVersions.Should().BeEquivalentTo([CodeSystemVersion]);
        references.Artifacts.Should().BeEquivalentTo([Artifact]);
        ProtocolDesignSamples.MinimalDesign().ExternalReferences().Concepts.Should().BeEmpty();
    }
}
