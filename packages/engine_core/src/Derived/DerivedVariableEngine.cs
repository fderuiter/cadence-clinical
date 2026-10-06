using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cadence.Core.Domain.Derived;

/// <summary>
/// Computes standard clinical derived variables (BMI, BSA, eGFR, QTc, MAP, creatinine clearance) with unit
/// normalisation and plausibility gating. Port of <c>packages/engine_core/derived/engine.py</c> (#5043). It has no I/O
/// and no clock: the caller passes the instant it evaluates at (K-TIME-3). Custom formulas are written in the one rule
/// language, <c>Cadence.Rules</c> (K-RULE-3), not here.
/// </summary>
public static class DerivedVariableEngine
{
    private const double PoundsToKilograms = 0.45359237;
    private const double InchesToCentimetres = 2.54;
    private const double CreatinineMicromolesPerMilligramPerDecilitre = 88.42;

    /// <summary>
    /// Calculates <paramref name="formula"/> over <paramref name="context"/>: normalises units, applies input defaults,
    /// checks each declared input, then runs the formula. A failure is returned as an invalid result, never thrown.
    /// </summary>
    /// <param name="formula">The formula to calculate.</param>
    /// <param name="context">Input values by name: numbers, numeric strings, and text for <c>sex</c> or <c>gender</c>.</param>
    /// <param name="evaluatedAt">The instant the caller evaluates at, from its <c>IClock</c>.</param>
    public static DerivedCalculationResult Calculate(
        DerivedFormulaDefinition formula,
        IReadOnlyDictionary<string, object?> context,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(context);

        var violations = new List<string>();
        var (ctx, normalisationError) = NormalizeInputUnits(context);

        DerivedCalculationResult Failed(string message) => new(
            formula.FormulaId,
            formula.VersionIndex,
            formula.ComputeChecksum(),
            formula.OutputVariable,
            null,
            null,
            false,
            message,
            violations.ToArray(),
            ctx,
            evaluatedAt);

        if (normalisationError is not null) return Failed(normalisationError);

        foreach (var requirement in formula.Inputs)
        {
            object? value = ctx.GetValueOrDefault(requirement.Name);
            if (value is null && requirement.DefaultValue is { } defaultValue)
            {
                // The Python checked the default but left it out of the context the formula read.
                value = defaultValue;
                ctx[requirement.Name] = defaultValue;
            }

            if (value is null)
            {
                if (requirement.Required) return Failed($"Missing required input variable '{requirement.Name}'");
                continue;
            }

            if (!TryToDouble(value, out double number))
            {
                return Failed(NotNumeric(requirement.Name, value));
            }

            if (requirement.Plausibility is { } range && CheckPlausibility(requirement.Name, number, range, violations) is { } failure)
            {
                return Failed(failure);
            }
        }

        double calculated;
        try
        {
            calculated = Compute(formula.FormulaType, ctx);
        }
        // Every formula failure is thrown as InvalidOperationException; nothing else in Compute throws one.
        catch (InvalidOperationException error)
        {
            return Failed($"Calculation error: {error.Message}");
        }

        if (!double.IsFinite(calculated))
        {
            return Failed("Calculation error: the result is not a finite number");
        }

        return new DerivedCalculationResult(
            formula.FormulaId,
            formula.VersionIndex,
            formula.ComputeChecksum(),
            formula.OutputVariable,
            Math.Round(calculated, 4, MidpointRounding.ToEven),
            formula.OutputUnit,
            true,
            null,
            violations.ToArray(),
            ctx,
            evaluatedAt);
    }

    /// <summary>
    /// Adds SI equivalents to a context: <c>weight_kg</c> from <c>weight_lbs</c> or <c>weight</c>, <c>height_cm</c>
    /// from <c>height_in</c> or <c>height</c>, and <c>serum_creatinine</c> (mg/dL) and <c>scr</c> from
    /// <c>serum_creatinine_umol</c> or <c>serum_creatinine</c>. Returns an error for a non-numeric metric value.
    /// </summary>
    public static (Dictionary<string, object?> Normalized, string? Error) NormalizeInputUnits(IReadOnlyDictionary<string, object?> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var normalized = new Dictionary<string, object?>(inputs, StringComparer.Ordinal);

        string? error =
            Normalize(normalized, "weight_lbs", "weight", "weight_kg", pounds => pounds * PoundsToKilograms)
            ?? Normalize(normalized, "height_in", "height", "height_cm", inches => inches * InchesToCentimetres)
            ?? Normalize(normalized, "serum_creatinine_umol", "serum_creatinine", "scr", micromoles => micromoles / CreatinineMicromolesPerMilligramPerDecilitre);
        return (normalized, error);
    }

