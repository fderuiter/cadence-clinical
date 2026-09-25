using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cadence.Core.Domain.Rules;

public class EvaluationException : Exception
{
    public EvaluationException(string message) : base(message) { }
}

public class UndefinedVariableException : EvaluationException
{
    public UndefinedVariableException(string message) : base(message) { }
}

public class TypeMismatchException : EvaluationException
{
    public TypeMismatchException(string message) : base(message) { }
}

public class RuleEvaluator
{
    public bool StrictVariables { get; }
    public DateTime AsOf { get; }

    public RuleEvaluator(bool strictVariables = false, DateTime? asOf = null)
    {
        StrictVariables = strictVariables;
        AsOf = asOf ?? DateTime.UtcNow;
    }

    public object? Evaluate(ExpressionNode node, Dictionary<string, object?> context)
    {
        if (node == null) return null;

        string nodeType = node.Type;

        if (nodeType == "constant")
        {
            return node.Value;
        }

        if (nodeType == "variable")
        {
            string varName = node.VariableName ?? string.Empty;
            object? val = ResolveContextValue(context, varName);
            if (val == null && StrictVariables && !context.ContainsKey(varName))
            {
                throw new UndefinedVariableException($"Undefined variable: '{varName}'");
            }
            return val;
        }

        if (nodeType == "field_ref")
        {
            if (node.FieldRef == null) return null;
            string fieldId = node.FieldRef.FieldId;
            object? val = ResolveContextValue(context, fieldId);
            if (val == null && StrictVariables)
            {
                throw new UndefinedVariableException($"Undefined field reference: '{fieldId}'");
            }
            return val;
        }

        var operands = node.Operands ?? new List<ExpressionNode>();

        if (nodeType == "logical")
        {
            return EvaluateLogical(node.Operator ?? string.Empty, operands, context);
        }

        if (nodeType == "comparison")
        {
            return EvaluateComparison(node.Operator ?? string.Empty, operands, context);
        }

        if (nodeType == "arithmetic")
        {
            return EvaluateArithmetic(node.Operator ?? string.Empty, operands, context);
        }

        if (nodeType == "function")
        {
            return EvaluateFunction(node.Operator ?? string.Empty, operands, context);
        }

        throw new EvaluationException($"Unknown node type: '{nodeType}'");
    }

    private static object? GetNestedValue(object? data, string path)
    {
        if (data is null) return null;

        if (data is Dictionary<string, object?> dict)
        {
            if (dict.TryGetValue(path, out var direct)) return direct;
            string[] parts = path.Split('.');
            object? curr = dict;
            foreach (var part in parts)
            {
                if (curr is Dictionary<string, object?> subDict && subDict.TryGetValue(part, out var child))
                {
                    curr = child;
                }
                else if (curr is JsonElement elem && elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty(part, out var jsonChild))
                {
                    curr = ConvertJsonElement(jsonChild);
                }
                else
                {
                    return null;
                }
            }
            return curr;
        }

        if (data is JsonElement jsonElem && jsonElem.ValueKind == JsonValueKind.Object)
        {
            if (jsonElem.TryGetProperty(path, out var prop)) return ConvertJsonElement(prop);
            string[] parts = path.Split('.');
            JsonElement curr = jsonElem;
            foreach (var part in parts)
            {
                if (curr.ValueKind == JsonValueKind.Object && curr.TryGetProperty(part, out var child))
                {
                    curr = child;
                }
                else
                {
                    return null;
                }
            }
            return ConvertJsonElement(curr);
        }

        return null;
    }

    private static object? ConvertJsonElement(JsonElement elem)
    {
        return elem.ValueKind switch
        {
            JsonValueKind.String => elem.GetString(),
            JsonValueKind.Number => elem.TryGetInt64(out long l) ? l : elem.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => elem
        };
    }

    public static object? ResolveContextValue(Dictionary<string, object?> context, string key, object? defaultValue = null)
    {
        var val = GetNestedValue(context, key);
        if (val != null) return val;

