using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Cadence.Rules.Expressions;

namespace Cadence.Ddf;

/// <summary>
/// Compiles a rule expression (the one rule language, K-RULE-3) into an XPath 1.0 expression for an OpenRosa
/// <c>relevant</c> or <c>constraint</c> attribute. Variables name form items and become their instance paths.
/// </summary>
/// <remarks>
/// <para>An empty OpenRosa field holds the empty string where the rule language has null, so:</para>
/// <list type="bullet">
/// <item><c>x == null</c> becomes <c>string-length(x) = 0</c> and <c>x != null</c> becomes
/// <c>string-length(x) &gt; 0</c>; an ordering comparison with null is <c>false()</c>, as in the evaluator.</item>
/// <item><c>is_empty(x)</c> becomes <c>normalize-space(x) = ''</c> (null or blank text).</item>
/// <item>Comparisons with an empty field are false in XPath, as in the evaluator; <c>and</c>, <c>or</c> and
/// <c>not</c> are two-valued in XPath, so a condition that is unknown in the evaluator (for example arithmetic on an
/// empty field) is false in the form.</item>
/// </list>
/// <para>Text literals are quoted safely, with <c>concat()</c> when they contain both quote characters, so a literal
/// can never change the structure of the expression.</para>
/// </remarks>
public static class RuleXPathCompiler
{
    /// <summary>
    /// Compiles <paramref name="expression"/>. <paramref name="resolve"/> maps a variable name to its XPath node path,
    /// or returns null when no item has that name; such names are returned in <c>Unresolved</c> and the XPath is only
    /// usable when that list is empty.
    /// </summary>
    public static (string XPath, IReadOnlyList<string> Unresolved) Compile(RuleExpression expression, Func<string, string?> resolve)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resolve);
        var unresolved = new List<string>();
        string xpath = Emit(expression, resolve, unresolved);
        return (xpath, unresolved);
    }

    /// <summary>An XPath 1.0 string literal for <paramref name="text"/>.</summary>
    public static string Literal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains('\''))
        {
            return $"'{text}'";
        }

        if (!text.Contains('"'))
        {
            return $"\"{text}\"";
        }

        // XPath 1.0 has no escape character: split on the single quotes and join the parts with concat().
        var parts = text.Split('\'').Select(part => $"'{part}'");
        return $"concat({string.Join(", \"'\", ", parts)})";
    }

    private static string Emit(RuleExpression expression, Func<string, string?> resolve, List<string> unresolved) => expression switch
    {
        LiteralExpression literal => EmitLiteral(literal.Value),
        VariableExpression variable => EmitVariable(variable.Name, resolve, unresolved),
        NotExpression not => $"not({Emit(not.Operand, resolve, unresolved)})",
        LogicalExpression logical => Join(logical.Operator == LogicalOperator.And ? " and " : " or ", logical.Operands, resolve, unresolved),
        ComparisonExpression comparison => EmitComparison(comparison, resolve, unresolved),
        ArithmeticExpression arithmetic =>
            $"({Emit(arithmetic.Left, resolve, unresolved)} {ArithmeticSymbol(arithmetic.Operator)} {Emit(arithmetic.Right, resolve, unresolved)})",
        FunctionExpression function => EmitFunction(function, resolve, unresolved),
        _ => throw new NotSupportedException($"The expression node {expression.GetType().Name} has no XPath form."),
    };

    private static string EmitLiteral(RuleValue value)
    {
        if (value.TryGetBoolean(out bool boolean))
        {
            return boolean ? "true()" : "false()";
        }

        if (value.TryGetNumber(out decimal number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return value.TryGetText(out string text) ? Literal(text) : "''";
    }

    private static string EmitVariable(string name, Func<string, string?> resolve, List<string> unresolved)
    {
        if (resolve(name) is { } path)
        {
            return path;
        }

        if (!unresolved.Contains(name, StringComparer.Ordinal))
        {
            unresolved.Add(name);
        }

        return "''";
    }

    private static string EmitComparison(ComparisonExpression comparison, Func<string, string?> resolve, List<string> unresolved)
    {
        bool leftNull = comparison.Left is LiteralExpression { Value.IsNull: true };
        bool rightNull = comparison.Right is LiteralExpression { Value.IsNull: true };
        if (leftNull || rightNull)
        {
            if (leftNull && rightNull)
            {
                return comparison.Operator == ComparisonOperator.Equal ? "true()" : "false()";
            }

            string other = Emit(leftNull ? comparison.Right : comparison.Left, resolve, unresolved);
            return comparison.Operator switch
            {
                ComparisonOperator.Equal => $"(string-length({other}) = 0)",
                ComparisonOperator.NotEqual => $"(string-length({other}) > 0)",
                _ => "false()",
            };
        }

        return $"({Emit(comparison.Left, resolve, unresolved)} {ComparisonSymbol(comparison.Operator)} {Emit(comparison.Right, resolve, unresolved)})";
    }

    private static string EmitFunction(FunctionExpression function, Func<string, string?> resolve, List<string> unresolved)
    {
        var arguments = function.Arguments.Select(a => Emit(a, resolve, unresolved)).ToList();
        return function.Function switch
        {
            RuleFunction.IsEmpty => $"(normalize-space({arguments[0]}) = '')",
            RuleFunction.IsNotEmpty => $"(normalize-space({arguments[0]}) != '')",
            RuleFunction.In => $"({string.Join(" or ", arguments.Skip(1).Select(a => $"{arguments[0]} = {a}"))})",
            RuleFunction.NotIn => $"not({string.Join(" or ", arguments.Skip(1).Select(a => $"{arguments[0]} = {a}"))})",
            RuleFunction.Length => $"string-length({arguments[0]})",
            _ => throw new NotSupportedException($"The function {function.Function} has no XPath form."),
        };
    }

    private static string Join(string separator, IReadOnlyList<RuleExpression> operands, Func<string, string?> resolve, List<string> unresolved) =>
        $"({string.Join(separator, operands.Select(o => Emit(o, resolve, unresolved)))})";

    private static string ComparisonSymbol(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => "=",
        ComparisonOperator.NotEqual => "!=",
        ComparisonOperator.Less => "<",
        ComparisonOperator.LessOrEqual => "<=",
        ComparisonOperator.Greater => ">",
        ComparisonOperator.GreaterOrEqual => ">=",
        _ => throw new NotSupportedException($"The comparison {op} has no XPath form."),
    };

    private static string ArithmeticSymbol(ArithmeticOperator op) => op switch
    {
        ArithmeticOperator.Add => "+",
        ArithmeticOperator.Subtract => "-",
        ArithmeticOperator.Multiply => "*",
        ArithmeticOperator.Divide => "div",
        _ => throw new NotSupportedException($"The arithmetic operator {op} has no XPath form."),
    };
}
