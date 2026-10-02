using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Core.Domain.Derived;

/// <summary>A standard clinical formula <see cref="DerivedVariableEngine"/> computes.</summary>
public enum DerivedFormulaType
{
    /// <summary>Body mass index: weight (kg) / height (m)².</summary>
    Bmi,

    /// <summary>Body surface area (Mosteller): √(height (cm) × weight (kg) / 3600).</summary>
    BsaMosteller,

    /// <summary>Body surface area (DuBois): 0.007184 × height (cm)^0.725 × weight (kg)^0.425.</summary>
    BsaDubois,

    /// <summary>eGFR by the race-free CKD-EPI 2021 creatinine equation.</summary>
    EgfrCkdEpi2021,

    /// <summary>eGFR by the IDMS-traceable MDRD equation.</summary>
    EgfrMdrd,

    /// <summary>QT corrected by Fridericia: QT (ms) / ∛RR (s).</summary>
    QtcFridericia,

    /// <summary>QT corrected by Bazett: QT (ms) / √RR (s).</summary>
    QtcBazett,

    /// <summary>Mean arterial pressure: DBP + (SBP − DBP) / 3.</summary>
    Map,

    /// <summary>Creatinine clearance by Cockcroft-Gault, × 0.85 for a female participant.</summary>
    CockcroftGault,
}

/// <summary>The physiologically plausible bounds of an input.</summary>
/// <param name="MinValue">The lowest plausible value, if bounded below.</param>
/// <param name="MaxValue">The highest plausible value, if bounded above.</param>
/// <param name="Unit">The unit the bounds are in, for the message.</param>
/// <param name="WarningOnly">Whether a value out of bounds only warns instead of failing the calculation.</param>
public sealed record PlausibilityRange(double? MinValue = null, double? MaxValue = null, string? Unit = null, bool WarningOnly = false);

/// <summary>An input a formula declares: checked for presence, number and plausibility before the formula runs.</summary>
/// <param name="Name">The input's name in the calculation context (for example <c>weight</c>).</param>
/// <param name="Unit">The unit the input is expected in.</param>
/// <param name="Required">Whether the calculation fails without it.</param>
/// <param name="DefaultValue">The value used when the context has none.</param>
/// <param name="Plausibility">The bounds the value is checked against.</param>
public sealed record FormulaInputRequirement(
    string Name,
    string? Unit = null,
    bool Required = true,
    double? DefaultValue = null,
    PlausibilityRange? Plausibility = null);

/// <summary>
/// A versioned derived-variable formula. The Python model's <c>reason_for_change</c> belongs to whatever stores a
/// formula, through the GxP audit interceptor, and is not part of this record.
/// </summary>
public sealed record DerivedFormulaDefinition
{
    private readonly int versionIndex = 1;

    /// <summary>The formula's identifier (for example <c>FORMULA-EGFR-2021</c>).</summary>
    public required string FormulaId { get; init; }

    /// <summary>The formula's name.</summary>
    public required string Name { get; init; }

    /// <summary>The standard formula computed.</summary>
    public required DerivedFormulaType FormulaType { get; init; }

    /// <summary>The inputs checked before the formula runs.</summary>
    public IReadOnlyList<FormulaInputRequirement> Inputs { get; init; } = [];

    /// <summary>The name of the derived variable (for example <c>calculated_bmi</c>).</summary>
    public required string OutputVariable { get; init; }

    /// <summary>The unit of the derived value (for example <c>kg/m2</c>).</summary>
    public string? OutputUnit { get; init; }

    /// <summary>The formula's version, from 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The version is below 1.</exception>
    public int VersionIndex
    {
        get => versionIndex;
        init => versionIndex = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(VersionIndex), value, "A formula version starts at 1.");
    }

    /// <summary>Whether the formula is in force. The engine does not check it; the caller chooses the formula.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// SHA-256 (lower-case hex) of the formula's identity, type, output, inputs and version, written as the Python
    /// model wrote it (<c>json.dumps(payload, sort_keys=True)</c>), so it equals the checksum the Python reference
    /// computed. It identifies a formula version in provenance; it is not a signature.
    /// </summary>
    public string ComputeChecksum()
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["formula_id"] = FormulaId,
            ["version_index"] = VersionIndex,
            ["formula_type"] = PythonName(FormulaType),
            ["output_variable"] = OutputVariable,
            ["inputs"] = Inputs.Select(i => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = i.Name,
                ["unit"] = i.Unit,
                ["required"] = i.Required,
                ["default_value"] = i.DefaultValue,
                ["plausibility"] = i.Plausibility is null
                    ? null
                    : new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["min_value"] = i.Plausibility.MinValue,
                        ["max_value"] = i.Plausibility.MaxValue,
                        ["unit"] = i.Plausibility.Unit,
                        ["warning_only"] = i.Plausibility.WarningOnly,
                    },
            }).ToList(),
            ["expression"] = null,
        };
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(PythonJson.Value(payload))));
    }

    /// <summary>The formula type as the Python reference named it (for example <c>EGFR_CKD_EPI_2021</c>).</summary>
    public static string PythonName(DerivedFormulaType type) => type switch
    {
        DerivedFormulaType.Bmi => "BMI",
        DerivedFormulaType.BsaMosteller => "BSA_MOSTELLER",
        DerivedFormulaType.BsaDubois => "BSA_DUBOIS",
        DerivedFormulaType.EgfrCkdEpi2021 => "EGFR_CKD_EPI_2021",
        DerivedFormulaType.EgfrMdrd => "EGFR_MDRD",
        DerivedFormulaType.QtcFridericia => "QTC_FRIDERICIA",
        DerivedFormulaType.QtcBazett => "QTC_BAZETT",
        DerivedFormulaType.Map => "MAP",
        DerivedFormulaType.CockcroftGault => "COCKCROFT_GAULT",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown formula type."),
    };
}

/// <summary>
/// The outcome of one calculation, with what a derived Observation's provenance needs (K-FACT-2): the formula, its
/// version and checksum, and the input values used after unit normalisation.
/// </summary>
/// <param name="FormulaId">The formula computed.</param>
/// <param name="FormulaVersion">The formula's version.</param>
/// <param name="FormulaChecksum">The formula's <see cref="DerivedFormulaDefinition.ComputeChecksum"/>.</param>
/// <param name="OutputVariable">The derived variable's name.</param>
/// <param name="CalculatedValue">The value, rounded to four decimals; none when the calculation failed.</param>
/// <param name="OutputUnit">The value's unit; none when the calculation failed.</param>
/// <param name="IsValid">Whether the calculation succeeded.</param>
/// <param name="ErrorMessage">Why it failed.</param>
/// <param name="PlausibilityViolations">Every out-of-bounds input found before the calculation ended.</param>
/// <param name="InputValuesUsed">The calculation context after unit normalisation and defaults.</param>
/// <param name="EvaluatedAt">The instant the caller evaluated the formula at.</param>
public sealed record DerivedCalculationResult(
    string FormulaId,
    int FormulaVersion,
    string FormulaChecksum,
    string OutputVariable,
    double? CalculatedValue,
    string? OutputUnit,
    bool IsValid,
    string? ErrorMessage,
    IReadOnlyList<string> PlausibilityViolations,
    IReadOnlyDictionary<string, object?> InputValuesUsed,
    DateTimeOffset EvaluatedAt);
