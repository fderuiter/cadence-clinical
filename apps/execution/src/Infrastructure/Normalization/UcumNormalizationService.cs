using System;
using System.Collections.Frozen;
using System.Collections.Generic;

using Cadence.Apps.Execution.Application.Abstractions;

namespace Cadence.Apps.Execution.Infrastructure.Normalization;

/// <summary>
/// Canonical UCUM unit conversion and normalization engine conforming to PRD-SYS-042.
/// Matches legacy Python <c>ucum.py</c> conversions for temperature, mass, length, and pressure.
/// @req:PRD-SYS-042
/// @req:SRS-EDC-001
/// </summary>
public sealed class UcumNormalizationService : IUcumNormalizationService
{
    private static readonly FrozenDictionary<string, string> UnitAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Temperature
        ["cel"] = "Cel",
        ["c"] = "Cel",
        ["°c"] = "Cel",
        ["celsius"] = "Cel",
        ["degc"] = "Cel",
        ["[degc]"] = "Cel",
        ["fahr"] = "[degF]",
        ["f"] = "[degF]",
        ["°f"] = "[degF]",
        ["fahrenheit"] = "[degF]",
        ["[fahr]"] = "[degF]",
        ["degf"] = "[degF]",
        ["[degf]"] = "[degF]",
        ["k"] = "K",
        ["kelvin"] = "K",
        // Weight / Mass
        ["kg"] = "kg",
        ["kilogram"] = "kg",
        ["kilograms"] = "kg",
        ["lb"] = "[lb_av]",
        ["lbs"] = "[lb_av]",
        ["pound"] = "[lb_av]",
        ["pounds"] = "[lb_av]",
        ["[lb_av]"] = "[lb_av]",
        ["g"] = "g",
        ["gram"] = "g",
        ["grams"] = "g",
        ["mg"] = "mg",
        ["milligram"] = "mg",
        ["milligrams"] = "mg",
        ["[oz_av]"] = "[oz_av]",
        ["oz"] = "[oz_av]",
        // Length
        ["m"] = "m",
        ["meter"] = "m",
        ["meters"] = "m",
        ["cm"] = "cm",
        ["centimeter"] = "cm",
        ["centimeters"] = "cm",
        ["mm"] = "mm",
        ["millimeter"] = "mm",
        ["millimeters"] = "mm",
        ["in"] = "[in_i]",
        ["inch"] = "[in_i]",
        ["inches"] = "[in_i]",
        ["[in_i]"] = "[in_i]",
        ["ft"] = "[ft_i]",
        ["foot"] = "[ft_i]",
        ["feet"] = "[ft_i]",
        ["[ft_i]"] = "[ft_i]",
        // Pressure
        ["mm[hg]"] = "mm[Hg]",
        ["mmhg"] = "mm[Hg]",
        ["mm_hg"] = "mm[Hg]",
        ["kpa"] = "kPa",
        ["kilopascal"] = "kPa",
        ["kilopascals"] = "kPa",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, (string BaseUnit, double Factor)> Conversions = new Dictionary<string, (string, double)>(StringComparer.Ordinal)
    {
        // Mass (Base: kg)
        ["kg"] = ("kg", 1.0),
        ["[lb_av]"] = ("kg", 0.45359237),
        ["g"] = ("kg", 0.001),
        ["mg"] = ("kg", 0.000001),
        ["[oz_av]"] = ("kg", 0.028349523125),
        // Length (Base: m)
        ["m"] = ("m", 1.0),
        ["cm"] = ("m", 0.01),
        ["mm"] = ("m", 0.001),
        ["[in_i]"] = ("m", 0.0254),
        ["[ft_i]"] = ("m", 0.3048),
        // Pressure (Base: mm[Hg])
        ["mm[Hg]"] = ("mm[Hg]", 1.0),
        ["kPa"] = ("mm[Hg]", 7.5006156),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> TemperatureUnits = new[] { "Cel", "[degF]", "[Fahr]", "K" }.ToFrozenSet(StringComparer.Ordinal);

    public string NormalizeUnitName(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return unit;
        string cleaned = unit.Trim();
        return UnitAliases.TryGetValue(cleaned, out var alias) ? alias : unit;
    }

    public double ConvertUnit(double value, string fromUnit, string toUnit)
    {
        string normFrom = NormalizeUnitName(fromUnit);
        string normTo = NormalizeUnitName(toUnit);

        if (string.Equals(normFrom, normTo, StringComparison.Ordinal))
        {
            return value;
        }

        // Temperature conversion
        if (TemperatureUnits.Contains(normFrom) && TemperatureUnits.Contains(normTo))
        {
            double celVal = normFrom switch
            {
                "Cel" => value,
                "[degF]" or "[Fahr]" => (value - 32.0) * 5.0 / 9.0,
                "K" => value - 273.15,
                _ => throw new ArgumentException($"Unsupported temperature unit: '{normFrom}'", nameof(fromUnit))
            };

            return normTo switch
            {
                "Cel" => celVal,
                "[degF]" or "[Fahr]" => (celVal * 9.0 / 5.0) + 32.0,
                "K" => celVal + 273.15,
                _ => throw new ArgumentException($"Unsupported temperature unit: '{normTo}'", nameof(toUnit))
            };
        }

        // Multiplicative conversion
        if (Conversions.TryGetValue(normFrom, out var fromConv) && Conversions.TryGetValue(normTo, out var toConv))
        {
            if (!string.Equals(fromConv.BaseUnit, toConv.BaseUnit, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Incompatible unit conversion from {fromUnit} ({fromConv.BaseUnit}) to {toUnit} ({toConv.BaseUnit})");
            }

            double valueInBase = value * fromConv.Factor;
            return valueInBase / toConv.Factor;
        }

        throw new ArgumentException($"Unrecognized or unsupported conversion from '{fromUnit}' to '{toUnit}'");
    }

    public UcumNormalizationResult GetNormalizedRepresentation(double? value, string? unit)
    {
        if (value is null)
        {
            return new UcumNormalizationResult(
                IsNormalized: true,
                NormalizedValue: null,
                NormalizedUnit: string.IsNullOrWhiteSpace(unit) ? null : NormalizeUnitName(unit),
                StatusCode: "NORMALIZED");
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            return new UcumNormalizationResult(
                IsNormalized: true,
                NormalizedValue: value,
                NormalizedUnit: null,
                StatusCode: "NORMALIZED");
        }

        string normUnit = NormalizeUnitName(unit);

        // Temperature base: Cel
        if (TemperatureUnits.Contains(normUnit))
        {
            try
            {
                double valCel = ConvertUnit(value.Value, normUnit, "Cel");
                return new UcumNormalizationResult(
                    IsNormalized: true,
                    NormalizedValue: valCel,
                    NormalizedUnit: "Cel",
                    StatusCode: "NORMALIZED");
            }
            catch
            {
                return new UcumNormalizationResult(
                    IsNormalized: false,
                    NormalizedValue: null,
                    NormalizedUnit: null,
                    StatusCode: "UNMAPPED");
            }
        }

        // Multiplicative base
        if (Conversions.TryGetValue(normUnit, out var conv))
        {
            return new UcumNormalizationResult(
                IsNormalized: true,
                NormalizedValue: value.Value * conv.Factor,
                NormalizedUnit: conv.BaseUnit,
                StatusCode: "NORMALIZED");
        }

        return new UcumNormalizationResult(
            IsNormalized: false,
            NormalizedValue: null,
            NormalizedUnit: null,
            StatusCode: "UNMAPPED");
    }
}
