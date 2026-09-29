using System;
using System.Collections.Generic;

using Cadence.Core.Domain.Rules;

namespace Cadence.Core.Domain.DerivedMetrics;

/// <summary>
/// Pure mathematical execution engine for clinical derived variables and biostatistical formulas.
/// Evaluates standardized medical formulas (eGFR, BSA, BMI, QTcF, MAP, Cockcroft-Gault)
/// with physiological plausibility bounds and unit normalization.
/// @req:PRD-SYS-053
/// </summary>
public class DerivedMetricsEngine
{
    private readonly RuleEvaluator _ruleEvaluator = new();

    public static Dictionary<string, object?> NormalizeInputUnits(IReadOnlyDictionary<string, object?> inputs)
    {
        var normalized = new Dictionary<string, object?>(inputs ?? new Dictionary<string, object?>());

        // Weight conversion (lbs -> kg)
        if (normalized.TryGetValue("weight_lbs", out var wLbsObj) &&
            !normalized.ContainsKey("weight") &&
            !normalized.ContainsKey("weight_kg"))
        {
            if (double.TryParse(wLbsObj?.ToString(), out double wLbs))
            {
                double wKg = wLbs * 0.45359237;
                normalized["weight_kg"] = wKg;
                normalized["weight"] = wKg;
            }
        }
        else if (normalized.TryGetValue("weight", out var wObj) && !normalized.ContainsKey("weight_kg") && double.TryParse(wObj?.ToString(), out double wVal))
        {
            normalized["weight_kg"] = wVal;
        }

        // Height conversion (inches -> cm)
        if (normalized.TryGetValue("height_in", out var hInObj) &&
            !normalized.ContainsKey("height") &&
            !normalized.ContainsKey("height_cm"))
        {
            if (double.TryParse(hInObj?.ToString(), out double hIn))
            {
                double hCm = hIn * 2.54;
                normalized["height_cm"] = hCm;
                normalized["height"] = hCm;
            }
        }
        else if (normalized.TryGetValue("height", out var hObj) && !normalized.ContainsKey("height_cm") && double.TryParse(hObj?.ToString(), out double hVal))
        {
            normalized["height_cm"] = hVal;
        }

        // Serum Creatinine (umol/L -> mg/dL)
        if (normalized.TryGetValue("serum_creatinine_umol", out var scrUmolObj) && !normalized.ContainsKey("serum_creatinine"))
        {
            if (double.TryParse(scrUmolObj?.ToString(), out double scrUmol))
            {
                double scrMg = scrUmol / 88.42;
                normalized["serum_creatinine"] = scrMg;
                normalized["scr"] = scrMg;
            }
        }
        else if (normalized.TryGetValue("serum_creatinine", out var scrObj) && !normalized.ContainsKey("scr") && double.TryParse(scrObj?.ToString(), out double scrVal))
        {
            normalized["scr"] = scrVal;
        }

        return normalized;
    }

