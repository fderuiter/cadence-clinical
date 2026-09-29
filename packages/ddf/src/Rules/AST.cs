using System;
using System.Collections.Generic;

namespace Cadence.Ddf.Rules;

public record FieldReference(
    string FieldId,
    string? FormId = null,
    string? VisitId = null,
    string? VisitRelative = null
);

public record ExpressionNode(
    string Type,
    string? Operator = null,
    IReadOnlyList<ExpressionNode>? Operands = null,
    object? Value = null,
    FieldReference? FieldRef = null
)
{
    public void ValidateNode()
    {
        switch (Type)
        {
            case "constant":
                if (Value is null)
                {
                    throw new ArgumentException("Constant node must provide a 'Value'");
                }
                break;
            case "field_ref":
                if (FieldRef is null)
                {
                    throw new ArgumentException("Field reference node must provide 'FieldRef'");
                }
                break;
            case "logical":
                if (Operator is not ("and" or "or" or "not"))
                {
                    throw new ArgumentException($"Invalid logical operator: '{Operator}'");
                }
                if (Operands is null || Operands.Count == 0)
                {
                    throw new ArgumentException($"Logical node '{Operator}' requires operands");
                }
                if (Operator == "not" && Operands.Count != 1)
                {
                    throw new ArgumentException("Logical 'not' operator requires exactly 1 operand");
                }
                break;
            case "comparison":
                if (Operator is not ("==" or "!=" or "<" or "<=" or ">" or ">="))
                {
                    throw new ArgumentException($"Invalid comparison operator: '{Operator}'");
                }
                if (Operands is null || Operands.Count != 2)
                {
                    throw new ArgumentException($"Comparison operator '{Operator}' requires exactly 2 operands");
                }
                break;
            case "function":
                var validFuncs = new Dictionary<string, int>
                {
                    ["is_empty"] = 1,
                    ["empty"] = 1,
                    ["is_not_empty"] = 1,
                    ["indexed-repeat"] = 3,
                    ["sum"] = -1,
                    ["avg"] = -1,
                    ["min"] = -1,
                    ["max"] = -1,
                    ["count"] = -1
                };
                if (Operator is null || !validFuncs.TryGetValue(Operator, out int expectedArity))
                {
                    throw new ArgumentException($"Invalid function operator: '{Operator}'");
                }
                if (Operands is null || Operands.Count == 0)
                {
                    throw new ArgumentException($"Function node '{Operator}' requires operands");
                }
                if (expectedArity != -1 && Operands.Count != expectedArity)
                {
                    string suffix = expectedArity == 1 ? "operand" : "operands";
                    throw new ArgumentException($"Function '{Operator}' requires exactly {expectedArity} {suffix}");
                }
                break;
        }

        if (Operands != null)
        {
            foreach (var op in Operands)
            {
                op.ValidateNode();
            }
        }
    }
}

public record SkipLogicRule(
    string Id,
    string StudyId,
    ExpressionNode Condition,
    string Action,
    string TargetField,
    string? TargetForm = null,
    string? TargetGroup = null,
    int VersionIndex = 1,
    bool IsDeleted = false,
    string Type = "skip_logic"
);

public record ConstraintRule(
    string Id,
    string StudyId,
    ExpressionNode Condition,
    string TargetField,
    string QueryMessage,
    string? TargetForm = null,
    int VersionIndex = 1,
    bool IsDeleted = false,
    string Type = "constraint"
)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(QueryMessage))
        {
            throw new ArgumentException("Constraint rule requires a non-empty QueryMessage");
        }
    }
}

public record CrossFormCheckRule(
    string Id,
    string StudyId,
    ExpressionNode Condition,
    string QueryMessage,
    int VersionIndex = 1,
    bool IsDeleted = false,
    string Type = "cross_form_check"
)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(QueryMessage))
        {
            throw new ArgumentException("Cross-form check rule requires a non-empty QueryMessage");
        }
    }
}

public record CreateRuleRequest(
    string Type,
    ExpressionNode Condition,
    string? Action = null,
    string? TargetField = null,
    string? TargetForm = null,
    string? TargetGroup = null,
    string? QueryMessage = null
)
{
    public void Validate()
    {
        if (Type == "skip_logic")
        {
            if (string.IsNullOrWhiteSpace(TargetField))
                throw new ArgumentException("Skip logic rule requires 'TargetField'");
            if (Action is not ("show" or "hide"))
                throw new ArgumentException("Skip logic rule requires 'Action' ('show' or 'hide')");
        }
        else if (Type == "constraint")
        {
            if (string.IsNullOrWhiteSpace(TargetField))
                throw new ArgumentException("Constraint rule requires 'TargetField'");
            if (string.IsNullOrWhiteSpace(QueryMessage))
                throw new ArgumentException("Constraint rule requires 'QueryMessage'");
        }
        else if (Type == "cross_form_check" && string.IsNullOrWhiteSpace(QueryMessage))
        {
            throw new ArgumentException("Cross-form check rule requires 'QueryMessage'");
        }
    }
}

public static class AstExtensions
{
    public static List<FieldReference> ExtractFieldReferences(ExpressionNode node)
    {
        var refs = new List<FieldReference>();
        if (node.Type == "field_ref" && node.FieldRef != null)
        {
            refs.Add(node.FieldRef);
        }
        if (node.Operands != null)
        {
            foreach (var op in node.Operands)
            {
                refs.AddRange(ExtractFieldReferences(op));
            }
        }
        return refs;
    }
}
