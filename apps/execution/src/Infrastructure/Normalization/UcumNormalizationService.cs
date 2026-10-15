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

    public (double? NormalizedValue, string? NormalizedUnit) GetNormalizedRepresentation(double? value, string? unit)
    {
        if (value is null || string.IsNullOrWhiteSpace(unit))
        {
            return (value, unit);
        }

        string normUnit = NormalizeUnitName(unit);

        // Temperature base: Cel
        if (TemperatureUnits.Contains(normUnit))
        {
            try
            {
                double valCel = ConvertUnit(value.Value, normUnit, "Cel");
                return (valCel, "Cel");
            }
            catch
            {
                return (value, unit);
            }
        }

        // Multiplicative base
        if (Conversions.TryGetValue(normUnit, out var conv))
        {
            return (value.Value * conv.Factor, conv.BaseUnit);
        }

        return (value, unit);
    }

    private static readonly Dictionary<string, Application.Observations.LoincCatalogItemResponse> LoincMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALT"] = new("ALT", "Alanine Aminotransferase", "1742-6", "U/L", "Hepatic Safety", null),
        ["LB_ALT"] = new("ALT", "Alanine Aminotransferase", "1742-6", "U/L", "Hepatic Safety", null),
        ["AST"] = new("AST", "Aspartate Aminotransferase", "1920-8", "U/L", "Hepatic Safety", null),
        ["LB_AST"] = new("AST", "Aspartate Aminotransferase", "1920-8", "U/L", "Hepatic Safety", null),
        ["BILI"] = new("BILI", "Total Bilirubin", "1975-2", "umol/L", "Hepatic Safety", 584.66),
        ["BILIRUBIN"] = new("BILI", "Total Bilirubin", "1975-2", "umol/L", "Hepatic Safety", 584.66),
        ["CREAT"] = new("CREAT", "Serum Creatinine", "2160-0", "umol/L", "Renal Function", 113.12),
        ["CREATININE"] = new("CREAT", "Serum Creatinine", "2160-0", "umol/L", "Renal Function", 113.12),
        ["GLUCOSE"] = new("GLUCOSE", "Fasting Glucose", "2345-7", "mmol/L", "Metabolic Panel", 180.16),
        ["GLUC"] = new("GLUCOSE", "Fasting Glucose", "2345-7", "mmol/L", "Metabolic Panel", 180.16),
        ["HEMOGLOBIN"] = new("HEMOGLOBIN", "Hemoglobin", "718-7", "g/L", "Hematology", 64458.0), // deid-ignore
        ["HGB"] = new("HEMOGLOBIN", "Hemoglobin", "718-7", "g/L", "Hematology", 64458.0), // deid-ignore
        ["PLATELETS"] = new("PLATELETS", "Platelet Count", "777-3", "10^9/L", "Hematology", null),
        ["PLT"] = new("PLATELETS", "Platelet Count", "777-3", "10^9/L", "Hematology", null),
        ["ANC"] = new("ANC", "Absolute Neutrophil Count", "26499-4", "10^9/L", "Hematology", null), // deid-ignore
        ["POTASSIUM"] = new("POTASSIUM", "Serum Potassium", "2823-3", "mmol/L", "Electrolytes", 39.098),
        ["K"] = new("POTASSIUM", "Serum Potassium", "2823-3", "mmol/L", "Electrolytes", 39.098),
        ["SODIUM"] = new("SODIUM", "Serum Sodium", "2951-2", "mmol/L", "Electrolytes", 22.99),
        ["NA"] = new("SODIUM", "Serum Sodium", "2951-2", "mmol/L", "Electrolytes", 22.99),
        ["CALCIUM"] = new("CALCIUM", "Serum Calcium", "17861-6", "mmol/L", "Electrolytes", 40.078), // deid-ignore
        ["CA"] = new("CALCIUM", "Serum Calcium", "17861-6", "mmol/L", "Electrolytes", 40.078), // deid-ignore
        ["CHOLESTEROL"] = new("CHOLESTEROL", "Total Cholesterol", "2093-3", "mmol/L", "Lipid Panel", 386.65),
        ["TRIGLYCERIDES"] = new("TRIGLYCERIDES", "Triglycerides", "2571-8", "mmol/L", "Lipid Panel", 885.7),
        ["WBC"] = new("WBC", "White Blood Cell Count", "6690-2", "10^9/L", "Hematology", null),
        ["SYSBP"] = new("SYSBP", "Systolic Blood Pressure", "8480-6", "mm[Hg]", "Vital Signs", null),
        ["DIABP"] = new("DIABP", "Diastolic Blood Pressure", "8462-4", "mm[Hg]", "Vital Signs", null),
        ["HR"] = new("HR", "Heart Rate", "8867-4", "beats/min", "Vital Signs", null),
        ["PULSE"] = new("HR", "Heart Rate", "8867-4", "beats/min", "Vital Signs", null),
        ["TEMP"] = new("TEMP", "Body Temperature", "8310-5", "Cel", "Vital Signs", null),
    };

    public string? MapLoincCode(string testCode)
    {
        if (string.IsNullOrWhiteSpace(testCode)) return null;
        return LoincMap.TryGetValue(testCode.Trim(), out var item) ? item.LoincCode : null;
    }

    public string GetTargetSiUnit(string testCode, string currentUnit)
    {
        if (!string.IsNullOrWhiteSpace(testCode) && LoincMap.TryGetValue(testCode.Trim(), out var item))
        {
            return item.StandardUnit;
        }

        string norm = NormalizeUnitName(currentUnit);
        if (norm.Equals("mg/dL", StringComparison.OrdinalIgnoreCase)) return "umol/L";
        if (norm.Equals("g/dL", StringComparison.OrdinalIgnoreCase)) return "g/L";
        if (norm.Equals("10^3/uL", StringComparison.OrdinalIgnoreCase) || norm.Equals("10^3/mcL", StringComparison.OrdinalIgnoreCase) || norm.Equals("k/uL", StringComparison.OrdinalIgnoreCase)) return "10^9/L";
        if (norm.Equals("mEq/L", StringComparison.OrdinalIgnoreCase)) return "mmol/L";
        return norm;
    }

    public (double ConvertedValue, double Factor) ConvertLabUnit(double value, string fromUnit, string toUnit, string? testCode = null)
    {
        string normFrom = NormalizeUnitName(fromUnit?.Trim() ?? string.Empty);
        string normTo = NormalizeUnitName(toUnit?.Trim() ?? string.Empty);

        if (string.Equals(normFrom, normTo, StringComparison.OrdinalIgnoreCase))
        {
            return (value, 1.0);
        }

        // Try standard ConvertUnit first
        try
        {
            double converted = ConvertUnit(value, normFrom, normTo);
            double factor = Math.Abs(value) > 1e-9 ? converted / value : 1.0;
            return (converted, factor);
        }
        catch
        {
            // Custom clinical laboratory unit conversions
            string codeUpper = testCode?.Trim().ToUpperInvariant() ?? string.Empty;

            // mg/dL to umol/L or mmol/L
            if (normFrom.Equals("mg/dL", StringComparison.OrdinalIgnoreCase) && normTo.Equals("umol/L", StringComparison.OrdinalIgnoreCase))
            {
                double factor = GetMgDlToUmolFactor(codeUpper);
                return (Math.Round(value * factor, 4), factor);
            }
            if (normFrom.Equals("umol/L", StringComparison.OrdinalIgnoreCase) && normTo.Equals("mg/dL", StringComparison.OrdinalIgnoreCase))
            {
                double factor = 1.0 / GetMgDlToUmolFactor(codeUpper);
                return (Math.Round(value * factor, 4), factor);
            }
            if (normFrom.Equals("mg/dL", StringComparison.OrdinalIgnoreCase) && normTo.Equals("mmol/L", StringComparison.OrdinalIgnoreCase))
            {
                double factor = GetMgDlToMmolFactor(codeUpper);
                return (Math.Round(value * factor, 4), factor);
            }
            if (normFrom.Equals("mmol/L", StringComparison.OrdinalIgnoreCase) && normTo.Equals("mg/dL", StringComparison.OrdinalIgnoreCase))
            {
                double factor = 1.0 / GetMgDlToMmolFactor(codeUpper);
                return (Math.Round(value * factor, 4), factor);
            }

            // g/dL to g/L
            if (normFrom.Equals("g/dL", StringComparison.OrdinalIgnoreCase) && normTo.Equals("g/L", StringComparison.OrdinalIgnoreCase))
            {
                return (Math.Round(value * 10.0, 4), 10.0);
            }
            if (normFrom.Equals("g/L", StringComparison.OrdinalIgnoreCase) && normTo.Equals("g/dL", StringComparison.OrdinalIgnoreCase))
            {
                return (Math.Round(value / 10.0, 4), 0.1);
            }

            // 10^3/uL or k/uL to 10^9/L
            if ((normFrom.Equals("10^3/uL", StringComparison.OrdinalIgnoreCase) || normFrom.Equals("10^3/mcL", StringComparison.OrdinalIgnoreCase) || normFrom.Equals("k/uL", StringComparison.OrdinalIgnoreCase) || normFrom.Equals("thou/uL", StringComparison.OrdinalIgnoreCase))
                && normTo.Equals("10^9/L", StringComparison.OrdinalIgnoreCase))
            {
                return (value, 1.0);
            }

            // mEq/L to mmol/L
            if (normFrom.Equals("mEq/L", StringComparison.OrdinalIgnoreCase) && normTo.Equals("mmol/L", StringComparison.OrdinalIgnoreCase))
            {
                return (value, 1.0);
            }

            // Fallback identity
            return (value, 1.0);
        }
    }

    private static double GetMgDlToUmolFactor(string codeUpper) => codeUpper switch
    {
        "CREAT" or "CREATININE" => 88.4,
        "BILI" or "BILIRUBIN" or "TBIL" => 17.1,
        _ => 88.4
    };

    private static double GetMgDlToMmolFactor(string codeUpper) => codeUpper switch
    {
        "GLUCOSE" or "GLUC" => 0.0555,
        "CHOLESTEROL" => 0.02586, // deid-ignore
        "TRIGLYCERIDES" => 0.01129, // deid-ignore
        "CALCIUM" or "CA" => 0.2495,
        _ => 0.0555
    };

    public IReadOnlyList<Application.Observations.LoincCatalogItemResponse> GetLoincCatalog()
    {
        return LoincMap.Values
            .DistinctBy(x => x.LoincCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

