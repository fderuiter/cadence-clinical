using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Validation;
using Cadence.Standards.Mapping;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Standards;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Standards;

/// <summary>
/// Mapping specifications (spec §6.3, ADR-2463): the JSON format, the .NET mapping engine, and how a study's pinned
/// specifications and overrides resolve to one mapping per Concept.
/// </summary>
public sealed class MappingSpecificationTests
{
    private static readonly ConceptRef Systolic = new(ConceptId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-000000000001")), 1);
    private static readonly ConceptRef Glucose = new(ConceptId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-000000000002")), 2);
    private static readonly ConceptRef Position = new(ConceptId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-000000000003")), 1);
    private static readonly StandardVersionId Sdtmig = StandardVersionId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-0000000000a1"));

    private readonly MappingEngine _engine = new();

    /// <summary>
    /// Validates the VS acceptance case of #4810: systolic blood pressure collected as 120 mm[Hg] maps to VSTESTCD
    /// SYSBP with VSORRES/VSORRESU as collected and VSSTRESC/VSSTRESN/VSSTRESU standardised, units written as their CT
    /// terms.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-CDR-002")]
    public void Map_SystolicBloodPressure_WritesOriginalAndStandardResults()
    {
        var specification = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(Systolic));

        var result = _engine.Map(specification, new MappingInput(Systolic, "120", "mm[Hg]"));

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Problems));
        result.Dataset.Should().Be("VS");
        result.MappingId.Should().Be("vs-sysbp");
        result.Values.Should().Equal(
            new MappedValue("VSTEST", "Systolic Blood Pressure"),
            new MappedValue("VSTESTCD", "SYSBP"),
            new MappedValue("VSORRES", "120"),
            new MappedValue("VSORRESU", "mmHg"),
            new MappedValue("VSSTRESC", "120"),
            new MappedValue("VSSTRESN", "120"),
            new MappedValue("VSSTRESU", "mmHg"));
    }

    /// <summary>A result collected in another unit is converted into the standard unit and rounded as the mapping says.</summary>
    [Fact]
    public void Map_SystolicBloodPressureInKilopascals_ConvertsIntoTheStandardUnit()
    {
        var specification = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(Systolic));

