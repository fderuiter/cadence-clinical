using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cadence.Ddf.Rules;

public static partial class XPathCompiler
{
    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_]*$")]
    private static partial Regex ValidIdentifierRegex();

    [GeneratedRegex("^\\d")]
    private static partial Regex LeadingDigitRegex();

    [GeneratedRegex("^[a-zA-Z_]")]
    private static partial Regex ValidStartRegex();

    public static string SanitizeIdentifier(object? rawId)
    {
        string? idStr = rawId?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(idStr))
        {
            return $"item_{Guid.NewGuid():N}"[..13];
        }

        if (ValidIdentifierRegex().IsMatch(idStr))
        {
            return idStr;
        }

        bool startsWithDigit = LeadingDigitRegex().IsMatch(idStr);
        var sb = new StringBuilder();

        foreach (char c in idStr)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
            else if (c == ' ')
            {
                sb.Append('_');
            }
            else
            {
                sb.Append($"_{((int)c):x2}");
            }
        }

        string result = sb.ToString();

        if (startsWithDigit || !ValidStartRegex().IsMatch(result))
        {
            result = $"item_{result}";
        }

        return result;
    }

    public static string? ExtractAppearance(IReadOnlyDictionary<string, object?>? item)
    {
        if (item is null) return null;

        string[] keys = ["cols", "column_span", "span"];
        foreach (string k in keys)
        {
            if (item.TryGetValue(k, out object? val) && val != null)
            {
                string strVal = val.ToString() ?? "";
                if (strVal is "1" or "2" or "3" or "4")
                {
                    return $"w{strVal}";
                }
            }
        }

        string[] subKeys = ["layout", "grid"];
        foreach (string sub in subKeys)
        {
            if (item.TryGetValue(sub, out object? subObj) && subObj is IReadOnlyDictionary<string, object?> subDict)
            {
                foreach (string k in keys)
                {
                    if (subDict.TryGetValue(k, out object? val) && val != null)
                    {
                        string strVal = val.ToString() ?? "";
                        if (strVal is "1" or "2" or "3" or "4")
                        {
                            return $"w{strVal}";
                        }
                    }
                }
            }
        }

        return null;
    }

    public static string CompileConditionToXPath(object? node, string rootPrefix = "")
    {
        if (node is null) return string.Empty;

        if (node is ExpressionNode exprNode)
        {
            return CompileExpressionNode(exprNode, rootPrefix);
        }

        if (node is JsonElement element)
        {
            return CompileJsonElement(element, rootPrefix);
        }

        if (node is IReadOnlyDictionary<string, object?> dict)
        {
            return CompileDictionary(dict, rootPrefix);
        }

        return string.Empty;
    }

    public static string CompileToXPath(object? node, string rootPrefix = "/clinical_data")
    {
        return CompileConditionToXPath(node, rootPrefix);
    }

    private static string CompileExpressionNode(ExpressionNode node, string rootPrefix)
    {
        string nodeType = node.Type;
        if (string.IsNullOrEmpty(nodeType)) return string.Empty;

        if (nodeType == "constant")
        {
            object? val = node.Value;
            if (val is true || (val is JsonElement jeTrue && jeTrue.ValueKind == JsonValueKind.True)) return "true()";
            if (val is false || (val is JsonElement jeFalse && jeFalse.ValueKind == JsonValueKind.False)) return "false()";
            if (val is null) return string.Empty;
            if (val is string s) return $"'{s}'";
            if (val is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.String => $"'{je.GetString()}'",
                    JsonValueKind.True => "true()",
                    JsonValueKind.False => "false()",
                    _ => je.ToString()
                };
            }
            return val.ToString() ?? string.Empty;
        }

        if (nodeType == "field_ref")
        {
            var refObj = node.FieldRef;
            if (refObj is null) return string.Empty;
            string fieldId = refObj.FieldId;
            if (string.IsNullOrEmpty(fieldId)) return string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(rootPrefix))
            {
                parts.Add(rootPrefix.Trim('/'));
            }

            if (!string.IsNullOrEmpty(refObj.VisitRelative))
            {
                parts.Add(refObj.VisitRelative);
            }
            else if (!string.IsNullOrEmpty(refObj.VisitId))
            {
                parts.Add(refObj.VisitId);
            }

            if (!string.IsNullOrEmpty(refObj.FormId))
            {
                parts.Add(refObj.FormId);
            }

            string sanitized = string.IsNullOrEmpty(rootPrefix) ? SanitizeIdentifier(fieldId) : fieldId;
            parts.Add(sanitized);

            return $"/{string.Join("/", parts)}";
        }

        if (nodeType == "logical")
        {
            string? op = node.Operator;
            var operands = node.Operands;
            if (string.IsNullOrEmpty(op) || operands is null || operands.Count == 0) return string.Empty;

            if (op == "not")
            {
                return $"not({CompileExpressionNode(operands[0], rootPrefix)})";
            }

            string opFmt = !string.IsNullOrEmpty(rootPrefix) ? op.ToUpperInvariant() : op.ToLowerInvariant();
            string opJoin = $" {opFmt} ";
            var compiledOps = new List<string>();
            foreach (var operand in operands)
            {
                compiledOps.Add(CompileExpressionNode(operand, rootPrefix));
            }
            return $"({string.Join(opJoin, compiledOps)})";
        }

        if (nodeType == "comparison")
        {
            string? op = node.Operator;
            var operands = node.Operands;
            if (string.IsNullOrEmpty(op) || operands is null || operands.Count < 2) return string.Empty;

            string opSymbol = op == "==" ? "=" : op;
            string left = CompileExpressionNode(operands[0], rootPrefix);
            string right = CompileExpressionNode(operands[1], rootPrefix);
            return $"({left} {opSymbol} {right})";
        }

        if (nodeType == "function")
        {
            string? op = node.Operator;
            var operands = node.Operands;
            if (string.IsNullOrEmpty(op) || operands is null || operands.Count == 0) return string.Empty;

            if (op is "is_empty" or "empty")
            {
                if (operands.Count != 1) throw new ArgumentException($"Function '{op}' requires exactly 1 operand");
                return $"empty({CompileExpressionNode(operands[0], rootPrefix)})";
            }

            if (op == "is_not_empty")
            {
                if (operands.Count != 1) throw new ArgumentException($"Function '{op}' requires exactly 1 operand");
                return $"not(empty({CompileExpressionNode(operands[0], rootPrefix)}))";
            }

            if (op == "indexed-repeat")
            {
                if (operands.Count != 3) throw new ArgumentException($"Function '{op}' requires exactly 3 operands");
                var compiledRepeat = new List<string>();
                foreach (var o in operands) compiledRepeat.Add(CompileExpressionNode(o, rootPrefix));
                return $"indexed-repeat({string.Join(", ", compiledRepeat)})";
            }

            var compiledList = new List<string>();
            foreach (var o in operands) compiledList.Add(CompileExpressionNode(o, rootPrefix));
            return $"{op}({string.Join(", ", compiledList)})";
        }

        return string.Empty;
    }

    private static string CompileJsonElement(JsonElement element, string rootPrefix)
    {
        if (element.ValueKind != JsonValueKind.Object) return string.Empty;

        if (!element.TryGetProperty("type", out var typeProp)) return string.Empty;
        string nodeType = typeProp.GetString() ?? string.Empty;

        if (nodeType == "constant")
        {
            if (element.TryGetProperty("value", out var valProp))
            {
                return valProp.ValueKind switch
                {
                    JsonValueKind.True => "true()",
                    JsonValueKind.False => "false()",
                    JsonValueKind.String => $"'{valProp.GetString()}'",
                    JsonValueKind.Null => string.Empty,
                    _ => valProp.ToString()
                };
            }
            return string.Empty;
        }

        if (nodeType == "field_ref")
        {
            if (!element.TryGetProperty("field_ref", out var refProp)) return string.Empty;
            string fieldId = refProp.TryGetProperty("field_id", out var fid) ? fid.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(fieldId)) return string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(rootPrefix)) parts.Add(rootPrefix.Trim('/'));

            if (refProp.TryGetProperty("visit_relative", out var vr) && !string.IsNullOrEmpty(vr.GetString()))
            {
                parts.Add(vr.GetString()!);
            }
            else if (refProp.TryGetProperty("visit_id", out var vi) && !string.IsNullOrEmpty(vi.GetString()))
            {
                parts.Add(vi.GetString()!);
            }

            if (refProp.TryGetProperty("form_id", out var fi) && !string.IsNullOrEmpty(fi.GetString()))
            {
                parts.Add(fi.GetString()!);
            }

            string sanitized = string.IsNullOrEmpty(rootPrefix) ? SanitizeIdentifier(fieldId) : fieldId;
            parts.Add(sanitized);

            return $"/{string.Join("/", parts)}";
        }

        if (nodeType == "logical")
        {
            string op = element.TryGetProperty("operator", out var opP) ? opP.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(op) || !element.TryGetProperty("operands", out var opsP) || opsP.ValueKind != JsonValueKind.Array) return string.Empty;

            if (op == "not")
            {
                var enumerator = opsP.EnumerateArray();
                enumerator.MoveNext();
                return $"not({CompileJsonElement(enumerator.Current, rootPrefix)})";
            }

            string opFmt = !string.IsNullOrEmpty(rootPrefix) ? op.ToUpperInvariant() : op.ToLowerInvariant();
            string opJoin = $" {opFmt} ";
            var compiledOps = new List<string>();
            foreach (var item in opsP.EnumerateArray())
            {
                compiledOps.Add(CompileJsonElement(item, rootPrefix));
            }
            return $"({string.Join(opJoin, compiledOps)})";
        }

        if (nodeType == "comparison")
        {
            string op = element.TryGetProperty("operator", out var opP) ? opP.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(op) || !element.TryGetProperty("operands", out var opsP) || opsP.ValueKind != JsonValueKind.Array) return string.Empty;

            string opSymbol = op == "==" ? "=" : op;
            var array = opsP.EnumerateArray();
            array.MoveNext();
            string left = CompileJsonElement(array.Current, rootPrefix);
            array.MoveNext();
            string right = CompileJsonElement(array.Current, rootPrefix);
            return $"({left} {opSymbol} {right})";
        }

        if (nodeType == "function")
        {
            string op = element.TryGetProperty("operator", out var opP) ? opP.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(op) || !element.TryGetProperty("operands", out var opsP) || opsP.ValueKind != JsonValueKind.Array) return string.Empty;

            var items = new List<JsonElement>();
            foreach (var item in opsP.EnumerateArray()) items.Add(item);

            if (op is "is_empty" or "empty")
            {
                if (items.Count != 1) throw new ArgumentException($"Function '{op}' requires exactly 1 operand");
                return $"empty({CompileJsonElement(items[0], rootPrefix)})";
            }

            if (op == "is_not_empty")
            {
                if (items.Count != 1) throw new ArgumentException($"Function '{op}' requires exactly 1 operand");
                return $"not(empty({CompileJsonElement(items[0], rootPrefix)}))";
            }

            if (op == "indexed-repeat")
            {
                if (items.Count != 3) throw new ArgumentException($"Function '{op}' requires exactly 3 operands");
                var compiledRepeat = new List<string>();
                foreach (var o in items) compiledRepeat.Add(CompileJsonElement(o, rootPrefix));
                return $"indexed-repeat({string.Join(", ", compiledRepeat)})";
            }

            var compiledList = new List<string>();
            foreach (var o in items) compiledList.Add(CompileJsonElement(o, rootPrefix));
            return $"{op}({string.Join(", ", compiledList)})";
        }

        return string.Empty;
    }

    private static string CompileDictionary(IReadOnlyDictionary<string, object?> dict, string rootPrefix)
    {
        if (!dict.TryGetValue("type", out object? typeObj) || typeObj is null) return string.Empty;
        string nodeType = typeObj.ToString() ?? string.Empty;

        if (nodeType == "constant")
        {
            dict.TryGetValue("value", out object? val);
            if (val is true) return "true()";
            if (val is false) return "false()";
            if (val is null) return string.Empty;
            if (val is string s) return $"'{s}'";
            return val.ToString() ?? string.Empty;
        }

        if (nodeType == "field_ref")
        {
            if (!dict.TryGetValue("field_ref", out object? refVal) || refVal is null) return string.Empty;
            string fieldId = string.Empty;
            string? formId = null, visitId = null, visitRel = null;

            if (refVal is FieldReference fr)
            {
                fieldId = fr.FieldId;
                formId = fr.FormId;
                visitId = fr.VisitId;
                visitRel = fr.VisitRelative;
            }
            else if (refVal is IReadOnlyDictionary<string, object?> refDict)
            {
                fieldId = refDict.TryGetValue("field_id", out var fid) ? fid?.ToString() ?? "" : "";
                formId = refDict.TryGetValue("form_id", out var fmi) ? fmi?.ToString() : null;
                visitId = refDict.TryGetValue("visit_id", out var vi) ? vi?.ToString() : null;
                visitRel = refDict.TryGetValue("visit_relative", out var vr) ? vr?.ToString() : null;
            }

            if (string.IsNullOrEmpty(fieldId)) return string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(rootPrefix)) parts.Add(rootPrefix.Trim('/'));

            if (!string.IsNullOrEmpty(visitRel)) parts.Add(visitRel);
            else if (!string.IsNullOrEmpty(visitId)) parts.Add(visitId);

            if (!string.IsNullOrEmpty(formId)) parts.Add(formId);

            string sanitized = string.IsNullOrEmpty(rootPrefix) ? SanitizeIdentifier(fieldId) : fieldId;
            parts.Add(sanitized);

            return $"/{string.Join("/", parts)}";
        }

        if (nodeType == "logical")
        {
            string op = dict.TryGetValue("operator", out var opVal) ? opVal?.ToString() ?? "" : "";
            if (string.IsNullOrEmpty(op) || !dict.TryGetValue("operands", out var opsVal) || opsVal is null) return string.Empty;

            var opsList = GetOperandsList(opsVal);
            if (opsList.Count == 0) return string.Empty;

            if (op == "not")
            {
                return $"not({CompileConditionToXPath(opsList[0], rootPrefix)})";
            }

            string opFmt = !string.IsNullOrEmpty(rootPrefix) ? op.ToUpperInvariant() : op.ToLowerInvariant();
            string opJoin = $" {opFmt} ";
            var compiledOps = new List<string>();
            foreach (var item in opsList) compiledOps.Add(CompileConditionToXPath(item, rootPrefix));
            return $"({string.Join(opJoin, compiledOps)})";
        }

        if (nodeType == "comparison")
        {
            string op = dict.TryGetValue("operator", out var opVal) ? opVal?.ToString() ?? "" : "";
            if (string.IsNullOrEmpty(op) || !dict.TryGetValue("operands", out var opsVal) || opsVal is null) return string.Empty;

            var opsList = GetOperandsList(opsVal);
            if (opsList.Count < 2) return string.Empty;

            string opSymbol = op == "==" ? "=" : op;
            string left = CompileConditionToXPath(opsList[0], rootPrefix);
            string right = CompileConditionToXPath(opsList[1], rootPrefix);
            return $"({left} {opSymbol} {right})";
        }

        if (nodeType == "function")
        {
            string op = dict.TryGetValue("operator", out var opVal) ? opVal?.ToString() ?? "" : "";
            if (string.IsNullOrEmpty(op) || !dict.TryGetValue("operands", out var opsVal) || opsVal is null) return string.Empty;

            var opsList = GetOperandsList(opsVal);

            if (op is "is_empty" or "empty")
            {
                if (opsList.Count != 1) throw new ArgumentException($"Function '{op}' requires exactly 1 operand");
                return $"empty({CompileConditionToXPath(opsList[0], rootPrefix)})";
            }

            if (op == "is_not_empty")
            {
                if (opsList.Count != 1) throw new ArgumentException($"Function '{op}' requires exactly 1 operand");
                return $"not(empty({CompileConditionToXPath(opsList[0], rootPrefix)}))";
            }

            if (op == "indexed-repeat")
            {
                if (opsList.Count != 3) throw new ArgumentException($"Function '{op}' requires exactly 3 operands");
                var compiledRepeat = new List<string>();
                foreach (var o in opsList) compiledRepeat.Add(CompileConditionToXPath(o, rootPrefix));
                return $"indexed-repeat({string.Join(", ", compiledRepeat)})";
            }

            var compiledList = new List<string>();
            foreach (var o in opsList) compiledList.Add(CompileConditionToXPath(o, rootPrefix));
            return $"{op}({string.Join(", ", compiledList)})";
        }

        return string.Empty;
    }

    private static List<object> GetOperandsList(object opsVal)
    {
        var list = new List<object>();
        if (opsVal is System.Collections.IEnumerable enumOps && opsVal is not string)
        {
            foreach (var item in enumOps)
            {
                if (item != null) list.Add(item);
            }
        }
        return list;
    }
}