    // Converts the non-SI source into both names when neither is given, or else copies the plain name into the other.
    // The Python skipped a non-numeric source value but crashed on a non-numeric plain one; the port reports it.
    private static string? Normalize(Dictionary<string, object?> ctx, string source, string plain, string other, Func<double, double> convert)
    {
        // A weight or height source also gives way to an SI value; the creatinine source gives way only to mg/dL.
        bool creatinine = source == "serum_creatinine_umol";
        bool sourceApplies = ctx.ContainsKey(source) && !ctx.ContainsKey(plain) && (creatinine || !ctx.ContainsKey(other));
        if (sourceApplies)
        {
            if (TryToDouble(ctx[source], out double value))
            {
                ctx[creatinine ? plain : other] = convert(value);
                ctx[creatinine ? other : plain] = convert(value);
            }
        }
        else if (ctx.TryGetValue(plain, out object? raw) && !ctx.ContainsKey(other))
        {
            if (!TryToDouble(raw, out double value)) return NotNumeric(plain, raw);
            ctx[other] = value;
        }

        return null;
    }

    private static string? CheckPlausibility(string name, double value, PlausibilityRange range, List<string> violations)
    {
        string unit = range.Unit ?? string.Empty;
        if (range.MinValue is { } min && value < min)
        {
            string message = $"Input '{name}' value {PythonJson.Float(value)} is below plausible minimum {PythonJson.Float(min)} {unit}".Trim();
            violations.Add(message);
            if (!range.WarningOnly) return message;
        }

        if (range.MaxValue is { } max && value > max)
        {
            string message = $"Input '{name}' value {PythonJson.Float(value)} exceeds plausible maximum {PythonJson.Float(max)} {unit}".Trim();
            violations.Add(message);
            if (!range.WarningOnly) return message;
        }

        return null;
    }

    private static double Compute(DerivedFormulaType type, Dictionary<string, object?> ctx) => type switch
    {
        DerivedFormulaType.Bmi => Bmi(ctx),
        DerivedFormulaType.BsaMosteller => Math.Sqrt(Number(ctx, "height_cm", "height") * Number(ctx, "weight_kg", "weight") / 3600.0),
        DerivedFormulaType.BsaDubois => 0.007184 * Math.Pow(Number(ctx, "height_cm", "height"), 0.725) * Math.Pow(Number(ctx, "weight_kg", "weight"), 0.425),
        DerivedFormulaType.EgfrCkdEpi2021 => EgfrCkdEpi2021(ctx),
        DerivedFormulaType.EgfrMdrd => 175.0 * Math.Pow(Creatinine(ctx), -1.154) * Math.Pow(Number(ctx, "age"), -0.203) * (IsFemale(ctx) ? 0.742 : 1.0),
        DerivedFormulaType.QtcFridericia => Number(ctx, "qt_ms", "qt") / Math.Pow(RrSeconds(ctx, "QTcF"), 1.0 / 3.0),
        DerivedFormulaType.QtcBazett => Number(ctx, "qt_ms", "qt") / Math.Sqrt(RrSeconds(ctx, "QTcB")),
        DerivedFormulaType.Map => MeanArterialPressure(ctx),
        DerivedFormulaType.CockcroftGault => CockcroftGault(ctx),
        _ => throw new InvalidOperationException($"Unsupported formula type: {type}"),
    };

    private static double Bmi(Dictionary<string, object?> ctx)
    {
        double weightKg = Number(ctx, "weight_kg", "weight");
        double heightM = Number(ctx, "height_cm", "height") / 100.0;
        if (heightM <= 0) throw new InvalidOperationException("Height must be greater than zero");
        return weightKg / (heightM * heightM);
    }

    private static double EgfrCkdEpi2021(Dictionary<string, object?> ctx)
    {
        double scr = Creatinine(ctx);
        double age = Number(ctx, "age");
        bool female = IsFemale(ctx);
        double kappa = female ? 0.7 : 0.9;
        double alpha = female ? -0.241 : -0.302;
        double sexFactor = female ? 1.012 : 1.0;

        double ratio = scr / kappa;
        return 142.0 * Math.Pow(Math.Min(ratio, 1.0), alpha) * Math.Pow(Math.Max(ratio, 1.0), -1.200) * Math.Pow(0.9938, age) * sexFactor;
    }