        string[] namespaces = ["data", "observations", "variables", "fields", "subject", "form"];
        foreach (var ns in namespaces)
        {
            if (context.TryGetValue(ns, out var wrapper) && wrapper != null)
            {
                val = GetNestedValue(wrapper, key);
                if (val != null) return val;
            }
        }

        return defaultValue;
    }

    private object? EvaluateLogical(string op, List<ExpressionNode> operands, Dictionary<string, object?> context)
    {
        if (op == "and")
        {
            return operands.All(o => IsTruthy(Evaluate(o, context)));
        }
        if (op == "or")
        {
            return operands.Any(o => IsTruthy(Evaluate(o, context)));
        }
        if (op == "not")
        {
            return !IsTruthy(Evaluate(operands[0], context));
        }
        if (op == "xor")
        {
            bool val1 = IsTruthy(Evaluate(operands[0], context));
            bool val2 = IsTruthy(Evaluate(operands[1], context));
            return val1 ^ val2;
        }
        if (op == "if_then_else")
        {
            bool cond = IsTruthy(Evaluate(operands[0], context));
            if (cond) return Evaluate(operands[1], context);
            if (operands.Count > 2) return Evaluate(operands[2], context);
            return null;
        }

        throw new EvaluationException($"Unsupported logical operator: '{op}'");
    }

    private bool EvaluateComparison(string op, List<ExpressionNode> operands, Dictionary<string, object?> context)
    {
        object? left = Evaluate(operands[0], context);
        object? right = Evaluate(operands[1], context);

        if (op == "==")
        {
            return EqualsComparison(left, right);
        }
        if (op == "!=")
        {
            return !EqualsComparison(left, right);
        }
        if (op is "<" or "<=" or ">" or ">=")
        {
            var dLeft = ParseDate(left);
            var dRight = ParseDate(right);
            if (dLeft.HasValue && dRight.HasValue)
            {
                return op switch
                {
                    "<" => dLeft < dRight,
                    "<=" => dLeft <= dRight,
                    ">" => dLeft > dRight,
                    ">=" => dLeft >= dRight,
                    _ => false
                };
            }

            var nLeft = ToNumber(left);
            var nRight = ToNumber(right);
            if (nLeft.HasValue && nRight.HasValue)
            {
                return op switch
                {
                    "<" => nLeft < nRight,
                    "<=" => nLeft <= nRight,
                    ">" => nLeft > nRight,
                    ">=" => nLeft >= nRight,
                    _ => false
                };
            }

            if (left is string s1 && right is string s2)
            {
                int cmp = string.Compare(s1, s2, StringComparison.Ordinal);
                return op switch
                {
                    "<" => cmp < 0,
                    "<=" => cmp <= 0,
                    ">" => cmp > 0,
                    ">=" => cmp >= 0,
                    _ => false
                };
            }

            return false;
        }
        if (op == "in")
        {
            if (right is IEnumerable seq and not string)
            {
                return seq.Cast<object?>().Any(item => EqualsComparison(left, item));
            }
            if (right is string rStr && left is string lStr)
            {
                return rStr.Contains(lStr);
            }
            return false;
        }
        if (op == "not_in")
        {
            return !EvaluateComparison("in", operands, context);
        }
        if (op == "between")
        {
            object? lower = Evaluate(operands[1], context);
            object? upper = Evaluate(operands[2], context);

            var valNum = ToNumber(left);
            var lowNum = ToNumber(lower);
            var highNum = ToNumber(upper);
            if (valNum.HasValue && lowNum.HasValue && highNum.HasValue)
            {
                return valNum.Value >= lowNum.Value && valNum.Value <= highNum.Value;
            }

            var dVal = ParseDate(left);
            var dLow = ParseDate(lower);
            var dHigh = ParseDate(upper);
            if (dVal.HasValue && dLow.HasValue && dHigh.HasValue)
            {
                return dVal.Value >= dLow.Value && dVal.Value <= dHigh.Value;
            }

            return false;
        }
        if (op == "regex_match")
        {
            if (left == null || right == null) return false;
            try
            {
                return Regex.IsMatch(left.ToString() ?? "", right.ToString() ?? "");
            }
            catch
            {
                return false;
            }
        }

        throw new EvaluationException($"Unsupported comparison operator: '{op}'");
    }

