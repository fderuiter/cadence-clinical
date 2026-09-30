using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Rules.Expressions;

/// <summary>Reads the variables of the record a rule is checking.</summary>
public interface IRuleVariables
{
    /// <summary>The variable's value; <see cref="RuleValue.Null"/> when the record does not have it.</summary>
    RuleValue this[string name] { get; }
}

/// <summary>
/// Compiles a <see cref="RuleExpression"/> into a delegate. The semantics are fixed here and are the contract a
/// replacement engine (the Rust runtime, spec §10.1) must reproduce:
/// <list type="bullet">
/// <item><c>and</c>, <c>or</c>, <c>not</c> use Kleene logic: an operand that is not a boolean is unknown (null);
/// <c>and</c> is false if any operand is false, <c>or</c> true if any is true, otherwise unknown if any is unknown.</item>
/// <item>Comparisons are never unknown. Null equals only null, and an ordering comparison with null is false. Two
/// values that both have a number (<see cref="RuleValue.TryGetNumber"/>) compare as numbers; two booleans compare for
/// equality only (ordering is false); anything else compares as invariant text, ordinally.</item>
/// <item>Arithmetic needs two numbers and gives unknown otherwise, on overflow and on division by zero.</item>
/// <item><c>is_empty</c> is true for null and blank text; <c>in</c> is true when the first argument <c>==</c> any
/// other; <c>length</c> counts Unicode characters of text and is unknown for anything else.</item>
/// </list>
/// Nothing reads the clock, the culture or any state outside the record, so the same rule on the same record always
/// gives the same result (K-RULE-4).
/// </summary>
public static class RuleExpressionCompiler
{
    /// <summary>Compiles <paramref name="expression"/>.</summary>
    public static Func<IRuleVariables, RuleValue> Compile(RuleExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression switch
        {
            LiteralExpression literal => CompileLiteral(literal.Value),
            VariableExpression variable => CompileVariable(variable.Name),
            NotExpression not => CompileNot(Compile(not.Operand)),
            LogicalExpression logical => CompileLogical(logical.Operator, [.. logical.Operands.Select(Compile)]),
            ComparisonExpression comparison => CompileComparison(comparison.Operator, Compile(comparison.Left), Compile(comparison.Right)),
            ArithmeticExpression arithmetic => CompileArithmetic(arithmetic.Operator, Compile(arithmetic.Left), Compile(arithmetic.Right)),
            FunctionExpression function => CompileFunction(function.Function, [.. function.Arguments.Select(Compile)]),
            _ => throw new ArgumentException($"Unknown expression type {expression.GetType().Name}.", nameof(expression)),
        };
    }

    /// <summary>Evaluates <paramref name="expression"/> once, without keeping the compiled form.</summary>
    public static RuleValue Evaluate(RuleExpression expression, IRuleVariables variables) => Compile(expression)(variables);

    /// <summary>The result of <c>left op right</c> under the comparison rules above.</summary>
    public static bool Compare(ComparisonOperator op, RuleValue left, RuleValue right)
    {
        if (left.IsNull || right.IsNull)
        {
            var bothNull = left.IsNull && right.IsNull;
            return op switch
            {
                ComparisonOperator.Equal => bothNull,
                ComparisonOperator.NotEqual => !bothNull,
                _ => false,
            };
        }

        if (left.TryGetNumber(out var l) && right.TryGetNumber(out var r))
        {
            return Ordered(op, l.CompareTo(r));
        }

        if (left.TryGetBoolean(out var lb) && right.TryGetBoolean(out var rb))
        {
            return op switch
            {
                ComparisonOperator.Equal => lb == rb,
                ComparisonOperator.NotEqual => lb != rb,
                _ => false,
            };
        }

        return Ordered(op, string.CompareOrdinal(left.ToString(), right.ToString()));
    }

    private static bool Ordered(ComparisonOperator op, int comparison) => op switch
    {
        ComparisonOperator.Equal => comparison == 0,
        ComparisonOperator.NotEqual => comparison != 0,
        ComparisonOperator.Less => comparison < 0,
        ComparisonOperator.LessOrEqual => comparison <= 0,
        ComparisonOperator.Greater => comparison > 0,
        _ => comparison >= 0,
    };

    private static Func<IRuleVariables, RuleValue> CompileLiteral(RuleValue value) => _ => value;

    private static Func<IRuleVariables, RuleValue> CompileVariable(string name) => variables => variables[name];

    private static Func<IRuleVariables, RuleValue> CompileNot(Func<IRuleVariables, RuleValue> operand) => variables =>
        operand(variables).TryGetBoolean(out var b) ? RuleValue.FromBoolean(!b) : RuleValue.Null;

    private static Func<IRuleVariables, RuleValue> CompileLogical(LogicalOperator op, Func<IRuleVariables, RuleValue>[] operands)
    {
        // "and" stops at false, "or" at true.
        var decisive = op == LogicalOperator.Or;
        return variables =>
        {
            var unknown = false;
            foreach (var operand in operands)
            {
                if (!operand(variables).TryGetBoolean(out var b))
                {
                    unknown = true;
                }
                else if (b == decisive)
                {
                    return RuleValue.FromBoolean(decisive);
                }
            }

            return unknown ? RuleValue.Null : RuleValue.FromBoolean(!decisive);
        };
    }

    private static Func<IRuleVariables, RuleValue> CompileComparison(
        ComparisonOperator op, Func<IRuleVariables, RuleValue> left, Func<IRuleVariables, RuleValue> right) =>
        variables => RuleValue.FromBoolean(Compare(op, left(variables), right(variables)));

    private static Func<IRuleVariables, RuleValue> CompileArithmetic(
        ArithmeticOperator op, Func<IRuleVariables, RuleValue> left, Func<IRuleVariables, RuleValue> right) => variables =>
    {
        if (!left(variables).TryGetNumber(out var l) || !right(variables).TryGetNumber(out var r))
        {
            return RuleValue.Null;
        }

        try
        {
            return op switch
            {
                ArithmeticOperator.Add => RuleValue.FromNumber(l + r),
                ArithmeticOperator.Subtract => RuleValue.FromNumber(l - r),
                ArithmeticOperator.Multiply => RuleValue.FromNumber(l * r),
                _ => r == 0m ? RuleValue.Null : RuleValue.FromNumber(l / r),
            };
        }
        catch (OverflowException)
        {
            return RuleValue.Null;
        }
    };

    private static Func<IRuleVariables, RuleValue> CompileFunction(RuleFunction function, Func<IRuleVariables, RuleValue>[] args) => function switch
    {
        RuleFunction.IsEmpty => variables => RuleValue.FromBoolean(IsEmpty(args[0](variables))),
        RuleFunction.IsNotEmpty => variables => RuleValue.FromBoolean(!IsEmpty(args[0](variables))),
        RuleFunction.In => variables => RuleValue.FromBoolean(In(args, variables)),
        RuleFunction.NotIn => variables => RuleValue.FromBoolean(!In(args, variables)),
        _ => variables => args[0](variables).TryGetText(out var text)
            ? RuleValue.FromNumber(text.EnumerateRunes().Count())
            : RuleValue.Null,
    };

    private static bool IsEmpty(RuleValue value) =>
        value.IsNull || (value.TryGetText(out var text) && string.IsNullOrWhiteSpace(text));

    private static bool In(Func<IRuleVariables, RuleValue>[] args, IRuleVariables variables)
    {
        var value = args[0](variables);
        for (var i = 1; i < args.Length; i++)
        {
            if (Compare(ComparisonOperator.Equal, value, args[i](variables)))
            {
                return true;
            }
        }

        return false;
    }
}
