using System;
using System.Collections.Generic;

using Cadence.Core.Domain.Derived;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Behavioural tests for the C# port of <c>packages/engine_core/derived/</c> (#5043). Expected values were produced by
/// running the Python reference on the same inputs.
/// </summary>
public class DerivedVariableEngineTests
{
    private static readonly DateTimeOffset At = FakeClock.DefaultStart;

    private static readonly FormulaInputRequirement[] BodyInputs =
    [
        new("weight", "kg", Plausibility: new PlausibilityRange(20.0, 300.0, "kg")),
        new("height", "cm", Plausibility: new PlausibilityRange(100.0, 250.0, "cm", WarningOnly: true)),
    ];

    private static DerivedFormulaDefinition Formula(DerivedFormulaType type, params FormulaInputRequirement[] inputs) => new()
    {
        FormulaId = "F-" + DerivedFormulaDefinition.PythonName(type),
        Name = DerivedFormulaDefinition.PythonName(type),
        FormulaType = type,
        OutputVariable = "out",
        OutputUnit = "unit",
        Inputs = inputs,
    };

    private static DerivedCalculationResult Calculate(DerivedFormulaType type, Dictionary<string, object?> context, params FormulaInputRequirement[] inputs) =>
        DerivedVariableEngine.Calculate(Formula(type, inputs), context, At);

    /// <summary>Each standard formula gives the Python reference's value, rounded to four decimals.</summary>
    [Theory]
    [MemberData(nameof(StandardFormulas))]
    public void Calculate_StandardFormulas_MatchPythonReference(DerivedFormulaType type, Dictionary<string, object?> context, double expected)
    {
        var result = Calculate(type, context);

        result.IsValid.Should().BeTrue(result.ErrorMessage);
        result.CalculatedValue.Should().Be(expected);
        result.OutputUnit.Should().Be("unit");
        result.EvaluatedAt.Should().Be(At);
    }

    /// <summary>Formula inputs and expected values from the Python reference.</summary>
    public static TheoryData<DerivedFormulaType, Dictionary<string, object?>, double> StandardFormulas() => new()
    {
        { DerivedFormulaType.Bmi, new() { ["weight"] = 70, ["height"] = 175 }, 22.8571 },
        { DerivedFormulaType.Bmi, new() { ["weight_lbs"] = 154, ["height_in"] = 69 }, 22.7416 },
        { DerivedFormulaType.BsaMosteller, new() { ["weight"] = 70, ["height"] = 175 }, 1.8447 },
        { DerivedFormulaType.BsaDubois, new() { ["weight"] = 70, ["height"] = 175 }, 1.8481 },
        { DerivedFormulaType.EgfrCkdEpi2021, new() { ["scr"] = 0.8, ["age"] = 50, ["sex"] = "F" }, 89.7074 },
        { DerivedFormulaType.EgfrCkdEpi2021, new() { ["scr"] = 1.2, ["age"] = 65, ["sex"] = "male" }, 67.1114 },
        { DerivedFormulaType.EgfrCkdEpi2021, new() { ["serum_creatinine_umol"] = 61.9, ["age"] = 40, ["gender"] = "M" }, 119.453 },
        { DerivedFormulaType.EgfrMdrd, new() { ["creatinine"] = 1.1, ["age"] = 70, ["sex"] = "female" }, 49.1043 },
        { DerivedFormulaType.EgfrMdrd, new() { ["serum_creatinine"] = 1.1, ["age"] = 70, ["sex"] = "M" }, 66.1784 },
        { DerivedFormulaType.QtcFridericia, new() { ["qt_ms"] = 400, ["rr_s"] = 0.8 }, 430.8869 },
        { DerivedFormulaType.QtcFridericia, new() { ["qt"] = 400, ["heart_rate"] = 75 }, 430.8869 },
        { DerivedFormulaType.QtcBazett, new() { ["qt"] = 400, ["heart_rate"] = 75 }, 447.2136 },
        { DerivedFormulaType.QtcBazett, new() { ["qt_ms"] = 380, ["rr"] = 1.1 }, 362.3158 },
        { DerivedFormulaType.Map, new() { ["systolic_bp"] = 120, ["diastolic_bp"] = 80 }, 93.3333 },
        { DerivedFormulaType.Map, new() { ["sbp"] = 130, ["dbp"] = 85 }, 100.0 },
        { DerivedFormulaType.CockcroftGault, new() { ["age"] = 60, ["weight"] = 70, ["scr"] = 1.0, ["sex"] = "F" }, 66.1111 },
        { DerivedFormulaType.CockcroftGault, new() { ["age"] = 60, ["weight_lbs"] = 200, ["serum_creatinine_umol"] = 97, ["sex"] = "M" }, 91.8823 },
    };