    private static double MeanArterialPressure(Dictionary<string, object?> ctx)
    {
        double sbp = Number(ctx, "systolic_bp", "sbp", "systolic");
        double dbp = Number(ctx, "diastolic_bp", "dbp", "diastolic");
        return dbp + (1.0 / 3.0) * (sbp - dbp);
    }

    private static double CockcroftGault(Dictionary<string, object?> ctx)
    {
        double age = Number(ctx, "age");
        double weightKg = Number(ctx, "weight_kg", "weight");
        double scr = Creatinine(ctx);
        bool female = IsFemale(ctx);
        if (scr <= 0) throw new InvalidOperationException("Serum creatinine must be greater than zero");

        double clearance = (140.0 - age) * weightKg / (72.0 * scr);
        return female ? clearance * 0.85 : clearance;
    }

    private static double Creatinine(Dictionary<string, object?> ctx) => Number(ctx, "scr", "serum_creatinine", "creatinine");

    private static double RrSeconds(Dictionary<string, object?> ctx, string formulaName)
    {
        double rr;
        if (ctx.ContainsKey("rr_s") || ctx.ContainsKey("rr"))
        {
            rr = Number(ctx, "rr_s", "rr");
        }
        else if (ctx.ContainsKey("heart_rate") || ctx.ContainsKey("hr"))
        {
            double heartRate = Number(ctx, "heart_rate", "hr");
            if (heartRate <= 0) throw new InvalidOperationException("Heart rate must be greater than zero");
            rr = 60.0 / heartRate;
        }
        else
        {
            throw new InvalidOperationException($"Either 'rr_s' or 'heart_rate' is required for {formulaName} calculation");
        }

        if (rr <= 0) throw new InvalidOperationException("RR interval must be greater than zero");
        return rr;
    }

    // The Python defaulted a missing sex to female and read any other value as male; the port requires F or M.
    private static bool IsFemale(Dictionary<string, object?> ctx)
    {
        object? raw = HasValue(ctx.GetValueOrDefault("sex")) ? ctx["sex"] : ctx.GetValueOrDefault("gender");
        string sex = (Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty).Trim().ToUpperInvariant();
        return sex switch
        {
            "F" or "FEMALE" => true,
            "M" or "MALE" => false,
            "" => throw new InvalidOperationException("'sex' or 'gender' is required"),
            _ => throw new InvalidOperationException($"Sex must be F, FEMALE, M or MALE (got {raw})"),
        };
    }

    // Resolves input aliases: the first present value among the leading keys, else the last key,
    // which must be present.
    private static double Number(Dictionary<string, object?> ctx, params string[] keys)
    {
        for (int i = 0; i < keys.Length - 1; i++)
        {
            if (ctx.TryGetValue(keys[i], out object? candidate) && HasValue(candidate))
            {
                return ToNumber(keys[i], candidate);
            }
        }

        string last = keys[^1];
        if (!ctx.TryGetValue(last, out object? value)) throw new InvalidOperationException($"'{last}'");
        return ToNumber(last, value);
    }

    private static double ToNumber(string key, object? value) =>
        TryToDouble(value, out double number)
            ? number
            : throw new InvalidOperationException($"'{key}' must be numeric (got {Convert.ToString(value, CultureInfo.InvariantCulture)})");

    private static bool HasValue(object? value) => value switch
    {
        null => false,
        string s => s.Trim().Length > 0,
        _ => true,
    };

    private static string NotNumeric(string name, object? value) =>
        $"Input variable '{name}' must be numeric (got {Convert.ToString(value, CultureInfo.InvariantCulture)})";

    // Numbers and numeric text only. The Python also read true and false as 1 and 0; the port does not.
    private static bool TryToDouble(object? value, out double number)
    {
        switch (value)
        {
            case double d: number = d; break;
            case float f: number = f; break;
            case int or long or short or byte or sbyte or uint or ulong or ushort or decimal:
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                break;
            case string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed):
                number = parsed;
                break;
            default:
                number = 0;
                return false;
        }

        return double.IsFinite(number);
    }
}