    private object? EvaluateArithmetic(string op, List<ExpressionNode> operands, Dictionary<string, object?> context)
    {
        var vals = operands.Select(o => Evaluate(o, context)).ToList();

        if (op == "+")
        {
            if (vals.Any(v => v is string))
            {
                return string.Concat(vals.Select(v => v?.ToString() ?? ""));
            }
            var nums = vals.Select(ToNumber).ToList();
            if (nums.Any(n => !n.HasValue)) return null;
            double sum = nums.Sum(n => n!.Value);
            return Math.Abs(sum % 1) < 1e-9 ? (object)(long)sum : sum;
        }
        if (op == "-")
        {
            if (vals.Count == 1)
            {
                var n = ToNumber(vals[0]);
                return n.HasValue ? -n.Value : null;
            }
            var n1 = ToNumber(vals[0]);
            var n2 = ToNumber(vals[1]);
            if (!n1.HasValue || !n2.HasValue) return null;
            double diff = n1.Value - n2.Value;
            return Math.Abs(diff % 1) < 1e-9 ? (object)(long)diff : diff;
        }
        if (op == "*")
        {
            double res = 1.0;
            foreach (var v in vals)
            {
                var n = ToNumber(v);
                if (!n.HasValue) return null;
                res *= n.Value;
            }
            return Math.Abs(res % 1) < 1e-9 ? (object)(long)res : res;
        }
        if (op is "/" or "//")
        {
            var n1 = ToNumber(vals[0]);
            var n2 = ToNumber(vals[1]);
            if (!n1.HasValue || !n2.HasValue || Math.Abs(n2.Value) < 1e-9) return null;
            if (op == "//")
            {
                return (long)Math.Floor(n1.Value / n2.Value);
            }
            double div = n1.Value / n2.Value;
            return Math.Abs(div % 1) < 1e-9 ? (object)(long)div : div;
        }
        if (op == "%")
        {
            var n1 = ToNumber(vals[0]);
            var n2 = ToNumber(vals[1]);
            if (!n1.HasValue || !n2.HasValue || Math.Abs(n2.Value) < 1e-9) return null;
            return n1.Value % n2.Value;
        }
        if (op is "^" or "**")
        {
            var n1 = ToNumber(vals[0]);
            var n2 = ToNumber(vals[1]);
            if (!n1.HasValue || !n2.HasValue) return null;
            double pow = Math.Pow(n1.Value, n2.Value);
            return Math.Abs(pow % 1) < 1e-9 ? (object)(long)pow : pow;
        }
        if (op == "abs")
        {
            var n = ToNumber(vals[0]);
            return n.HasValue ? Math.Abs(n.Value) : null;
        }
        if (op == "round")
        {
            var n = ToNumber(vals[0]);
            int digits = vals.Count > 1 ? (int)(ToNumber(vals[1]) ?? 0) : 0;
            return n.HasValue ? Math.Round(n.Value, digits) : null;
        }
        if (op == "floor")
        {
            var n = ToNumber(vals[0]);
            return n.HasValue ? Math.Floor(n.Value) : null;
        }
        if (op == "ceil")
        {
            var n = ToNumber(vals[0]);
            return n.HasValue ? Math.Ceiling(n.Value) : null;
        }

        throw new EvaluationException($"Unsupported arithmetic operator: '{op}'");
    }