    /// <summary>Imperial and micromolar inputs gain their SI equivalents, which the result reports as used.</summary>
    [Fact]
    public void Calculate_NormalisesUnits_AndReportsThem()
    {
        var result = Calculate(DerivedFormulaType.CockcroftGault, new() { ["age"] = 60, ["weight_lbs"] = 200, ["serum_creatinine_umol"] = 97, ["sex"] = "M" });

        result.InputValuesUsed.Should().Contain("weight_kg", 90.718474).And.Contain("weight", 90.718474);
        result.InputValuesUsed.Should().Contain("serum_creatinine", 1.0970368694865416).And.Contain("scr", 1.0970368694865416);
    }

    /// <summary>Formula errors come back as invalid results with the Python reference's messages.</summary>
    [Theory]
    [MemberData(nameof(FormulaErrors))]
    public void Calculate_FormulaErrors_AreInvalidResults(DerivedFormulaType type, Dictionary<string, object?> context, string message)
    {
        var result = Calculate(type, context);

        result.IsValid.Should().BeFalse();
        result.CalculatedValue.Should().BeNull();
        result.OutputUnit.Should().BeNull();
        result.ErrorMessage.Should().Be(message);
    }

    /// <summary>Formula inputs and error messages from the Python reference.</summary>
    public static TheoryData<DerivedFormulaType, Dictionary<string, object?>, string> FormulaErrors() => new()
    {
        { DerivedFormulaType.Bmi, new() { ["weight"] = 70 }, "Calculation error: 'height'" },
        { DerivedFormulaType.Bmi, new() { ["weight"] = 70, ["height"] = 0 }, "Calculation error: Height must be greater than zero" },
        { DerivedFormulaType.QtcFridericia, new() { ["qt"] = 400, ["hr"] = 0 }, "Calculation error: Heart rate must be greater than zero" },
        { DerivedFormulaType.QtcFridericia, new() { ["qt"] = 400 }, "Calculation error: Either 'rr_s' or 'heart_rate' is required for QTcF calculation" },
        { DerivedFormulaType.CockcroftGault, new() { ["age"] = 60, ["weight"] = 70, ["creatinine"] = 0, ["sex"] = "M" }, "Calculation error: Serum creatinine must be greater than zero" },
    };

    /// <summary>A required input that is missing fails the calculation before the formula runs.</summary>
    [Fact]
    public void Calculate_MissingRequiredInput_Fails()
    {
        var result = Calculate(DerivedFormulaType.Bmi, new() { ["height"] = 175 }, BodyInputs);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Missing required input variable 'weight'");
    }

    /// <summary>A declared input that is not a number fails; numeric text is accepted, padded or not.</summary>
    [Fact]
    public void Calculate_DeclaredInputs_MustBeNumeric()
    {
        var sbp = new FormulaInputRequirement("sbp");

        Calculate(DerivedFormulaType.Map, new() { ["sbp"] = "abc", ["dbp"] = 80 }, sbp).ErrorMessage
            .Should().Be("Input variable 'sbp' must be numeric (got abc)");
        Calculate(DerivedFormulaType.Bmi, new() { ["weight"] = " 70.5 ", ["height"] = 175 }, BodyInputs).CalculatedValue
            .Should().Be(23.0204);
    }

