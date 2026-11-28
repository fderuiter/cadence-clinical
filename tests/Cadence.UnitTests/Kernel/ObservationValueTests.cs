using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Standards.Mapping;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Standards;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The typed observation value (spec §8.1, K-FACT-2, K-FACT-3, ADR-2623): its JSON shape, what makes a value invalid,
/// how a collection item admits a value, and how a value maps to SDTM.
/// </summary>
public sealed class ObservationValueTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };
    private static readonly CodeSystemVersionId CdiscCt = CodeSystemVersionId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-0000000000c1"));
    private static readonly ConceptRef Systolic = new(ConceptId.FromGuid(Guid.Parse("0192a6b1-0000-7000-8000-000000000001")), 1);

    /// <summary>Every variant, valid, for round trips.</summary>
    public static IEnumerable<object[]> Variants() =>
    [
        [new QuantityValue(120m, "mm[Hg]")],
        [new QuantityValue(1.50m, "mg/dL")],
        [new QuantityValue(0.5m, "mg/dL", QuantityComparator.LessThan)],
        [new CodedValue(CdiscCt, "C49488")],
        [new TextValue("Headache, mild")],
        [new BooleanValue(true)],
        [new DateTimeValue("2026-03")],
        [new DateTimeValue("2026-03-14T09:30:15+01:00")],
        [new MissingValue(NullFlavor.NotDone, "Patient refused")],
        [new MissingValue(NullFlavor.Unknown)],
    ];

    /// <summary>Each variant writes a <c>type</c> member and reads back equal, whatever order its members arrive in (jsonb reorders them).</summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void Json_RoundTripsEveryVariant_EvenWithTheTypeMemberLast(ObservationValue value)
    {
        var json = JsonSerializer.Serialize(value, Json);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        element.GetProperty("type").GetString().Should().NotBeNullOrEmpty();

        var reordered = "{" + string.Join(",", element.EnumerateObject().OrderByDescending(p => p.Name == "type" ? 0 : 1).Select(p => $"\"{p.Name}\":{p.Value.GetRawText()}")) + "}";
        JsonSerializer.Deserialize<ObservationValue>(json, Json).Should().Be(value);
        JsonSerializer.Deserialize<ObservationValue>(reordered, Json).Should().Be(value);
        value.Problem().Should().BeNull();
    }

    /// <summary>The wire shape is the one the API documents: the discriminator, the members by name, enums by name.</summary>
    [Fact]
    public void Json_UsesTheDocumentedShape()
    {
        JsonSerializer.Serialize<ObservationValue>(new QuantityValue(0.5m, "mg/dL", QuantityComparator.LessOrEqual), Json)
            .Should().Be("""{"type":"quantity","value":0.5,"unit":"mg/dL","comparator":"\u003C="}""", "the default encoder escapes < as \\u003C, which reads back as <");
        JsonSerializer.Serialize<ObservationValue>(new MissingValue(NullFlavor.NotApplicable), Json)
            .Should().Be("""{"type":"missing","reason":"NotApplicable","detail":null}""");
        JsonSerializer.Serialize<ObservationValue>(new DateTimeValue("2026"), Json)
            .Should().Be("""{"type":"dateTime","value":"2026","precision":"Year"}""");
    }

    /// <summary>A quantity keeps the precision it was entered with: 1.50 is not stored as 1.5 (K-FACT-2, the value as entered).</summary>
    [Fact]
    public void Quantity_KeepsTheEnteredPrecision()
    {
        var json = JsonSerializer.Serialize<ObservationValue>(new QuantityValue(1.50m, "mg/dL"), Json);

        json.Should().Contain("\"value\":1.50");
        ((QuantityValue)JsonSerializer.Deserialize<ObservationValue>(json, Json)!).ToDisplayString().Should().Be("1.50 mg/dL");
    }

    /// <summary>A date is valid at each ISO 8601 precision and knows its precision; a partial date is never padded.</summary>
    [Theory]
    [InlineData("2026", DateTimePrecision.Year)]
    [InlineData("2026-03", DateTimePrecision.Month)]
    [InlineData("2024-02-29", DateTimePrecision.Day)]
    [InlineData("2026-03-14T09:30", DateTimePrecision.Minute)]
    [InlineData("2026-03-14T09:30:15Z", DateTimePrecision.Second)]
    [InlineData("2026-03-14T23:59-05:00", DateTimePrecision.Minute)]
    public void DateTime_IsValidAtEachPrecision(string text, DateTimePrecision precision)
    {
        var value = new DateTimeValue(text);

        value.Problem().Should().BeNull();
        value.Precision.Should().Be(precision);
        value.ToDisplayString().Should().Be(text);
    }

    /// <summary>Text that is not an ISO 8601 date or time at a supported precision, or a day that does not exist, is invalid.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("03/14/2026")]
    [InlineData("2026-3")]
    [InlineData("2026-13")]
    [InlineData("2025-02-29")]
    [InlineData("2026-03-14T24:00")]
    [InlineData("2026-03-14T09")]
    [InlineData("2026-03-14 09:30")]
    [InlineData("UN-UNK-2026")]
    public void DateTime_RejectsWhatIsNotIso8601(string text)
    {
        var value = new DateTimeValue(text);

        value.Precision.Should().BeNull();
        value.Problem().Should().Contain("ISO 8601");
    }

    /// <summary>Each variant refuses what it cannot hold, so a command refuses it with a 422 instead of storing it.</summary>
    [Fact]
    public void Problem_NamesWhatIsInvalid()
    {
        ObservationValue.ProblemOf(null).Should().Contain("required");
        new QuantityValue(120m, "").Problem().Should().Contain("UCUM unit");
        new QuantityValue(120m, "mm Hg").Problem().Should().Contain("without spaces");
        new QuantityValue(120m, "mm[Hg").Problem().Should().Contain("balance");
        new QuantityValue(120m, "mg/{a[b]}").Problem().Should().Contain("cannot contain brackets");
        new QuantityValue(120m, new string('m', ObservationValue.MaxUnitLength + 1)).Problem().Should().Contain("at most");
        new QuantityValue(120m, "mm[Hg]", (QuantityComparator)9).Problem().Should().Contain("comparator");
        new CodedValue(default, "C49488").Problem().Should().Contain("code-system version");
        new CodedValue(CdiscCt, " ").Problem().Should().Contain("code");
        new TextValue(" ").Problem().Should().Contain("required");
        new TextValue(new string('a', ObservationValue.MaxTextLength + 1)).Problem().Should().Contain("at most");
        new MissingValue((NullFlavor)42).Problem().Should().Contain("not a reason");
        new MissingValue(NullFlavor.NotDone, " ").Problem().Should().Contain("detail");
        new QuantityValue(1m, "1").Problem().Should().BeNull("a dimensionless quantity has unit 1");
        new QuantityValue(37.2m, "Cel").Problem().Should().BeNull();
        new QuantityValue(10m, "10*3/uL").Problem().Should().BeNull();
    }

    /// <summary>
    /// A collection item admits a value of its kind, in its unit and from its code-system version; a reasoned missing
    /// value fits every item (K-FACT-3); an item with neither unit, terminology nor kind takes any valid value.
    /// </summary>
    [Fact]
    [KernelRule("K-FACT-3")]
    public void CollectionItem_AdmitsOnlyValuesItCanHold()
    {
        var sysbp = new CollectionItem("sysbp", Systolic, Unit: "mm[Hg]");
        var position = new CollectionItem("position", Systolic, Terminology: new TerminologyBinding(CdiscCt, "C71148"));
        var onset = new CollectionItem("onset", Systolic, ValueKind: ObservationValueKind.DateTime);
        var comment = new CollectionItem("comment", Systolic);

        sysbp.ExpectedKind.Should().Be(ObservationValueKind.Quantity);
        position.ExpectedKind.Should().Be(ObservationValueKind.Coded);
        comment.ExpectedKind.Should().BeNull();

        sysbp.Admits(new QuantityValue(120m, "mm[Hg]")).Should().BeNull();
        sysbp.Admits(new QuantityValue(16m, "kPa")).Should().Contain("collected in mm[Hg]");
        sysbp.Admits(new TextValue("120")).Should().Contain("takes a Quantity value");
        sysbp.Admits(new MissingValue(NullFlavor.NotDone, "Cuff unavailable")).Should().BeNull();
        sysbp.Admits(new MissingValue(NullFlavor.Unknown)).Should().BeNull();
        sysbp.Admits(new MissingValue(NullFlavor.AskedButUnknown)).Should().BeNull();
        sysbp.Admits(new MissingValue(NullFlavor.NotApplicable)).Should().BeNull();
        sysbp.Admits(new MissingValue(NullFlavor.Masked)).Should().Contain("Masked");
        sysbp.Admits(new MissingValue(NullFlavor.NotAsked)).Should().Contain("NotAsked");
        ObservationValue.CallerProblemOf(new MissingValue(NullFlavor.Masked)).Should().Contain("Masked");
        ObservationValue.CallerProblemOf(new MissingValue(NullFlavor.NotAsked)).Should().Contain("NotAsked");
        ObservationValue.ProblemOf(new MissingValue(NullFlavor.NotAsked)).Should().BeNull("internal unasked placeholders remain valid kernel facts");
        ObservationValue.ProblemOf(new MissingValue(NullFlavor.Masked)).Should().BeNull("blinded read projections remain valid wire values");
        ObservationValue.IsCallerAdmissibleNullFlavor(NullFlavor.NotDone).Should().BeTrue();
        ObservationValue.IsCallerAdmissibleNullFlavor(NullFlavor.Masked).Should().BeFalse();
        ObservationValue.IsCallerAdmissibleNullFlavor(NullFlavor.NotAsked).Should().BeFalse();
        sysbp.Admits(new QuantityValue(120m, "mm Hg")).Should().Contain("not a UCUM unit");
        sysbp.Admits(null).Should().Contain("required");

        position.Admits(new CodedValue(CdiscCt, "C62166")).Should().BeNull();
        position.Admits(new CodedValue(CodeSystemVersionId.New(), "C62166")).Should().Contain("code-system version");

        onset.Admits(new DateTimeValue("2026-03")).Should().BeNull();
        onset.Admits(new TextValue("March 2026")).Should().Contain("takes a DateTime value");

        comment.Admits(new BooleanValue(false)).Should().BeNull();
    }

    /// <summary>
    /// A typed value maps to SDTM through <see cref="MappingInput.From"/>: a quantity's amount and unit, a bound with its
    /// comparator, and a missing value as <c>--STAT = NOT DONE</c> with its reason in <c>--REASND</c>.
    /// </summary>
    [Fact]
    [KernelRule("K-FACT-3")]
    public void Mapping_WritesQuantitiesBoundsAndMissingValues()
    {
        var engine = new MappingEngine();
        var sysbp = SampleMappingSpecifications.SystolicBloodPressure(Systolic) with
        {
            Result = new ResultVariables("VSORRES", "VSORRESU", "VSSTRESC", "VSSTRESN", "VSSTRESU", "VSSTAT", "VSREASND"),
        };
        var specification = SampleMappingSpecifications.Specification(sysbp);

        var measured = engine.Map(specification, MappingInput.From(Systolic, new QuantityValue(120m, "mm[Hg]")));
        measured.Succeeded.Should().BeTrue(string.Join(" ", measured.Problems));
        Values(measured).Should().Contain(new Dictionary<string, string?>
        {
            ["VSORRES"] = "120",
            ["VSORRESU"] = "mmHg",
            ["VSSTRESN"] = "120",
            ["VSSTAT"] = null,
            ["VSREASND"] = null,
        });

        var bound = engine.Map(specification, MappingInput.From(Systolic, new QuantityValue(60m, "mm[Hg]", QuantityComparator.LessThan)));
        Values(bound)["VSORRES"].Should().Be("<60");

        var notDone = engine.Map(specification, MappingInput.From(Systolic, new MissingValue(NullFlavor.NotDone, "Cuff unavailable")));
        notDone.Succeeded.Should().BeTrue(string.Join(" ", notDone.Problems));
        Values(notDone).Should().Contain(new Dictionary<string, string?>
        {
            ["VSTESTCD"] = "SYSBP",
            ["VSORRES"] = null,
            ["VSORRESU"] = null,
            ["VSSTRESN"] = null,
            ["VSSTAT"] = "NOT DONE",
            ["VSREASND"] = "Cuff unavailable",
        });

        Values(engine.Map(specification, MappingInput.From(Systolic, new MissingValue(NullFlavor.NotDone))))["VSREASND"].Should().BeNull();
        Values(engine.Map(specification, MappingInput.From(Systolic, new MissingValue(NullFlavor.Unknown))))["VSREASND"].Should().Be("UNKNOWN");

        var noStatus = engine.Map(SampleMappingSpecifications.Specification(SampleMappingSpecifications.SystolicBloodPressure(Systolic)),
            MappingInput.From(Systolic, new MissingValue(NullFlavor.NotDone)));
        noStatus.Succeeded.Should().BeFalse();
        noStatus.Problems.Should().ContainSingle().Which.Should().Contain("no status variable");
    }

    /// <summary>Dates, codes, booleans and text map as their text; a coded value gives its code to the codelist decode.</summary>
    [Fact]
    public void MappingInput_FromEachVariant()
    {
        MappingInput.From(Systolic, new DateTimeValue("2026-03")).Should().Be(new MappingInput(Systolic, "2026-03"));
        MappingInput.From(Systolic, new CodedValue(CdiscCt, "C62166")).Should().Be(new MappingInput(Systolic, "C62166"));
        MappingInput.From(Systolic, new BooleanValue(true)).Should().Be(new MappingInput(Systolic, "true"));
        MappingInput.From(Systolic, new TextValue("left arm")).Should().Be(new MappingInput(Systolic, "left arm"));
        MappingInput.From(Systolic, new QuantityValue(1.50m, "mg/dL", QuantityComparator.GreaterOrEqual))
            .Should().Be(new MappingInput(Systolic, ">=1.50", "mg/dL"));
    }

    private static Dictionary<string, string?> Values(MappingResult result) =>
        result.Values.ToDictionary(v => v.Variable, v => v.Value, StringComparer.Ordinal);
}
