using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Domain.Observations;

namespace Cadence.Apps.Execution.Infrastructure.EditChecks;

/// <summary>
/// High-performance Abstract Syntax Tree (AST) edit check evaluator.
/// Matches legacy Python <c>evaluator.py</c> and <c>edit_checks.py</c> semantics.
/// @req:PRD-SYS-004
/// @req:SRS-EDC-002
/// @req:SRS-EDC-005
/// </summary>
public sealed class AstEditCheckEngine : IEditCheckEngine
{
    public object? EvaluateAst(object? astNode, IReadOnlyDictionary<string, object?> context) =>
        EvaluateAstCore(astNode, context, 0);

    private object? EvaluateAstCore(object? astNode, IReadOnlyDictionary<string, object?> context, int depth)
    {
        if (astNode is null || depth > 64) return null;

        // Direct primitive value
        if (astNode is JsonElement je)
        {
            switch (je.ValueKind)
            {
                case JsonValueKind.Number:
                    return je.TryGetDecimal(out var dec) ? dec : je.GetDouble();
                case JsonValueKind.String:
                    return je.GetString();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;
            }
        }
        else if (astNode is double or float or int or long or decimal or bool or string)
        {
            return astNode;
        }

        // Check for variable reference: {"var": "field_name"}
        string? varName = GetNodeAttribute(astNode, "var");
        if (!string.IsNullOrEmpty(varName))
        {
            return context.TryGetValue(varName, out var v) ? v : null;
        }

        string nodeType = GetNodeAttribute(astNode, "node_type") ?? GetNodeAttribute(astNode, "type") ?? string.Empty;
        object? value = GetNodeRawAttribute(astNode, "value");
        var operands = GetNodeOperands(astNode);