    public DerivedCalculationResult Calculate(
        DerivedFormulaDefinition formula,
        IReadOnlyDictionary<string, object?> context)
    {
        ArgumentNullException.ThrowIfNull(formula);

        var ctx = NormalizeInputUnits(context);
        var violations = new List<string>();

        // Validate input requirements and plausibility bounds
        foreach (var req in formula.Inputs)
        {
            ctx.TryGetValue(req.Name, out object? val);
            if (val == null && req.DefaultValue.HasValue)
            {
                val = req.DefaultValue.Value;
                ctx[req.Name] = val;
            }

            if (val == null)
            {
                if (req.Required)
                {
                    return new DerivedCalculationResult(
                        FormulaId: formula.FormulaId,
                        OutputVariable: formula.OutputVariable,
                        IsValid: false,
                        ErrorMessage: $"Missing required input variable '{req.Name}'",
                        InputValuesUsed: ctx
                    );
                }
                continue;
            }

            bool isNumeric = double.TryParse(val.ToString(), out double numVal);
            if (!isNumeric && (req.Plausibility != null || val is not string))
            {
                return new DerivedCalculationResult(
                    FormulaId: formula.FormulaId,
                    OutputVariable: formula.OutputVariable,
                    IsValid: false,
                    ErrorMessage: $"Input variable '{req.Name}' must be numeric (got {val})",
                    InputValuesUsed: ctx
                );
            }

            // Plausibility bounds checks
            if (isNumeric && req.Plausibility != null)
            {
                var p = req.Plausibility;
                if (p.MinValue.HasValue && numVal < p.MinValue.Value)
                {
                    string msg = $"Input '{req.Name}' value {numVal} is below plausible minimum {p.MinValue.Value} {p.Unit ?? ""}".Trim();
                    violations.Add(msg);
                    if (!p.WarningOnly)
                    {
                        return new DerivedCalculationResult(
                            FormulaId: formula.FormulaId,
                            OutputVariable: formula.OutputVariable,
                            IsValid: false,
                            ErrorMessage: msg,
                            PlausibilityViolations: violations,
                            InputValuesUsed: ctx
                        );
                    }
                }

                if (p.MaxValue.HasValue && numVal > p.MaxValue.Value)
                {
                    string msg = $"Input '{req.Name}' value {numVal} exceeds plausible maximum {p.MaxValue.Value} {p.Unit ?? ""}".Trim();
                    violations.Add(msg);
                    if (!p.WarningOnly)
                    {
                        return new DerivedCalculationResult(
                            FormulaId: formula.FormulaId,
                            OutputVariable: formula.OutputVariable,
                            IsValid: false,
                            ErrorMessage: msg,
                            PlausibilityViolations: violations,
                            InputValuesUsed: ctx
                        );
                    }
                }
            }
        }

        // Dispatch calculation
        double? calcVal = null;
        try
        {
            calcVal = formula.FormulaType switch
            {
                DerivedFormulaType.BMI => CalculateBmi(ctx),
                DerivedFormulaType.BSA_MOSTELLER => CalculateBsaMosteller(ctx),
                DerivedFormulaType.BSA_DUBOIS => CalculateBsaDubois(ctx),
                DerivedFormulaType.EGFR_CKD_EPI_2021 => CalculateEgfrCkdEpi2021(ctx),
                DerivedFormulaType.EGFR_MDRD => CalculateEgfrMdrd(ctx),
                DerivedFormulaType.QTC_FRIDERICIA => CalculateQtcFridericia(ctx),
                DerivedFormulaType.QTC_BAZETT => CalculateQtcBazett(ctx),
                DerivedFormulaType.MAP => CalculateMap(ctx),
                DerivedFormulaType.COCKCROFT_GAULT => CalculateCockcroftGault(ctx),
                DerivedFormulaType.CUSTOM_AST => EvaluateCustomAst(formula, ctx),
                _ => throw new InvalidOperationException($"Unsupported formula type: {formula.FormulaType}")
            };
        }
        catch (Exception err)
        {
            return new DerivedCalculationResult(
                FormulaId: formula.FormulaId,
                OutputVariable: formula.OutputVariable,
                IsValid: false,
                ErrorMessage: $"Calculation error: {err.Message}",
                PlausibilityViolations: violations,
                InputValuesUsed: ctx
            );
        }

        return new DerivedCalculationResult(
            FormulaId: formula.FormulaId,
            OutputVariable: formula.OutputVariable,
            CalculatedValue: calcVal.HasValue ? Math.Round(calcVal.Value, 4) : null,
            OutputUnit: formula.OutputUnit,
            IsValid: true,
            PlausibilityViolations: violations,
            InputValuesUsed: ctx
        );
    }

    private double? EvaluateCustomAst(DerivedFormulaDefinition formula, Dictionary<string, object?> ctx)
    {
        if (formula.Expression == null)
        {
            throw new InvalidOperationException("Formula type CUSTOM_AST requires an expression AST node");
        }
        object? raw = _ruleEvaluator.Evaluate(formula.Expression, ctx);
        return raw != null && double.TryParse(raw.ToString(), out double result) ? result : null;
    }

    private static double CalculateBmi(Dictionary<string, object?> ctx)
    {
        double weightKg = GetDoubleCtx(ctx, "weight_kg", "weight");
        double heightCm = GetDoubleCtx(ctx, "height_cm", "height");
        double heightM = heightCm / 100.0;
        if (heightM <= 0)
        {
            throw new ArgumentException("Height must be greater than zero");
        }
        return weightKg / (heightM * heightM);
    }

    private static double CalculateBsaMosteller(Dictionary<string, object?> ctx)
    {
        double weightKg = GetDoubleCtx(ctx, "weight_kg", "weight");
        double heightCm = GetDoubleCtx(ctx, "height_cm", "height");
        return Math.Sqrt((heightCm * weightKg) / 3600.0);
    }

    private static double CalculateBsaDubois(Dictionary<string, object?> ctx)
    {
        double weightKg = GetDoubleCtx(ctx, "weight_kg", "weight");
        double heightCm = GetDoubleCtx(ctx, "height_cm", "height");
        return 0.007184 * Math.Pow(heightCm, 0.725) * Math.Pow(weightKg, 0.425);
    }

    private static double CalculateEgfrCkdEpi2021(Dictionary<string, object?> ctx)
    {
        double scr = GetDoubleCtx(ctx, "scr", "serum_creatinine", "creatinine");
        double age = GetDoubleCtx(ctx, "age");
        string sexRaw = GetStringCtx(ctx, "sex", "gender") ?? "F";
        bool isFemale = sexRaw.Equals("F", StringComparison.OrdinalIgnoreCase) ||
                        sexRaw.Equals("FEMALE", StringComparison.OrdinalIgnoreCase);

        double kappa = isFemale ? 0.7 : 0.9;
        double alpha = isFemale ? -0.241 : -0.302;
        double sexFactor = isFemale ? 1.012 : 1.0;

        double scrRatio = scr / kappa;
        double minTerm = Math.Pow(Math.Min(scrRatio, 1.0), alpha);
        double maxTerm = Math.Pow(Math.Max(scrRatio, 1.0), -1.200);
        double ageFactor = Math.Pow(0.9938, age);

        return 142.0 * minTerm * maxTerm * ageFactor * sexFactor;
    }