    private object? EvaluateFunction(string op, List<ExpressionNode> operands, Dictionary<string, object?> context)
    {
        if (op is "is_empty" or "empty")
        {
            object? val = Evaluate(operands[0], context);
            if (val == null || (val is string s && string.IsNullOrEmpty(s))) return true;
            if (val is ICollection col) return col.Count == 0;
            return false;
        }
        if (op == "is_not_empty")
        {
            return !((bool)EvaluateFunction("is_empty", operands, context)!);
        }
        if (op is "coalesce" or "if_null")
        {
            foreach (var opNode in operands)
            {
                object? val = Evaluate(opNode, context);
                if (val != null && !(val is string s && string.IsNullOrEmpty(s))) return val;
            }
            return null;
        }
        if (op == "contains")
        {
            object? target = Evaluate(operands[0], context);
            object? search = Evaluate(operands[1], context);
            if (target == null || search == null) return false;
            if (target is IEnumerable seq and not string)
            {
                return seq.Cast<object?>().Any(item => EqualsComparison(search, item));
            }
            return target.ToString()!.Contains(search.ToString()!, StringComparison.OrdinalIgnoreCase);
        }
        if (op == "starts_with")
        {
            object? target = Evaluate(operands[0], context);
            object? prefix = Evaluate(operands[1], context);
            if (target == null || prefix == null) return false;
            return target.ToString()!.StartsWith(prefix.ToString()!, StringComparison.OrdinalIgnoreCase);
        }
        if (op == "ends_with")
        {
            object? target = Evaluate(operands[0], context);
            object? suffix = Evaluate(operands[1], context);
            if (target == null || suffix == null) return false;
            return target.ToString()!.EndsWith(suffix.ToString()!, StringComparison.OrdinalIgnoreCase);
        }
        if (op == "lower")
        {
            object? val = Evaluate(operands[0], context);
            return val?.ToString()?.ToLowerInvariant() ?? "";
        }
        if (op == "upper")
        {
            object? val = Evaluate(operands[0], context);
            return val?.ToString()?.ToUpperInvariant() ?? "";
        }
        if (op == "trim")
        {
            object? val = Evaluate(operands[0], context);
            return val?.ToString()?.Trim() ?? "";
        }
        if (op == "length")
        {
            object? val = Evaluate(operands[0], context);
            if (val == null) return 0;
            if (val is ICollection col) return col.Count;
            if (val is string s) return s.Length;
            return val.ToString()!.Length;
        }
        if (op is "sum" or "avg" or "min" or "max" or "count")
        {
            var rawVals = operands.Select(o => Evaluate(o, context)).ToList();
            var extracted = new List<double>();
            foreach (var item in rawVals)
            {
                if (item is IEnumerable seq and not string)
                {
                    foreach (var sub in seq)
                    {
                        var n = ToNumber(sub);
                        if (n.HasValue) extracted.Add(n.Value);
                    }
                }
                else
                {
                    var n = ToNumber(item);
                    if (n.HasValue) extracted.Add(n.Value);
                }
            }

            if (op == "count") return extracted.Count;
            if (extracted.Count == 0) return null;

            if (op == "sum") return extracted.Sum();
            if (op == "avg") return Math.Round(extracted.Average(), 4);
            if (op == "min") return extracted.Min();
            if (op == "max") return extracted.Max();
        }
        if (op == "date_diff_days")
        {
            var d1 = ParseDate(Evaluate(operands[0], context));
            var d2 = ParseDate(Evaluate(operands[1], context));
            if (!d1.HasValue || !d2.HasValue) return null;
            return (d1.Value - d2.Value).Days;
        }
        if (op == "date_add_days")
        {
            var d = ParseDate(Evaluate(operands[0], context));
            var days = ToNumber(Evaluate(operands[1], context));
            if (!d.HasValue || !days.HasValue) return null;
            return d.Value.AddDays(days.Value).ToString("yyyy-MM-dd");
        }
        if (op == "is_future")
        {
            var d = ParseDate(Evaluate(operands[0], context));
            return d.HasValue && d.Value > AsOf;
        }
        if (op == "is_past")
        {
            var d = ParseDate(Evaluate(operands[0], context));
            return d.HasValue && d.Value < AsOf;
        }
        if (op == "age_years")
        {
            var birthDt = ParseDate(Evaluate(operands[0], context));
            if (!birthDt.HasValue) return null;
            DateTime refDt = AsOf;
            if (operands.Count > 1)
            {
                var customRef = ParseDate(Evaluate(operands[1], context));
                if (customRef.HasValue) refDt = customRef.Value;
            }
            int age = refDt.Year - birthDt.Value.Year;
            if (refDt.Month < birthDt.Value.Month || (refDt.Month == birthDt.Value.Month && refDt.Day < birthDt.Value.Day))
            {
                age--;
            }
            return Math.Max(0, age);
        }