        var result = _engine.Map(specification, new MappingInput(Systolic, "16", "kPa"));

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Problems));
        Value(result, "VSORRES").Should().Be("16");
        Value(result, "VSORRESU").Should().Be("kPa");
        Value(result, "VSSTRESN").Should().Be("120");
        Value(result, "VSSTRESU").Should().Be("mmHg");
    }

    /// <summary>
    /// Validates the LB acceptance case of #4810: glucose collected in conventional units (mg/dL) is standardised to SI
    /// (mmol/L) with the specification's factor and rounding; a result already in SI keeps its value.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-LBM-001")]
    public void Map_GlucoseInConventionalUnits_ConvertsToSi()
    {
        var specification = SampleMappingSpecifications.Specification(SampleMappingSpecifications.Glucose(Glucose));

        var conventional = _engine.Map(specification, new MappingInput(Glucose, "90", "mg/dL"));
        var si = _engine.Map(specification, new MappingInput(Glucose, "5.1", "mmol/L"));

        conventional.Succeeded.Should().BeTrue(string.Join(" ", conventional.Problems));
        conventional.Dataset.Should().Be("LB");
        conventional.Values.Should().Equal(
            new MappedValue("LBTEST", "Glucose"),
            new MappedValue("LBTESTCD", "GLUC"),
            new MappedValue("LBORRES", "90"),
            new MappedValue("LBORRESU", "mg/dL"),
            new MappedValue("LBSTRESC", "5.00"),
            new MappedValue("LBSTRESN", "5.00"),
            new MappedValue("LBSTRESU", "mmol/L"));
        Value(si, "LBSTRESN").Should().Be("5.10");
        Value(si, "LBORRESU").Should().Be("mmol/L");
    }

    /// <summary>A mapping matches any version of its Concept, so a collected value of a later concept version still maps.</summary>
    [Fact]
    public void Map_LaterConceptVersion_UsesTheConceptsMapping()
    {
        var specification = SampleMappingSpecifications.Specification(SampleMappingSpecifications.Glucose(Glucose));

        _engine.Map(specification, new MappingInput(new ConceptRef(Glucose.ConceptId, 3), "90", "mg/dL")).Succeeded.Should().BeTrue();
    }

    /// <summary>A text result is kept as collected, with no numeric or standard unit value.</summary>
    [Fact]
    public void Map_TextResult_KeepsTheCharacterResultOnly()
    {
        var specification = SampleMappingSpecifications.Specification(SampleMappingSpecifications.Glucose(Glucose));

        var result = _engine.Map(specification, new MappingInput(Glucose, "<1.0", "mmol/L"));

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Problems));
        Value(result, "LBSTRESC").Should().Be("<1.0");
        Value(result, "LBSTRESN").Should().BeNull();
        Value(result, "LBSTRESU").Should().BeNull();
    }

    /// <summary>
    /// A quantity collected with a unit must be a number or a bound; other text is a problem rather than a guess, and a
    /// conversion out of range is a problem rather than a crash.
    /// </summary>
    [Theory]
    [InlineData("1,5", "neither a number nor a bound")]
    [InlineData(" 90", "neither a number nor a bound")]
    [InlineData("high", "neither a number nor a bound")]
    [InlineData("=5", "neither a number nor a bound")]
    [InlineData("79000000000000000000000000000", "out of range")]
    public void Map_QuantityThatCannotBeStandardised_IsAProblem(string value, string expected)
    {
        var specification = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(Systolic));

        var result = _engine.Map(specification, new MappingInput(Systolic, value, "kPa"));

        result.Succeeded.Should().BeFalse();
        result.Values.Should().BeEmpty();
        result.Problems.Should().ContainSingle().Which.Should().Contain(expected);
    }

    /// <summary>Bounds such as &gt;=200 are kept as the character result.</summary>
    [Theory]
    [InlineData(">=200")]
    [InlineData(">5.5")]
    [InlineData("<=0.1")]
    public void Map_Bound_KeepsTheCharacterResult(string value)
    {
        var specification = SampleMappingSpecifications.Specification(SampleMappingSpecifications.Glucose(Glucose));

        var result = _engine.Map(specification, new MappingInput(Glucose, value, "mg/dL"));

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Problems));
        Value(result, "LBSTRESC").Should().Be(value);
        Value(result, "LBSTRESN").Should().BeNull();
    }

    /// <summary>Without rounding, the converted value is written exactly, without trailing zeros.</summary>
    [Fact]
    public void Map_WithoutDecimals_WritesTheExactValue()
    {
        var specification = SampleMappingSpecifications.Specification(SampleMappingSpecifications.Glucose(Glucose) with { Decimals = null });

        var result = _engine.Map(specification, new MappingInput(Glucose, "90", "mg/dL"));

        Value(result, "LBSTRESN").Should().Be("4.995");
    }

    /// <summary>A coded result is written as its CT submission value; a code outside the codelist is a problem.</summary>
    [Fact]
    public void Map_CodedResult_DecodesToTheSubmissionValue()
    {
        var specification = SampleMappingSpecifications.Specification(SampleMappingSpecifications.Position(Position));
        var decoder = new Decoder(new() { [("C78431", "C62122")] = "SITTING" });

        var sitting = _engine.Map(specification, new MappingInput(Position, "C62122"), decoder);
        var unknown = _engine.Map(specification, new MappingInput(Position, "C00000"), decoder);
        var noDecoder = _engine.Map(specification, new MappingInput(Position, "C62122"));

        sitting.Values.Should().Equal(new MappedValue("VSPOS", "SITTING"));
        unknown.Problems.Should().ContainSingle().Which.Should().Contain("C00000");
        unknown.Values.Should().BeEmpty();
        noDecoder.Succeeded.Should().BeFalse();
    }

    /// <summary>The engine never guesses: a missing mapping, unit, conversion or unit term is a problem and writes nothing.</summary>
    [Fact]
    public void Map_WhatTheSpecificationDoesNotCover_IsAProblem()
    {
        var specification = SampleMappingSpecifications.Specification(SampleMappingSpecifications.Glucose(Glucose));
        var noUnitTerms = specification with { Units = [] };

        _engine.Map(specification, new MappingInput(Systolic, "120", "mm[Hg]")).Problems.Should().ContainSingle().Which.Should().Contain("no mapping");
        _engine.Map(specification, new MappingInput(Glucose, "90")).Problems.Should().ContainSingle().Which.Should().Contain("no unit");
        _engine.Map(specification, new MappingInput(Glucose, "90", "g/L")).Problems.Should().Contain(p => p.Contains("no conversion from g/L", StringComparison.Ordinal));
        var missingTerm = _engine.Map(noUnitTerms, new MappingInput(Glucose, "90", "mg/dL"));
        missingTerm.Problems.Should().Contain(p => p.Contains("mg/dL", StringComparison.Ordinal));
        missingTerm.Values.Should().BeEmpty();
    }

    /// <summary>A value that was not collected maps to a record with the constant variables and empty results.</summary>
    [Fact]
    public void Map_MissingValue_WritesEmptyResults()
    {
        var specification = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(Systolic));

        var result = _engine.Map(specification, new MappingInput(Systolic, null));

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Problems));
        Value(result, "VSTESTCD").Should().Be("SYSBP");
        Value(result, "VSORRES").Should().BeNull();
        Value(result, "VSSTRESN").Should().BeNull();
    }

    /// <summary>The JSON form reads back as the same specification.</summary>
    [Fact]
    public void Json_RoundTrips()
    {
        var specification = SampleMappingSpecifications.Specification(
            SampleMappingSpecifications.SystolicBloodPressure(Systolic),
            SampleMappingSpecifications.Glucose(Glucose),
            SampleMappingSpecifications.Position(Position)) with
        { Overrides = "base" };

        var json = MappingSpecificationJson.ToJson(specification);
        var (read, problems) = MappingSpecificationJson.Read(json);

        problems.Should().BeEmpty();
        read.Should().BeEquivalentTo(specification, options => options.WithStrictOrdering());
        MappingSpecificationJson.ToJson(read!).GetRawText().Should().Be(json.GetRawText());
    }

    /// <summary>Reading is strict and reports every problem.</summary>
    [Theory]
    [InlineData("""{"title":"t","mappings":[]}""", "between 1 and")]
    [InlineData("""{"title":"t","color":"red","mappings":[{"id":"m","concept":{"id":"0192a6b1-0000-7000-8000-000000000001","version":1},"dataset":"VS","result":{"original":"VSORRES"}}]}""", "unknown property \"color\"")]
    [InlineData("""{"title":"t","mappings":[{"id":"m","concept":{"id":"0192a6b1-0000-7000-8000-000000000001"},"dataset":"VS","result":{"original":"VSORRES"}}]}""", "concept.version")]
    [InlineData("""{"title":"t","mappings":[{"id":"m","concept":{"id":"0192a6b1-0000-7000-8000-000000000001","version":1},"dataset":"vs","result":{"original":"VSORRES"}}]}""", "not a dataset or variable name")]
    [InlineData("""{"title":"t","mappings":[{"id":"a","concept":{"id":"0192a6b1-0000-7000-8000-000000000001","version":1},"dataset":"VS","result":{"original":"VSORRES"}},{"id":"b","concept":{"id":"0192a6b1-0000-7000-8000-000000000001","version":2},"dataset":"VS","result":{"original":"VSORRES"}}]}""", "maps each concept once")]
    [InlineData("""{"title":"t","mappings":[{"id":"m","concept":{"id":"0192a6b1-0000-7000-8000-000000000001","version":1},"dataset":"VS","result":{"original":"VSORRES","standardNumeric":"VSSTRESN"}}]}""", "standardUnit: required")]
    [InlineData("""{"title":"t","units":[{"ucum":"mm[Hg]","submissionValue":"mmHg"}],"mappings":[{"id":"m","concept":{"id":"0192a6b1-0000-7000-8000-000000000001","version":1},"dataset":"VS","result":{"original":"VSORRES","standardNumeric":"VSSTRESN","standardUnit":"VSSTRESU"},"standardUnit":"mm[Hg]","conversions":[{"from":"kPa","to":"cm[H2O]","factor":1}]}]}""", "does not convert into the standard unit")]
    [InlineData("""{"title":"t","mappings":[{"id":"m","concept":{"id":"0192a6b1-0000-7000-8000-000000000001","version":1},"dataset":"VS","result":{"original":"VSORRES","originalUnit":"VSORRESU","standardNumeric":"VSSTRESN","standardUnit":"VSSTRESU"},"standardUnit":"mm[Hg]"}]}""", "which $.units does not list")]
    [InlineData("""{"title":"t","mappings":[{"id":"m","concept":{"id":"0192a6b1-0000-7000-8000-000000000001","version":1},"dataset":"VS","assign":{"VSORRES":"x"},"result":{"original":"VSORRES"}}]}""", "is written 2 times")]
    [InlineData("""{"title":"t","controlledTerminology":[{"codeSystem":"SDTM CT","version":"1"}],"mappings":[{"id":"m","concept":{"id":"0192a6b1-0000-7000-8000-000000000001","version":1},"dataset":"VS","result":{"original":"VSORRES"}}]}""", "names no code system")]
    [InlineData("""{"title":"t","mappings":[{"id":"m","concept":{"id":"0192a6b1-0000-7000-8000-000000000001","version":1},"dataset":"VS","result":{"original":"VSORRES"},"codelist":"78431"}]}""", "NCI C-code")]
    [InlineData("""{"title":"t","mappings":[{"id":"m","concept":{"id":"0192a6b1-0000-7000-8000-000000000001","version":1},"dataset":"VS","result":{"original":"VSPOS","originalUnit":"VSORRESU"},"codelist":"C78431"}]}""", "is not a quantity")]
    public void Json_Read_RefusesInvalidContent(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);

        var (specification, problems) = MappingSpecificationJson.Read(document.RootElement);

        specification.Should().BeNull();
        problems.Should().Contain(p => p.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>The content validator the kernel registers reports the reader's problems.</summary>
    [Fact]
    public void Json_Parse_ThrowsWithTheProblems()
    {
        using var document = JsonDocument.Parse("""{"title":"t"}""");

        var parse = () => MappingSpecificationJson.Parse(document.RootElement);

        parse.Should().Throw<FormatException>().WithMessage("*$.mappings: required*");
    }

    /// <summary>
    /// A study-level override replaces the base mapping for the Concepts it maps and adds the rest; one mapping serves
    /// every item that records the same Concept.
    /// </summary>
    [Fact]
    public void StudyMappings_OverrideReplacesTheBaseMappingForItsConcepts()
    {
        var baseSpec = Pin("sdtm-vitals", SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(Systolic)));
        var overridden = SampleMappingSpecifications.SystolicBloodPressure(Systolic) with { Decimals = 1 };
        var studySpec = Pin("study-001-vitals", SampleMappingSpecifications.Specification(overridden, SampleMappingSpecifications.Glucose(Glucose)) with { Overrides = "sdtm-vitals" });

        var (mappings, problems) = StudyMappings.Resolve([studySpec, baseSpec], [Sdtmig]);

        problems.Should().BeEmpty();
        mappings!.Specifications.Select(s => s.Key).Should().Equal("sdtm-vitals", "study-001-vitals");
        mappings.For(Systolic.ConceptId).Should().ContainSingle().Which.Should().Be(new CoveringMapping(studySpec, overridden));
        mappings.For(Glucose.ConceptId).Should().ContainSingle().Which.Specification.Should().Be(studySpec);
        mappings.For(Position.ConceptId).Should().BeEmpty();
    }

    /// <summary>Pins that cannot be used together are refused with the reason.</summary>
    [Fact]
    public void StudyMappings_RefusesPinsThatCannotWorkTogether()
    {
        var vitals = SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(Systolic));
        var baseSpec = Pin("sdtm-vitals", vitals);
        var otherStandard = StandardVersionId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-0000000000a2"));

        Problems([baseSpec], []).Should().ContainSingle().Which.Should().Contain("does not declare");
        Problems([Pin("study", vitals with { Overrides = "sdtm-vitals" })], [Sdtmig]).Should().ContainSingle().Which.Should().Contain("does not pin");
        Problems([baseSpec, Pin("a", vitals with { Overrides = "sdtm-vitals" }), Pin("b", vitals with { Overrides = "a" })], [Sdtmig])
            .Should().ContainSingle().Which.Should().Contain("itself an override");
        Problems([baseSpec, Pin("study", vitals with { Overrides = "sdtm-vitals" }, otherStandard)], [Sdtmig, otherStandard])
            .Should().ContainSingle().Which.Should().Contain("different standard version");
        Problems([baseSpec, Pin("sdtm-vitals-2", vitals)], [Sdtmig]).Should().ContainSingle().Which.Should().Contain("mapped by both");
        Problems([baseSpec, Pin("a", vitals with { Overrides = "sdtm-vitals" }), Pin("b", vitals with { Overrides = "sdtm-vitals" })], [Sdtmig])
            .Should().ContainSingle().Which.Should().Contain("mapped by both a and b");
        Problems([baseSpec, Pin("sendig-vitals", vitals, otherStandard)], [Sdtmig, otherStandard]).Should().BeEmpty();
        Problems([baseSpec, Pin("sdtm-vitals", vitals, version: 2)], [Sdtmig]).Should().ContainSingle().Which.Should().Contain("pinned at 2 versions");
    }

    /// <summary>An override replaces only its own base's mapping; a Concept another base maps is a clash, not a replacement.</summary>
    [Fact]
    public void StudyMappings_OverrideNeverReplacesAnotherBasesMapping()
    {
        var baseA = Pin("a-labs", SampleMappingSpecifications.Specification(SampleMappingSpecifications.Glucose(Glucose)));
        var baseB = Pin("b-vitals", SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(Systolic)));
        var overrideA = Pin("c-study", SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(Systolic)) with { Overrides = "a-labs" });

        Problems([baseA, baseB, overrideA], [Sdtmig]).Should().ContainSingle().Which.Should().Contain("mapped by both b-vitals and c-study");
    }

    /// <summary>A mapping-gap finding names the Concept, not a version of it, so its acceptance holds across concept versions.</summary>
    [Fact]
    public void MappingGap_TargetsTheConceptWithoutAVersion()
    {
        var target = MappingGap.Target(Systolic.ConceptId);

        target.Problems().Should().BeEmpty();
        target.Kind.Should().Be(FindingTargetKind.DesignElement);
        target.Dataset.Should().Be(MappingGap.Dataset);
        target.Node.Should().Be(Cadence.Kernel.References.NodeRef.For(Systolic.ConceptId));
    }

    private static IReadOnlyList<string> Problems(IReadOnlyList<PinnedMappingSpecification> pins, IReadOnlyCollection<StandardVersionId> standards) =>
        StudyMappings.Resolve(pins, standards).Problems;

    private static PinnedMappingSpecification Pin(string key, MappingSpecification specification, StandardVersionId? standard = null, int version = 1) =>
        new(LibraryItemId.New(), key, version, version, standard ?? Sdtmig, specification);

    private static string? Value(MappingResult result, string variable) => result.Values.Single(v => v.Variable == variable).Value;

    private sealed class Decoder(Dictionary<(string Codelist, string Code), string> terms) : ICodelistDecoder
    {
        public string? SubmissionValue(string codelist, string code) => terms.GetValueOrDefault((codelist, code));
    }
}