    private static double CalculateEgfrMdrd(Dictionary<string, object?> ctx)
    {
        double scr = GetDoubleCtx(ctx, "scr", "serum_creatinine", "creatinine");
        double age = GetDoubleCtx(ctx, "age");
        string sexRaw = GetStringCtx(ctx, "sex", "gender") ?? "F";
        bool isFemale = sexRaw.Equals("F", StringComparison.OrdinalIgnoreCase) ||
                        sexRaw.Equals("FEMALE", StringComparison.OrdinalIgnoreCase);

        double femaleFactor = isFemale ? 0.742 : 1.0;
        return 175.0 * Math.Pow(scr, -1.154) * Math.Pow(age, -0.203) * femaleFactor;
    }

    private static double CalculateQtcFridericia(Dictionary<string, object?> ctx)
    {
        double qtMs = GetDoubleCtx(ctx, "qt_ms", "qt");
        double rrS;

        if (TryGetDoubleCtx(ctx, out double rrVal, "rr_s", "rr"))
        {
            rrS = rrVal;
        }
        else if (TryGetDoubleCtx(ctx, out double hr, "heart_rate", "hr"))
        {
            if (hr <= 0)
            {
                throw new ArgumentException("Heart rate must be greater than zero");
            }
            rrS = 60.0 / hr;
        }
        else
        {
            throw new ArgumentException("Either 'rr_s' or 'heart_rate' is required for QTcF calculation");
        }

        if (rrS <= 0)
        {
            throw new ArgumentException("RR interval must be greater than zero");
        }

        return qtMs / Math.Pow(rrS, 1.0 / 3.0);
    }

    private static double CalculateQtcBazett(Dictionary<string, object?> ctx)
    {
        double qtMs = GetDoubleCtx(ctx, "qt_ms", "qt");
        double rrS;

        if (TryGetDoubleCtx(ctx, out double rrVal, "rr_s", "rr"))
        {
            rrS = rrVal;
        }
        else if (TryGetDoubleCtx(ctx, out double hr, "heart_rate", "hr"))
        {
            if (hr <= 0)
            {
                throw new ArgumentException("Heart rate must be greater than zero");
            }
            rrS = 60.0 / hr;
        }
        else
        {
            throw new ArgumentException("Either 'rr_s' or 'heart_rate' is required for QTcB calculation");
        }

        if (rrS <= 0)
        {
            throw new ArgumentException("RR interval must be greater than zero");
        }

        return qtMs / Math.Sqrt(rrS);
    }

    private static double CalculateMap(Dictionary<string, object?> ctx)
    {
        double sbp = GetDoubleCtx(ctx, "systolic_bp", "sbp", "systolic");
        double dbp = GetDoubleCtx(ctx, "diastolic_bp", "dbp", "diastolic");
        return dbp + (1.0 / 3.0) * (sbp - dbp);
    }

    private static double CalculateCockcroftGault(Dictionary<string, object?> ctx)
    {
        double age = GetDoubleCtx(ctx, "age");
        double weightKg = GetDoubleCtx(ctx, "weight_kg", "weight");
        double scr = GetDoubleCtx(ctx, "scr", "serum_creatinine", "creatinine");
        string sexRaw = GetStringCtx(ctx, "sex", "gender") ?? "F";
        bool isFemale = sexRaw.Equals("F", StringComparison.OrdinalIgnoreCase) ||
                        sexRaw.Equals("FEMALE", StringComparison.OrdinalIgnoreCase);

        if (scr <= 0)
        {
            throw new ArgumentException("Serum creatinine must be greater than zero");
        }

        double crcl = ((140.0 - age) * weightKg) / (72.0 * scr);
        if (isFemale)
        {
            crcl *= 0.85;
        }

        return crcl;
    }

    private static double GetDoubleCtx(Dictionary<string, object?> ctx, params string[] keys)
    {
        if (TryGetDoubleCtx(ctx, out double val, keys))
        {
            return val;
        }
        throw new KeyNotFoundException($"Missing required numerical context variable: {string.Join(" or ", keys)}");
    }

    private static bool TryGetDoubleCtx(Dictionary<string, object?> ctx, out double result, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (ctx.TryGetValue(k, out object? obj) && obj != null && double.TryParse(obj.ToString(), out result))
            {
                return true;
            }
        }
        result = 0.0;
        return false;
    }

    private static string? GetStringCtx(Dictionary<string, object?> ctx, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (ctx.TryGetValue(k, out object? obj) && obj != null)
            {
                return obj.ToString()?.Trim();
            }
        }
        return null;
    }
}

/// <summary>
/// Alias class matching DerivedVariableEngine.
/// </summary>
public class DerivedVariableEngine : DerivedMetricsEngine
{
    public DerivedCalculationResult CalculateVariable(
        DerivedFormulaDefinition formula,
        IReadOnlyDictionary<string, object?> context) =>
        Calculate(formula, context);
}