        // 1. LITERAL / CONSTANT
        if (string.Equals(nodeType, "LITERAL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(nodeType, "constant", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        // 2. XPATH / FIELD_REF
        if (string.Equals(nodeType, "XPATH", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(nodeType, "field_ref", StringComparison.OrdinalIgnoreCase))
        {
            string path = ExtractPathOrField(astNode);
            if (string.IsNullOrEmpty(path)) return null;

            if (context.TryGetValue(path, out var directVal)) return directVal;

            if (path.StartsWith("../", StringComparison.Ordinal))
            {
                string resolvedRelative = "/clinical_data/subject/" + path[3..];
                if (context.TryGetValue(resolvedRelative, out var relVal)) return relVal;
            }

            var parts = path.Split('/');
            string bareField = parts[^1];
            if (context.TryGetValue(bareField, out var bareVal)) return bareVal;

            return null;
        }

        // 3. OPERATOR / LOGICAL / COMPARISON / ARITHMETIC
        string op = (GetNodeAttribute(astNode, "op") ?? GetNodeAttribute(astNode, "operator") ?? value?.ToString() ?? string.Empty).ToLowerInvariant();
        if (!string.IsNullOrEmpty(op))
        {
            // Kleene logic: NOT
            if (op is "not" or "!")
            {
                if (operands.Count == 0) return null;
                var child = EvaluateAstCore(operands[0], context, depth + 1);
                if (child is null) return null;
                if (child is bool b) return !b;
                return null;
            }

            // Kleene logic: AND
            if (op is "and" or "&&")
            {
                bool hasNull = false;
                foreach (var operand in operands)
                {
                    var child = EvaluateAstCore(operand, context, depth + 1);
                    if (child is bool b && !b) return false;
                    if (child is null) hasNull = true;
                }
                return hasNull ? null : true;
            }

            // Kleene logic: OR
            if (op is "or" or "||")
            {
                bool hasNull = false;
                foreach (var operand in operands)
                {
                    var child = EvaluateAstCore(operand, context, depth + 1);
                    if (child is bool b && b) return true;
                    if (child is null) hasNull = true;
                }
                return hasNull ? null : false;
            }

            if (operands.Count < 2) return null;

            var left = EvaluateAstCore(operands[0], context, depth + 1);
            var right = EvaluateAstCore(operands[1], context, depth + 1);

            // Arithmetic: +, -, *, /
            if (op is "+" or "-" or "*" or "/")
            {
                if (left is null || right is null) return null;
                if (!TryToDecimal(left, out decimal l) || !TryToDecimal(right, out decimal r)) return null;

                try
                {
                    return op switch
                    {
                        "+" => l + r,
                        "-" => l - r,
                        "*" => l * r,
                        "/" => r == 0m ? null : l / r,
                        _ => null
                    };
                }
                catch (OverflowException)
                {
                    return null;
                }
                catch (DivideByZeroException)
                {
                    return null;
                }
            }

            // Comparison: ==, !=, <, <=, >, >=
            if (op is "==" or "!=" or "<" or "<=" or ">" or ">=")
            {
                if (left is null && right is null)
                {
                    return op switch
                    {
                        "==" => true,
                        "!=" => false,
                        _ => false
                    };
                }

                if (left is null || right is null)
                {
                    return op switch
                    {
                        "==" => false,
                        "!=" => true,
                        _ => false
                    };
                }

                if (TryToDecimal(left, out decimal lNum) && TryToDecimal(right, out decimal rNum))
                {
                    return op switch
                    {
                        "==" => lNum == rNum,
                        "!=" => lNum != rNum,
                        "<" => lNum < rNum,
                        "<=" => lNum <= rNum,
                        ">" => lNum > rNum,
                        ">=" => lNum >= rNum,
                        _ => false
                    };
                }

                if (left is bool lBool && right is bool rBool)
                {
                    return op switch
                    {
                        "==" => lBool == rBool,
                        "!=" => lBool != rBool,
                        _ => false
                    };
                }

                string lStr = left.ToString() ?? string.Empty;
                string rStr = right.ToString() ?? string.Empty;
                int cmp = string.Compare(lStr, rStr, StringComparison.Ordinal);

                return op switch
                {
                    "==" => cmp == 0,
                    "!=" => cmp != 0,
                    "<" => cmp < 0,
                    "<=" => cmp <= 0,
                    ">" => cmp > 0,
                    ">=" => cmp >= 0,
                    _ => false
                };
            }
        }

        // 4. FUNCTION
        if (string.Equals(nodeType, "FUNCTION", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(nodeType, "function", StringComparison.OrdinalIgnoreCase))
        {
            string funcName = (GetNodeAttribute(astNode, "operator") ?? value?.ToString() ?? string.Empty).ToLowerInvariant();

            if (funcName is "is_empty" or "empty")
            {
                if (operands.Count != 1) return null;
                var child = EvaluateAstCore(operands[0], context, depth + 1);
                return child is null || string.IsNullOrWhiteSpace(child.ToString());
            }

            if (funcName == "is_not_empty")
            {
                if (operands.Count != 1) return null;
                var child = EvaluateAstCore(operands[0], context, depth + 1);
                return child != null && !string.IsNullOrWhiteSpace(child.ToString());
            }

            if (funcName == "indexed-repeat")
            {
                if (operands.Count != 3) return null;
                var targetFieldNode = operands[0];
                var repeatGroupNode = operands[1];
                var indexNode = operands[2];

                string targetPath = ExtractPathOrField(targetFieldNode);
                string repeatGroup = ExtractPathOrField(repeatGroupNode);

                var indexVal = EvaluateAstCore(indexNode, context, depth + 1);
                if (indexVal is null || !TryToDecimal(indexVal, out decimal idxDec))
                {
                    return null;
                }

                int targetIndex = (int)idxDec;
                string fieldName = targetPath.Split('/')[^1];
                string indexedPath = $"{repeatGroup}[{targetIndex}]/{fieldName}";

                return context.TryGetValue(indexedPath, out var indexedVal) ? indexedVal : null;
            }
        }

        return null;
    }

    public IReadOnlyList<EditCheckViolation> EvaluateInlineConstraints(ClinicalObservation observation)
    {
        var violations = new List<EditCheckViolation>();

        // Check for unit mismatch / invalid unit code
        if (!string.IsNullOrWhiteSpace(observation.Unit) &&
            (observation.NormalizedUnit is null || string.Equals(observation.Unit, "INVALID_UNIT", StringComparison.OrdinalIgnoreCase) || string.Equals(observation.Unit, "ERR", StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add(new EditCheckViolation(
                "RULE_UNIT_MISMATCH",
                $"Unit '{observation.Unit}' is invalid for test {observation.TestCode.Value}.",
                "WARNING",
                observation.TestCode.Value,
                IsAutoDispatchEnabled: true,
                StandardTemplate: $"Invalid unit code '{observation.Unit}' provided for test {observation.TestCode.Value}. Please select or enter a valid UCUM unit code."));
        }

        double? val = observation.Value;
        if (val.HasValue)
        {
            string testCode = observation.TestCode.Value;
            if (testCode is "SYSBP" or "VSSBP" && (val.Value < 40.0 || val.Value > 300.0))
            {
                violations.Add(new EditCheckViolation("FATAL_SYSBP_RANGE", $"Systolic blood pressure {val.Value} is clinically implausible.", "FATAL", testCode, IsAutoDispatchEnabled: false));
            }
            else if (testCode is "DIABP" or "VSDPB" && (val.Value < 20.0 || val.Value > 200.0))
            {
                violations.Add(new EditCheckViolation("FATAL_DIABP_RANGE", $"Diastolic blood pressure {val.Value} is clinically implausible.", "FATAL", testCode, IsAutoDispatchEnabled: false));
            }
            else if (testCode is "PULSE" or "HR" or "VSHR" && (val.Value < 20.0 || val.Value > 250.0))
            {
                violations.Add(new EditCheckViolation("FATAL_PULSE_RANGE", $"Pulse rate {val.Value} is clinically implausible.", "FATAL", testCode, IsAutoDispatchEnabled: false));
            }
            else if (testCode is "TEMP" or "VSTEMP")
            {
                double tempCel = observation.NormalizedValue ??
                    (string.Equals(observation.Unit, "[degF]", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(observation.Unit, "[Fahr]", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(observation.Unit, "degF", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(observation.Unit, "fahr", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(observation.Unit, "f", StringComparison.OrdinalIgnoreCase)
                        ? (val.Value - 32.0) * 5.0 / 9.0
                        : val.Value);

                if (tempCel < 25.0 || tempCel > 45.0)
                {
                    violations.Add(new EditCheckViolation("FATAL_TEMP_RANGE", $"Body temperature {tempCel:F1} Cel is clinically implausible.", "FATAL", testCode, IsAutoDispatchEnabled: false));
                }
            }

            if (observation.AdditionalProperties != null)
            {
                if (observation.AdditionalProperties.ReferenceRangeLow.HasValue &&
                    val.Value < observation.AdditionalProperties.ReferenceRangeLow.Value)
                {
                    violations.Add(new EditCheckViolation(
                        "RULE_RANGE_LOW",
                        $"Value {val.Value} is below reference range low ({observation.AdditionalProperties.ReferenceRangeLow.Value}).",
                        "WARNING",
                        testCode,
                        IsAutoDispatchEnabled: true,
                        StandardTemplate: $"Value {val.Value} is below reference range low ({observation.AdditionalProperties.ReferenceRangeLow.Value}). Please verify or correct value."));
                }

                if (observation.AdditionalProperties.ReferenceRangeHigh.HasValue &&
                    val.Value > observation.AdditionalProperties.ReferenceRangeHigh.Value)
                {
                    violations.Add(new EditCheckViolation(
                        "RULE_RANGE_HIGH",
                        $"Value {val.Value} is above reference range high ({observation.AdditionalProperties.ReferenceRangeHigh.Value}).",
                        "WARNING",
                        testCode,
                        IsAutoDispatchEnabled: true,
                        StandardTemplate: $"Value {val.Value} is above reference range high ({observation.AdditionalProperties.ReferenceRangeHigh.Value}). Please verify or correct value."));
                }
            }
        }

        return violations;
    }

    public IReadOnlyList<EditCheckViolation> EvaluateBatchRules(BatchEvaluationContext context)
    {
        if (!context.IsConsented)
        {
            return Array.Empty<EditCheckViolation>();
        }

        var violations = new List<EditCheckViolation>();

        // Cross-field rule: Diastolic BP must not exceed Systolic BP in the same visit
        var sysList = context.GetObservationsByTestCode("SYSBP")
            .Concat(context.GetObservationsByTestCode("VSSBP"))
            .ToList();
        var diaList = context.GetObservationsByTestCode("DIABP")
            .Concat(context.GetObservationsByTestCode("VSDPB"))
            .ToList();

        foreach (var sys in sysList)
        {
            if (sys.VisitId is null || !sys.Value.HasValue) continue;

            var dia = diaList.FirstOrDefault(d => d.VisitId == sys.VisitId && d.Value.HasValue);
            if (dia != null && sys.Value.Value < dia.Value!.Value)
            {
                violations.Add(new EditCheckViolation(
                    "RULE_BP_PHYSIOLOGY",
                    $"Systolic BP ({sys.Value.Value}) cannot be lower than Diastolic BP ({dia.Value.Value}) on visit {sys.VisitId}.",
                    "QUERY",
                    TargetField: "SYSBP",
                    VisitId: sys.VisitId,
                    IsAutoDispatchEnabled: false));
            }
        }

        return violations;
    }

    private static string? GetNodeAttribute(object node, string attr) =>
        GetNodeRawAttribute(node, attr)?.ToString();

    private static object? GetNodeRawAttribute(object node, string attr)
    {
        if (node is null) return null;

        if (node is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty(attr, out var prop))
            {
                return prop.ValueKind switch
                {
                    JsonValueKind.String => prop.GetString(),
                    JsonValueKind.Number => prop.TryGetDecimal(out var dec) ? dec : prop.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop
                };
            }
            return null;
        }

        if (node is IReadOnlyDictionary<string, object?> dict && dict.TryGetValue(attr, out var val))
        {
            return val;
        }

        var propInfo = node.GetType().GetProperty(attr);
        return propInfo?.GetValue(node);
    }

    private static IReadOnlyList<object> GetNodeOperands(object node)
    {
        object? children = GetNodeRawAttribute(node, "operands") ?? GetNodeRawAttribute(node, "children") ?? GetNodeRawAttribute(node, "args");
        if (children is null) return Array.Empty<object>();

        if (children is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var list = new List<object>();
            foreach (var item in je.EnumerateArray()) list.Add(item);
            return list;
        }

        if (children is System.Collections.IEnumerable enumerable)
        {
            var list = new List<object>();
            foreach (var item in enumerable)
            {
                if (item != null) list.Add(item);
            }
            return list;
        }

        return Array.Empty<object>();
    }

    private static bool TryToDecimal(object value, out decimal result)
    {
        if (value is decimal dec) { result = dec; return true; }
        if (value is int i) { result = i; return true; }
        if (value is long l) { result = l; return true; }
        if (value is double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { result = 0m; return false; }
            try { result = Convert.ToDecimal(d); return true; } catch { result = 0m; return false; }
        }
        if (value is float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) { result = 0m; return false; }
            try { result = Convert.ToDecimal(f); return true; } catch { result = 0m; return false; }
        }

        if (value is JsonElement je && je.ValueKind == JsonValueKind.Number)
        {
            return je.TryGetDecimal(out result);
        }

        if (value is string s && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed))
        {
            result = parsed;
            return true;
        }

        result = 0m;
        return false;
    }

    private static string ExtractPathOrField(object node)
    {
        if (node is null) return string.Empty;
        if (node is string s) return s;

        if (node is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
            if (je.ValueKind == JsonValueKind.Object)
            {
                if (je.TryGetProperty("field_ref", out var fr))
                {
                    if (fr.ValueKind == JsonValueKind.Object && fr.TryGetProperty("field_id", out var fid) && fid.ValueKind == JsonValueKind.String)
                        return fid.GetString() ?? string.Empty;
                    if (fr.ValueKind == JsonValueKind.String)
                        return fr.GetString() ?? string.Empty;
                }
                if (je.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString() ?? string.Empty;
                if (je.TryGetProperty("field_id", out var fieldId) && fieldId.ValueKind == JsonValueKind.String)
                    return fieldId.GetString() ?? string.Empty;
                if (je.TryGetProperty("var", out var varProp) && varProp.ValueKind == JsonValueKind.String)
                    return varProp.GetString() ?? string.Empty;
            }
        }

        object? fieldRef = GetNodeRawAttribute(node, "field_ref");
        if (fieldRef != null)
        {
            string? id = GetNodeAttribute(fieldRef, "field_id") ?? GetNodeAttribute(fieldRef, "value");
            if (!string.IsNullOrEmpty(id)) return id;
        }

        return GetNodeAttribute(node, "value")
            ?? GetNodeAttribute(node, "field_id")
            ?? GetNodeAttribute(node, "var")
            ?? string.Empty;
    }
}
