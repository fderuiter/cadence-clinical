using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Conformance;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Validation;
using Cadence.Rules.Design;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Standards;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Standards;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Standards;

/// <summary>
/// Design-time conformance (K-STD-2, ADR-2464): the design facts derived from a protocol design, its study's mapping
/// specifications, the SDTMIG 3.4 seed and controlled terminology, and the shipped design-stage rule pack over them.
/// </summary>
public sealed class DesignConformanceTests
{
    private static readonly ConceptRef Systolic = new(ConceptId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-000000000001")), 1);
    private static readonly ConceptRef Position = new(ConceptId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-000000000003")), 1);
    private static readonly StudyId Study = StudyId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-0000000000b1"));
    private static readonly StandardVersionId Sdtmig = StandardVersionId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-0000000000a1"));
    private static readonly CodeSystemVersionId SdtmCt = CodeSystemVersionId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-0000000000c1"));
    private static readonly CodeSystemVersionId OtherCt = CodeSystemVersionId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-0000000000c2"));

    private static readonly StandardPackage SdtmigPackage =
        StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package;

    /// <summary>The shipped pack holds eight design-stage rules, each with a citation and an explanation.</summary>
    [Fact]
    public void Pack_HoldsDesignStageRules()
    {
        var pack = DesignConformancePack.Pack;

        pack.Rules.Select(r => r.Id).Should().Equal("DC-001", "DC-002", "DC-003", "DC-004", "DC-005", "DC-006", "DC-007", "DC-008");
        pack.Rules.Should().OnlyContain(r => r.Applicability.Stage == ValidationStage.Design);
        pack.Rules.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Explanation) && !string.IsNullOrWhiteSpace(r.Citation.Document));
        pack.Rules.Where(r => r.Severity == ValidationSeverity.Error).Select(r => r.Id)
            .Should().Equal("DC-001", "DC-002", "DC-003", "DC-004", "DC-005", "DC-006");
    }