    /// <summary>A value out of plausible bounds fails, with the violation listed; a warning-only bound only warns.</summary>
    [Fact]
    public void Calculate_PlausibilityBounds()
    {
        var low = Calculate(DerivedFormulaType.Bmi, new() { ["weight"] = 12, ["height"] = 175 }, BodyInputs);
        low.IsValid.Should().BeFalse();
        low.ErrorMessage.Should().Be("Input 'weight' value 12.0 is below plausible minimum 20.0 kg");
        low.PlausibilityViolations.Should().Equal(low.ErrorMessage);

        var high = Calculate(DerivedFormulaType.Bmi, new() { ["weight"] = 301.5, ["height"] = 175 }, BodyInputs);
        high.ErrorMessage.Should().Be("Input 'weight' value 301.5 exceeds plausible maximum 300.0 kg");

        var warned = Calculate(DerivedFormulaType.Bmi, new() { ["weight"] = 70, ["height"] = 260 }, BodyInputs);
        warned.IsValid.Should().BeTrue();
        warned.CalculatedValue.Should().Be(10.355);
        warned.PlausibilityViolations.Should().Equal("Input 'height' value 260.0 exceeds plausible maximum 250.0 cm");
    }

    /// <summary>
    /// An input's default is used by the formula. The Python checked the default but left it out of the formula's
    /// context, so the calculation failed; the expected value is the Python's with the default passed explicitly.
    /// </summary>
    [Fact]
    public void Calculate_AppliesInputDefaults()
    {
        var result = Calculate(
            DerivedFormulaType.EgfrCkdEpi2021,
            new() { ["scr"] = 0.9, ["sex"] = "M" },
            new FormulaInputRequirement("age", DefaultValue: 45.0),
            new FormulaInputRequirement("scr", Plausibility: new PlausibilityRange(MaxValue: 0.5, WarningOnly: true)));

        result.IsValid.Should().BeTrue(result.ErrorMessage);
        result.CalculatedValue.Should().Be(107.3354);
        result.InputValuesUsed.Should().Contain("age", 45.0);
        result.PlausibilityViolations.Should().Equal("Input 'scr' value 0.9 exceeds plausible maximum 0.5");
    }

    /// <summary>An optional input that is missing is skipped.</summary>
    [Fact]
    public void Calculate_SkipsMissingOptionalInput()
    {
        Calculate(DerivedFormulaType.Map, new() { ["sbp"] = 130, ["dbp"] = 85 }, new FormulaInputRequirement("pulse", Required: false))
            .CalculatedValue.Should().Be(100.0);
    }

    /// <summary>
    /// Sex must be given and must be F or M. The Python read a missing sex as female and any other value as male.
    /// </summary>
    [Theory]
    [InlineData(null, "Calculation error: 'sex' or 'gender' is required")]
    [InlineData("U", "Calculation error: Sex must be F, FEMALE, M or MALE (got U)")]
    public void Calculate_RequiresAKnownSex(string? sex, string message)
    {
        var result = Calculate(DerivedFormulaType.EgfrCkdEpi2021, new() { ["scr"] = 0.8, ["age"] = 50, ["sex"] = sex });

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be(message);
    }

    /// <summary>
    /// A non-numeric weight or height fails as an invalid result (the Python raised), true and false are not numbers
    /// (the Python read them as 1 and 0), and a non-finite result fails (the Python raised or returned NaN).
    /// </summary>
    [Fact]
    public void Calculate_RejectsWhatThePythonCrashedOn()
    {
        Calculate(DerivedFormulaType.Bmi, new() { ["weight"] = "abc", ["height"] = 175 }).ErrorMessage
            .Should().Be("Input variable 'weight' must be numeric (got abc)");
        Calculate(DerivedFormulaType.Map, new() { ["sbp"] = true, ["dbp"] = 80 }).ErrorMessage
            .Should().Be("Calculation error: 'sbp' must be numeric (got True)");
        Calculate(DerivedFormulaType.EgfrMdrd, new() { ["scr"] = 1.1, ["age"] = 0, ["sex"] = "M" }).ErrorMessage
            .Should().Be("Calculation error: the result is not a finite number");
    }