        throw new EvaluationException($"Unsupported function: '{op}'");
    }

    private static bool IsTruthy(object? val)
    {
        if (val == null) return false;
        if (val is bool b) return b;
        if (val is int i) return i != 0;
        if (val is double d) return Math.Abs(d) > 1e-9;
        if (val is string s) return !string.IsNullOrEmpty(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static bool EqualsComparison(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;

        var n1 = ToNumber(left);
        var n2 = ToNumber(right);
        if (n1.HasValue && n2.HasValue)
        {
            return Math.Abs(n1.Value - n2.Value) < 1e-9;
        }

        if (left is bool b1 && right is string s2)
        {
            return b1 == (s2.Equals("true", StringComparison.OrdinalIgnoreCase) || s2 == "1");
        }
        if (right is bool b2 && left is string s1)
        {
            return b2 == (s1.Equals("true", StringComparison.OrdinalIgnoreCase) || s1 == "1");
        }

        return left.Equals(right) || string.Equals(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static double? ToNumber(object? val)
    {
        if (val == null || val is bool) return null;
        if (val is int i) return i;
        if (val is long l) return l;
        if (val is double d) return d;
        if (val is float f) return f;
        if (val is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
        {
            return parsed;
        }
        return null;
    }

    private static DateTime? ParseDate(object? val)
    {
        if (val == null) return null;
        if (val is DateTime dt) return dt.ToUniversalTime();
        if (val is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    public static ExpressionNode ParseJsonLogic(object? logic)
    {
        if (logic is not Dictionary<string, object?> dict || dict.Count != 1)
        {
            return new ExpressionNode { Type = "constant", Value = logic };
        }

        var entry = dict.First();
        string op = entry.Key;
        object? rawArgs = entry.Value;

        if (op == "var")
        {
            if (rawArgs is string varName)
            {
                return new ExpressionNode { Type = "field_ref", FieldRef = new FieldReference(varName) };
            }
            return new ExpressionNode { Type = "constant", Value = null };
        }

        var argsList = rawArgs is List<object?> list ? list : [rawArgs];
        var parsedOperands = argsList.Select(ParseJsonLogic).ToList();

        if (op is "==" or "===" or "!=" or "!==" or "<" or "<=" or ">" or ">=" or "in")
        {
            string mappedOp = op switch { "===" => "==", "!==" => "!=", _ => op };
            return new ExpressionNode { Type = "comparison", Operator = mappedOp, Operands = parsedOperands };
        }

        if (op is "and" or "or" or "!" or "not" or "?:" or "if")
        {
            string mappedOp = op switch { "!" => "not", "?:" => "if_then_else", "if" => "if_then_else", _ => op };
            return new ExpressionNode { Type = "logical", Operator = mappedOp, Operands = parsedOperands };
        }

        if (op is "+" or "-" or "*" or "/" or "%" or "min" or "max")
        {
            return new ExpressionNode { Type = "arithmetic", Operator = op, Operands = parsedOperands };
        }

        return new ExpressionNode { Type = "function", Operator = op, Operands = parsedOperands };
    }
}
