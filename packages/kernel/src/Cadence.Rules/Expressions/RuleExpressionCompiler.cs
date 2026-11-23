using System;
using System.Collections.Generic;

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
            LogicalExpression logical => CompileLogical(logical.Operator, CompileAll(logical.Operands)),
            ComparisonExpression comparison => CompileComparison(comparison.Operator, comparison.Left, comparison.Right),
            ArithmeticExpression arithmetic => CompileArithmetic(arithmetic.Operator, Compile(arithmetic.Left), Compile(arithmetic.Right)),
            FunctionExpression function => CompileFunction(function.Function, function.Arguments),
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

        if (left.CanBeNumber && right.CanBeNumber && left.TryGetNumber(out var l) && right.TryGetNumber(out var r))
        {
            return Ordered(op, l.CompareTo(r));
        }

        if (left.TryGetBoolean(out var lb) && right.TryGetBoolean(out var rb))
        {
            return CompareBooleans(op, lb, rb);
        }

        return Ordered(op, string.CompareOrdinal(left.ToString(), right.ToString()));
    }

    private static bool CompareBooleans(ComparisonOperator op, bool left, bool right) => op switch
    {
        ComparisonOperator.Equal => left == right,
        ComparisonOperator.NotEqual => left != right,
        _ => false,
    };

    private static bool Ordered(ComparisonOperator op, int comparison) => op switch
    {
        ComparisonOperator.Equal => comparison == 0,
        ComparisonOperator.NotEqual => comparison != 0,
        ComparisonOperator.Less => comparison < 0,
        ComparisonOperator.LessOrEqual => comparison <= 0,
        ComparisonOperator.Greater => comparison > 0,
        _ => comparison >= 0,
    };

    private static ComparisonOperator Flip(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Less => ComparisonOperator.Greater,
        ComparisonOperator.LessOrEqual => ComparisonOperator.GreaterOrEqual,
        ComparisonOperator.Greater => ComparisonOperator.Less,
        ComparisonOperator.GreaterOrEqual => ComparisonOperator.LessOrEqual,
        _ => op,
    };

    private static Func<IRuleVariables, RuleValue>[] CompileAll(IReadOnlyList<RuleExpression> expressions)
    {
        var compiled = new Func<IRuleVariables, RuleValue>[expressions.Count];
        for (var i = 0; i < expressions.Count; i++)
        {
            compiled[i] = Compile(expressions[i]);
        }

        return compiled;
    }

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
            for (var i = 0; i < operands.Length; i++)
            {
                if (!operands[i](variables).TryGetBoolean(out var b))
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
        ComparisonOperator op, RuleExpression leftExpression, RuleExpression rightExpression)
    {
        if (leftExpression is LiteralExpression leftLiteral && rightExpression is LiteralExpression rightLiteral)
        {
            var constant = RuleValue.FromBoolean(Compare(op, leftLiteral.Value, rightLiteral.Value));
            return _ => constant;
        }

        if (rightExpression is LiteralExpression rightConst)
        {
            return CompileComparisonWithLiteral(op, Compile(leftExpression), ClassifyLiteral(rightConst.Value));
        }

        if (leftExpression is LiteralExpression leftConst)
        {
            return CompileComparisonWithLiteral(Flip(op), Compile(rightExpression), ClassifyLiteral(leftConst.Value));
        }

        var left = Compile(leftExpression);
        var right = Compile(rightExpression);
        return variables => RuleValue.FromBoolean(Compare(op, left(variables), right(variables)));
    }

    private static Func<IRuleVariables, RuleValue> CompileComparisonWithLiteral(
        ComparisonOperator op, Func<IRuleVariables, RuleValue> left, ClassifiedOperand right)
    {
        switch (right.Kind)
        {
            case OperandKind.NullLiteral:
                return op switch
                {
                    ComparisonOperator.Equal => variables => RuleValue.FromBoolean(left(variables).IsNull),
                    ComparisonOperator.NotEqual => variables => RuleValue.FromBoolean(!left(variables).IsNull),
                    _ => variables =>
                    {
                        _ = left(variables);
                        return RuleValue.False;
                    }
                    ,
                };
            case OperandKind.BooleanLiteral:
                var rightBool = right.Boolean;
                var rightBoolText = right.Text;
                return variables => RuleValue.FromBoolean(CompareWithBooleanLiteral(op, left(variables), rightBool, rightBoolText));
            case OperandKind.NumericLiteral:
                var rightNumber = right.Number;
                var rightNumberText = right.Text;
                return variables => RuleValue.FromBoolean(CompareWithNumericLiteral(op, left(variables), rightNumber, rightNumberText));
            default:
                var rightText = right.Text;
                return variables => RuleValue.FromBoolean(CompareWithNonNumericTextLiteral(op, left(variables), rightText));
        }
    }

    private static bool CompareWithBooleanLiteral(ComparisonOperator op, RuleValue left, bool rightBool, string rightText)
    {
        if (left.IsNull)
        {
            return op == ComparisonOperator.NotEqual;
        }

        if (left.TryGetBoolean(out var leftBool))
        {
            return CompareBooleans(op, leftBool, rightBool);
        }

        return Ordered(op, string.CompareOrdinal(left.ToString(), rightText));
    }

    private static bool CompareWithNumericLiteral(ComparisonOperator op, RuleValue left, decimal rightNumber, string rightText)
    {
        if (left.IsNull)
        {
            return op == ComparisonOperator.NotEqual;
        }

        if (left.TryGetNumber(out var leftNumber))
        {
            return Ordered(op, leftNumber.CompareTo(rightNumber));
        }

        return Ordered(op, string.CompareOrdinal(left.ToString(), rightText));
    }

    private static bool CompareWithNonNumericTextLiteral(ComparisonOperator op, RuleValue left, string rightText)
    {
        if (left.IsNull)
        {
            return op == ComparisonOperator.NotEqual;
        }

        if (left.TryGetText(out var leftText))
        {
            return op switch
            {
                ComparisonOperator.Equal => string.Equals(leftText, rightText, StringComparison.Ordinal),
                ComparisonOperator.NotEqual => !string.Equals(leftText, rightText, StringComparison.Ordinal),
                _ => Ordered(op, string.CompareOrdinal(leftText, rightText)),
            };
        }

        if (left.Kind == RuleValueKind.Number && op is ComparisonOperator.Equal or ComparisonOperator.NotEqual)
        {
            return op == ComparisonOperator.NotEqual;
        }

        return Ordered(op, string.CompareOrdinal(left.ToString(), rightText));
    }

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

    private static Func<IRuleVariables, RuleValue> CompileFunction(RuleFunction function, IReadOnlyList<RuleExpression> arguments)
    {
        switch (function)
        {
            case RuleFunction.IsEmpty:
                var emptyArg = Compile(arguments[0]);
                return variables => RuleValue.FromBoolean(IsEmpty(emptyArg(variables)));
            case RuleFunction.IsNotEmpty:
                var notEmptyArg = Compile(arguments[0]);
                return variables => RuleValue.FromBoolean(!IsEmpty(notEmptyArg(variables)));
            case RuleFunction.In:
                return CompileIn(negate: false, arguments);
            case RuleFunction.NotIn:
                return CompileIn(negate: true, arguments);
            default:
                var lengthArg = Compile(arguments[0]);
                return variables => lengthArg(variables).TryGetText(out var text)
                    ? RuleValue.FromNumber(CountRunes(text))
                    : RuleValue.Null;
        }
    }

    private static Func<IRuleVariables, RuleValue> CompileIn(bool negate, IReadOnlyList<RuleExpression> arguments)
    {
        var target = Compile(arguments[0]);
        var candidateCount = arguments.Count - 1;
        var candidates = new ClassifiedOperand[candidateCount];
        var allNonNumericTextLiterals = true;
        for (var i = 0; i < candidateCount; i++)
        {
            var classified = ClassifyOperand(arguments[i + 1]);
            candidates[i] = classified;
            if (classified.Kind != OperandKind.NonNumericTextLiteral)
            {
                allNonNumericTextLiterals = false;
            }
        }

        if (allNonNumericTextLiterals)
        {
            var texts = new string[candidateCount];
            for (var i = 0; i < candidateCount; i++)
            {
                texts[i] = candidates[i].Text;
            }

            return variables => RuleValue.FromBoolean(InNonNumericTextLiterals(target(variables), texts) != negate);
        }

        return variables => RuleValue.FromBoolean(InCandidates(target(variables), candidates, variables) != negate);
    }

    private static bool InNonNumericTextLiterals(RuleValue value, string[] candidates)
    {
        if (value.TryGetText(out var text))
        {
            return ContainsOrdinal(candidates, text);
        }

        if (value.IsNull || value.Kind == RuleValueKind.Number)
        {
            return false;
        }

        return ContainsOrdinal(candidates, value.ToString());
    }

    private static bool ContainsOrdinal(string[] candidates, string value)
    {
        for (var i = 0; i < candidates.Length; i++)
        {
            if (string.Equals(value, candidates[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InCandidates(RuleValue value, ClassifiedOperand[] candidates, IRuleVariables variables)
    {
        var numberState = new LazyNumber(value);
        for (var i = 0; i < candidates.Length; i++)
        {
            if (MatchesCandidate(value, candidates[i], variables, ref numberState))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesCandidate(
        RuleValue value, ClassifiedOperand candidate, IRuleVariables variables, ref LazyNumber numberState) =>
        candidate.Kind switch
        {
            OperandKind.NullLiteral => value.IsNull,
            OperandKind.NonNumericTextLiteral => CompareWithNonNumericTextLiteral(ComparisonOperator.Equal, value, candidate.Text),
            OperandKind.BooleanLiteral => CompareWithBooleanLiteral(ComparisonOperator.Equal, value, candidate.Boolean, candidate.Text),
            OperandKind.NumericLiteral => MatchesNumericLiteral(value, candidate.Number, candidate.Text, ref numberState),
            _ => MatchesDynamic(value, candidate.Reader!(variables), ref numberState),
        };

    private static bool MatchesNumericLiteral(RuleValue value, decimal rightNumber, string rightText, ref LazyNumber numberState)
    {
        if (value.IsNull)
        {
            return false;
        }

        if (numberState.TryGet(out var leftNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(value.ToString(), rightText, StringComparison.Ordinal);
    }

    private static bool MatchesDynamic(RuleValue value, RuleValue right, ref LazyNumber numberState)
    {
        if (value.IsNull || right.IsNull)
        {
            return value.IsNull && right.IsNull;
        }

        if (value.CanBeNumber && right.CanBeNumber && numberState.TryGet(out var leftNumber) && right.TryGetNumber(out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        if (value.TryGetBoolean(out var leftBool) && right.TryGetBoolean(out var rightBool))
        {
            return leftBool == rightBool;
        }

        return string.Equals(value.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    private static int CountRunes(string text)
    {
        var count = 0;
        var enumerator = text.EnumerateRunes();
        while (enumerator.MoveNext())
        {
            count++;
        }

        return count;
    }

    private static bool IsEmpty(RuleValue value) =>
        value.IsNull || (value.TryGetText(out var text) && string.IsNullOrWhiteSpace(text));

    private static ClassifiedOperand ClassifyOperand(RuleExpression expression) =>
        expression is LiteralExpression literal
            ? ClassifyLiteral(literal.Value)
            : new ClassifiedOperand(OperandKind.Dynamic, Compile(expression), 0m, string.Empty, false);

    private static ClassifiedOperand ClassifyLiteral(RuleValue value)
    {
        if (value.IsNull)
        {
            return new ClassifiedOperand(OperandKind.NullLiteral, null, 0m, "null", false);
        }

        if (value.TryGetBoolean(out var boolean))
        {
            return new ClassifiedOperand(OperandKind.BooleanLiteral, null, 0m, boolean ? "true" : "false", boolean);
        }

        if (value.TryGetNumber(out var number))
        {
            return new ClassifiedOperand(OperandKind.NumericLiteral, null, number, value.ToString(), false);
        }

        return new ClassifiedOperand(OperandKind.NonNumericTextLiteral, null, 0m, value.ToString(), false);
    }

    private enum OperandKind : byte
    {
        Dynamic,
        NullLiteral,
        BooleanLiteral,
        NumericLiteral,
        NonNumericTextLiteral,
    }

    private readonly record struct ClassifiedOperand(
        OperandKind Kind,
        Func<IRuleVariables, RuleValue>? Reader,
        decimal Number,
        string Text,
        bool Boolean);

    private struct LazyNumber(RuleValue value)
    {
        private readonly RuleValue _value = value;
        private decimal _number;
        private bool _checked;
        private bool _hasNumber;

        public bool TryGet(out decimal number)
        {
            if (!_checked)
            {
                _checked = true;
                _hasNumber = _value.TryGetNumber(out _number);
            }

            number = _number;
            return _hasNumber;
        }
    }
}