    /// <summary>The checksum equals the one the Python model computed for the same definition.</summary>
    [Fact]
    public void ComputeChecksum_MatchesPythonReference()
    {
        (Formula(DerivedFormulaType.Bmi, BodyInputs) with { OutputUnit = "kg/m2", VersionIndex = 2 }).ComputeChecksum()
            .Should().Be("1a3cebd76723bdc3e7590157beef273117c5f49bc69657a5f955b0a7b8b08162"); // pragma: allowlist secret
        Formula(DerivedFormulaType.Map).ComputeChecksum()
            .Should().Be("aa621c68a2a47177a6031deec7ab680e4cf6f794dac469865e19a1efa2827e8c"); // pragma: allowlist secret
        new DerivedFormulaDefinition
        {
            FormulaId = "F-Ü",
            Name = "n",
            FormulaType = DerivedFormulaType.QtcBazett,
            OutputVariable = "qtc",
            Inputs = [new FormulaInputRequirement("qt", Required: false, DefaultValue: 1e-05)],
        }.ComputeChecksum().Should().Be("a463d90bcaa42306e00aa74001f94ddad49ab521e3b175371b93583d9e25522f"); // pragma: allowlist secret
    }

    /// <summary>The result names the formula version and checksum, for a derived Observation's provenance.</summary>
    [Fact]
    public void Calculate_ResultCarriesFormulaProvenance()
    {
        var formula = Formula(DerivedFormulaType.Map) with { VersionIndex = 3 };

        var result = DerivedVariableEngine.Calculate(formula, new Dictionary<string, object?> { ["sbp"] = 120, ["dbp"] = 80 }, At);

        result.FormulaId.Should().Be("F-MAP");
        result.FormulaVersion.Should().Be(3);
        result.FormulaChecksum.Should().Be(formula.ComputeChecksum());
    }

    /// <summary>A formula version starts at 1, as the Python model required.</summary>
    [Fact]
    public void VersionIndex_BelowOne_IsRejected()
    {
        var act = () => Formula(DerivedFormulaType.Map) with { VersionIndex = 0 };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>Numeric zero values (0, 0.0, "0") are accepted as valid present inputs for primary alias keys.</summary>
    [Theory]
    [InlineData(0, 0, 0.0)]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData("0", "0", 0.0)]
    public void Calculate_NumericZeroValues_AreTreatedAsPresentInputs(object sbp, object dbp, double expected)
    {
        var result = Calculate(DerivedFormulaType.Map, new() { ["systolic_bp"] = sbp, ["diastolic_bp"] = dbp });

        result.IsValid.Should().BeTrue(result.ErrorMessage);
        result.CalculatedValue.Should().Be(expected);
    }

    /// <summary>Zero value in primary alias overrides secondary fallback keys instead of being skipped as falsy.</summary>
    [Fact]
    public void Calculate_PrimaryAliasZeroValue_OverridesSecondaryAlias()
    {
        // weight_kg is 0 while weight (fallback) is 70; weight_kg must be used yielding BMI = 0
        var result = Calculate(DerivedFormulaType.Bmi, new() { ["weight_kg"] = 0, ["weight"] = 70, ["height"] = 100 });

        result.IsValid.Should().BeTrue(result.ErrorMessage);
        result.CalculatedValue.Should().Be(0.0);
    }

    /// <summary>Missing, null, or whitespace-only primary alias keys correctly fall through to secondary keys.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Calculate_MissingOrWhitespacePrimaryKeys_FallThroughToSecondaryKeys(string? primarySbp)
    {
        var result = Calculate(DerivedFormulaType.Map, new() { ["systolic_bp"] = primarySbp, ["sbp"] = 120, ["diastolic_bp"] = 80 });

        result.IsValid.Should().BeTrue(result.ErrorMessage);
        result.CalculatedValue.Should().Be(93.3333);
    }
}