    /// <summary>A design whose items the study maps conformantly has no problems and raises no finding.</summary>
    [Fact]
    public async Task Build_ConformantDesign_HasNoProblems()
    {
        var records = await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt), Vitals());

        records.Where(r => r.Dataset == DesignConformance.ConceptDataset).Should().HaveCount(2)
            .And.OnlyContain(r => r.Values[DesignConformance.Mapped] == "Y");
        var items = records.Where(r => r.Dataset == DesignConformance.ItemDataset).ToList();
        items.Select(r => r.Values[DesignConformance.Item]).Should().Equal("item.sbp", "item.position");
        items[0].Key.Should().Be(NodeRef.For(ProtocolElementId.For(Study, "item.sbp")));
        items.Should().OnlyContain(r => ProblemsOf(r).Count == 0, string.Join(" | ", items.SelectMany(ProblemsOf)));
        Evaluate(records).Should().BeEmpty();
    }

    /// <summary>
    /// Spec §14 test 8: an item whose unit has no conformant SDTM representation (no CT unit term, no conversion into
    /// the standard unit) raises the blocking design-stage finding DC-002 on the item.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-CDR-002")]
    public async Task Build_UnitWithNoConformantRepresentation_RaisesBlockingFinding()
    {
        var records = await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt, unit: "cm[H2O]"), Vitals());

        var sbp = Item(records, "item.sbp");
        sbp.Values[DesignConformance.UnitProblem].Should()
            .Contain("sdtm-vitals v1 mapping vs-sysbp")
            .And.Contain("cm[H2O] has no controlled-terminology unit term")
            .And.Contain("no conversion from cm[H2O] into the standard unit mm[Hg]");
        var violation = Evaluate(records).Should().ContainSingle().Subject;
        violation.RuleId.Should().Be("DC-002");
        violation.Severity.Should().Be(ValidationSeverity.Error);
        violation.RecordKey.Should().Be(sbp.Key.ToString());
    }

    /// <summary>A unit with a CT term and a conversion into the standard unit is conformant.</summary>
    [Fact]
    public async Task Build_UnitWithTermAndConversion_IsConformant()
    {
        var records = await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt, unit: "kPa"), Vitals());

        Item(records, "item.sbp").Values[DesignConformance.UnitProblem].Should().BeNull();
        Evaluate(records).Should().BeEmpty();
    }

    /// <summary>A Concept no pinned specification maps raises DC-001 on the Concept, the target of a mapping-gap finding.</summary>
    [Fact]
    public async Task Build_UnmappedConcept_RaisesMappingGapOnTheConcept()
    {
        var records = await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt), Pin("sdtm-vitals", SampleMappingSpecifications.SystolicBloodPressure(Systolic)));

        var violation = Evaluate(records).Should().ContainSingle().Subject;
        violation.RuleId.Should().Be("DC-001");
        violation.Dataset.Should().Be(MappingGap.Dataset);
        violation.RecordKey.Should().Be(MappingGap.Target(Position.ConceptId).Key);
        Item(records, "item.position").Values[DesignConformance.Mapped].Should().Be("N");
    }

    /// <summary>A study that pins no mapping specification reports every Concept as unmapped.</summary>
    [Fact]
    public async Task Build_NoMappings_EveryConceptIsUnmapped()
    {
        var records = await DesignConformance.BuildAsync(
            new DesignConformanceInput(Study, SampleDesignConformance.Vitals(Systolic, Position, SdtmCt), null, Packages()),
            new Terminology(),
            TestContext.Current.CancellationToken);

        Evaluate(records).Select(v => v.RuleId).Should().Equal("DC-001", "DC-001");
    }

    /// <summary>K-PROT-3: an item with no path to an endpoint, estimand, safety rule or need raises the warning DC-007.</summary>
    [Fact]
    public async Task Build_ItemWithoutPurpose_RaisesWarning()
    {
        var records = await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt, withPurpose: false), Vitals());

        Item(records, "item.sbp").Values[DesignConformance.Purpose].Should().Be("N");
        var violation = Evaluate(records).Should().ContainSingle().Subject;
        violation.RuleId.Should().Be("DC-007");
        violation.Severity.Should().Be(ValidationSeverity.Warning);
    }

    /// <summary>A result written to a variable of the wrong type, or a variable the dataset lacks, raises DC-003.</summary>
    [Fact]
    public async Task Build_WrongVariableTypes_RaiseTypeFinding()
    {
        var sbp = SampleMappingSpecifications.SystolicBloodPressure(Systolic) with
        {
            Result = new ResultVariables("VSORRES", "VSORRESU", "VSSTRESC", "VSSTRESC", "VSSTRESU"),
        };
        var numericAssign = SampleMappingSpecifications.SystolicBloodPressure(Systolic) with
        {
            Id = "vs-sysbp-2",
            Assign = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["VSTESTCD"] = "SYSBP", ["VSTEST"] = "Systolic Blood Pressure", ["VSSTRESN"] = "high" },
        };

        var typedRecords = await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt), Pin("a", sbp, SampleMappingSpecifications.Position(Position)));
        var typed = Item(typedRecords, "item.sbp");
        var assigned = Item(await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt), Pin("a", numericAssign, SampleMappingSpecifications.Position(Position))), "item.sbp");

        typed.Values[DesignConformance.TypeProblem].Should().Contain("goes to VSSTRESC, which is Char, not Num");
        var missing = SampleMappingSpecifications.SystolicBloodPressure(Systolic) with
        {
            Assign = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["VSTEST"] = "Systolic Blood Pressure", ["VSTSTCD"] = "SYSBP" },
            Result = new ResultVariables("VSORRES", "VSORRESU", "VSSTRESC", "VSSTRESX", "VSSTRESU"),
        };
        var absent = Item(await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt), Pin("a", missing, SampleMappingSpecifications.Position(Position))), "item.sbp");
        absent.Values[DesignConformance.TypeProblem].Should().Contain("VSSTRESX is not a variable of VS").And.Contain("VSTSTCD is not a variable of VS");
        Evaluate(typedRecords).Should().ContainSingle().Which.RuleId.Should().Be("DC-003");
        assigned.Values[DesignConformance.TypeProblem].Should().Contain("VSSTRESN is numeric, but the mapping assigns \"high\"");
    }

    /// <summary>
    /// An item coded from another codelist or terminology version than the mapping decodes raises DC-004; a coded
    /// mapping over an item with a unit is a unit problem.
    /// </summary>
    [Fact]
    public async Task Build_TerminologyDisagreement_RaisesTerminologyFinding()
    {
        var otherVersion = SampleDesignConformance.Vitals(Systolic, Position, OtherCt);
        var otherCodelist = SampleDesignConformance.Vitals(Systolic, Position, SdtmCt) with
        {
            CollectionSpecs =
            [
                SampleDesignConformance.Vitals(Systolic, Position, SdtmCt).CollectionSpecs[0] with
                {
                    Items =
                    [
                        SampleDesignConformance.Vitals(Systolic, Position, SdtmCt).CollectionSpecs[0].Items[0],
                        SampleDesignConformance.Vitals(Systolic, Position, SdtmCt).CollectionSpecs[0].Items[1] with
                        {
                            Terminology = new Cadence.Kernel.Protocol.Design.TerminologyBinding(SdtmCt, "C71148"),
                        },
                    ],
                },
            ],
        };

        var byVersion = await BuildAsync(otherVersion, Vitals());
        var byCodelist = await BuildAsync(otherCodelist, Vitals());

        Item(byVersion, "item.position").Values[DesignConformance.TerminologyProblem].Should().Contain("terminology version the specification does not use");
        Evaluate(byVersion).Should().ContainSingle().Which.RuleId.Should().Be("DC-004");
        Item(byCodelist, "item.position").Values[DesignConformance.TerminologyProblem].Should().Contain("records codes from C71148, but the mapping decodes C78431");
        Evaluate(byCodelist).Should().ContainSingle().Which.RuleId.Should().Be("DC-004");

        var codedOverUnit = await BuildAsync(
            SampleDesignConformance.Vitals(Systolic, Position, SdtmCt),
            Pin("a", SampleMappingSpecifications.Position(Systolic) with { Id = "vs-sysbp-coded" }, SampleMappingSpecifications.Position(Position)));
        Item(codedOverUnit, "item.sbp").Values[DesignConformance.UnitProblem].Should().Contain("decodes a coded result, but the item records a unit (mm[Hg])");
    }

    /// <summary>
    /// A coded mapping needs an item that records codes from a codelist, and a codelist the specification's terminology
    /// holds; neither is assumed.
    /// </summary>
    [Fact]
    public async Task Build_CodedMappingThatCannotDecode_RaisesTerminologyFinding()
    {
        var design = SampleDesignConformance.Vitals(Systolic, Position, SdtmCt);
        var uncoded = design with
        {
            CollectionSpecs = [design.CollectionSpecs[0] with { Items = [design.CollectionSpecs[0].Items[0], design.CollectionSpecs[0].Items[1] with { Terminology = null }] }],
        };
        var unknownCodelist = design with
        {
            CollectionSpecs =
            [
                design.CollectionSpecs[0] with
                {
                    Items = [design.CollectionSpecs[0].Items[0], design.CollectionSpecs[0].Items[1] with { Terminology = new Cadence.Kernel.Protocol.Design.TerminologyBinding(SdtmCt, "C99999") }],
                },
            ],
        };
        var decodesUnknown = Pin(
            "a", SampleMappingSpecifications.SystolicBloodPressure(Systolic), SampleMappingSpecifications.Position(Position) with { Codelist = "C99999" });

        var noCodes = Item(await BuildAsync(uncoded, Vitals()), "item.position");
        var notInCt = await BuildAsync(unknownCodelist, decodesUnknown);

        noCodes.Values[DesignConformance.TerminologyProblem].Should().Contain("decodes codes from C78431, but the item records no codes from a codelist");
        Item(notInCt, "item.position").Values[DesignConformance.TerminologyProblem].Should().Contain("decodes codes from C99999, which none of the specification's terminology versions has");
        Evaluate(notInCt).Should().ContainSingle().Which.RuleId.Should().Be("DC-004");
    }

    /// <summary>
    /// The unit facts agree with what the mapping engine refuses: a unit the mapping has no variable to write, and a
    /// value recorded without a unit for a mapping that standardises into one.
    /// </summary>
    [Fact]
    public async Task Build_UnitTheMappingCannotWrite_RaisesUnitFinding()
    {
        var noUnitVariables = SampleMappingSpecifications.SystolicBloodPressure(Systolic) with
        {
            Result = new ResultVariables("VSORRES", StandardCharacter: "VSSTRESC", StandardNumeric: "VSSTRESN"),
        };
        var characterOnly = SampleMappingSpecifications.SystolicBloodPressure(Systolic) with
        {
            Result = new ResultVariables("VSORRES", "VSORRESU", "VSSTRESC", StandardUnit: "VSSTRESU"),
        };

        var withUnit = Item(await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt), Pin("a", noUnitVariables, SampleMappingSpecifications.Position(Position))), "item.sbp");
        var withoutUnit = Item(
            await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt, unit: null), Pin("a", characterOnly, SampleMappingSpecifications.Position(Position))),
            "item.sbp");

        withUnit.Values[DesignConformance.UnitProblem].Should().Contain("the mapping writes no unit, but the item records mm[Hg]");
        withoutUnit.Values[DesignConformance.UnitProblem].Should().Contain("standardises results into mm[Hg], but the item records no unit");
    }

    /// <summary>A constant outside the values the standard lists for its variable raises DC-005; a long one raises DC-006.</summary>
    [Fact]
    public async Task Build_AssignedValues_AreCheckedAgainstTheStandard()
    {
        var sbp = SampleMappingSpecifications.SystolicBloodPressure(Systolic) with
        {
            Assign = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["DOMAIN"] = "LB",
                ["VSTEST"] = "Systolic Blood Pressure",
                ["VSTESTCD"] = "SYSBP",
                ["VSCAT"] = new string('x', DesignConformance.MaxCharacterBytes + 1),
            },
        };

        var records = await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt), Pin("a", sbp, SampleMappingSpecifications.Position(Position)));

        var item = Item(records, "item.sbp");
        item.Values[DesignConformance.ValueProblem].Should().Contain("DOMAIN = \"LB\" is not one of the values the standard lists (VS)");
        item.Values[DesignConformance.LengthProblem].Should().Contain("VSCAT is longer than 200 bytes");
        Evaluate(records).Select(v => v.RuleId).Should().Equal("DC-005", "DC-006");
    }

    /// <summary>
    /// A value outside its variable's codelists raises the warning DC-008: CDISC codelists may be extensible, so a
    /// sponsor term is a fact to confirm, not a block.
    /// </summary>
    [Fact]
    public async Task Build_ValueOutsideTheCodelist_RaisesTermWarning()
    {
        var sbp = SampleMappingSpecifications.SystolicBloodPressure(Systolic) with
        {
            Assign = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["VSTEST"] = "Systolic Blood Pressure", ["VSTESTCD"] = "SYSBPX" },
        };

        var records = await BuildAsync(SampleDesignConformance.Vitals(Systolic, Position, SdtmCt), Pin("a", sbp, SampleMappingSpecifications.Position(Position)));

        Item(records, "item.sbp").Values[DesignConformance.TermProblem].Should().Contain("VSTESTCD = \"SYSBPX\" is not a term of C66741");
        var violation = Evaluate(records).Should().ContainSingle().Subject;
        violation.RuleId.Should().Be("DC-008");
        violation.Severity.Should().Be(ValidationSeverity.Warning);
    }

    /// <summary>
    /// A CT version the tenant has not imported is a blocking terminology problem, and a codelist none of the
    /// specification's terminology versions has is reported; neither is assumed to pass.
    /// </summary>
    [Fact]
    public async Task Build_MissingTerminology_IsATermProblem()
    {
        var records = await DesignConformance.BuildAsync(
            new DesignConformanceInput(Study, SampleDesignConformance.Vitals(Systolic, Position, SdtmCt), Mappings(Vitals()), Packages()),
            new Terminology(resolves: false),
            TestContext.Current.CancellationToken);

        Item(records, "item.sbp").Values[DesignConformance.TermProblem].Should().Contain("which none of the specification's terminology versions has");
        Item(records, "item.sbp").Values[DesignConformance.TerminologyProblem].Should().Contain("CT version sdtmct 2024-09-27 is not imported");
        Item(records, "item.position").Values[DesignConformance.TerminologyProblem].Should().Contain("terminology version the specification does not use");
        Evaluate(records).Where(v => v.Severity == ValidationSeverity.Error).Select(v => v.RuleId).Should().Equal("DC-004", "DC-004");
    }

    /// <summary>A mapping into a dataset the registered package lacks is a type problem, not a crash.</summary>
    [Fact]
    public async Task Build_MissingPackage_IsATypeProblem()
    {
        var records = await DesignConformance.BuildAsync(
            new DesignConformanceInput(Study, SampleDesignConformance.Vitals(Systolic, Position, SdtmCt), Mappings(Vitals()), new Dictionary<StandardVersionId, StandardPackage>()),
            new Terminology(),
            TestContext.Current.CancellationToken);

        Item(records, "item.sbp").Values[DesignConformance.TypeProblem].Should().Contain("dataset VS is not in the registered package");
    }

    private static PinnedMappingSpecification Vitals() =>
        Pin("sdtm-vitals", SampleMappingSpecifications.SystolicBloodPressure(Systolic), SampleMappingSpecifications.Position(Position));

    private static PinnedMappingSpecification Pin(string key, params VariableMapping[] mappings) =>
        new(LibraryItemId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-0000000000d1")), key, 1, 1, Sdtmig, SampleMappingSpecifications.Specification(mappings));

    private static StudyMappings Mappings(PinnedMappingSpecification pin)
    {
        var (mappings, problems) = StudyMappings.Resolve([pin], [Sdtmig]);
        problems.Should().BeEmpty();
        return mappings!;
    }

    private static Dictionary<StandardVersionId, StandardPackage> Packages() => new() { [Sdtmig] = SdtmigPackage };

    private static Task<IReadOnlyList<DesignConformanceRecord>> BuildAsync(Cadence.Kernel.Protocol.Design.ProtocolDesign design, PinnedMappingSpecification pin) =>
        DesignConformance.BuildAsync(new DesignConformanceInput(Study, design, Mappings(pin), Packages()), new Terminology(), TestContext.Current.CancellationToken);

    private static DesignConformanceRecord Item(IReadOnlyList<DesignConformanceRecord> records, string id) =>
        records.Single(r => r.Dataset == DesignConformance.ItemDataset && r.Values[DesignConformance.Item] == id);

    private static List<string> ProblemsOf(DesignConformanceRecord record) =>
        [.. record.Values.Where(v => v.Key.EndsWith("_PROBLEM", StringComparison.Ordinal) && v.Value is not null).Select(v => $"{v.Key}: {v.Value}")];

    private static IReadOnlyList<RuleViolation> Evaluate(IReadOnlyList<DesignConformanceRecord> records) =>
        new DotNetRuleEvaluator().Evaluate(
            DesignConformancePack.Pack,
            new RuleEvaluationContext(ValidationStage.Design, new DateOnly(2026, 9, 30)),
            records.Select(r => new RuleRecord(r.Dataset, r.Key.ToString(), r.Values.ToDictionary(v => v.Key, v => RuleValue.FromText(v.Value), StringComparer.Ordinal))));

    /// <summary>The sample CT: SDTM CT resolves to one version holding the codelists the SDTMIG VS variables name.</summary>
    private sealed class Terminology(bool resolves = true) : IDesignTerminology
    {
        private static readonly Dictionary<string, IReadOnlySet<string>> Codelists = new(StringComparer.Ordinal)
        {
            ["C66741"] = new HashSet<string>(["SYSBP"], StringComparer.Ordinal),
            ["C67153"] = new HashSet<string>(["Systolic Blood Pressure"], StringComparer.Ordinal),
            ["C66770"] = new HashSet<string>(["mmHg", "kPa"], StringComparer.Ordinal),
            ["C71148"] = new HashSet<string>(["SITTING", "STANDING", "SUPINE"], StringComparer.Ordinal),
            ["C78431"] = new HashSet<string>(["SITTING", "STANDING", "SUPINE"], StringComparer.Ordinal),
        };

        public Task<CodeSystemVersionId?> ResolveAsync(CodeSystemVersionRef version, CancellationToken cancellationToken) =>
            Task.FromResult<CodeSystemVersionId?>(resolves && version == SampleMappingSpecifications.SdtmCt ? SdtmCt : null);

        public Task<IReadOnlySet<string>?> SubmissionValuesAsync(CodeSystemVersionId version, string codelist, CancellationToken cancellationToken) =>
            Task.FromResult(version == SdtmCt ? Codelists.GetValueOrDefault(codelist) : null);
    }
}
