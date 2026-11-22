using System;
using System.Collections.Generic;
using System.Globalization;
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
/// equality only (ordering is false); two ISO-8601 dates or timestamps parse into UTC instants before ordering;
/// anything else compares as invariant text, ordinally.</item>
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

        if (TryGetUtcTicks(left, out var leftTicks) && TryGetUtcTicks(right, out var rightTicks))
        {
            return Ordered(op, leftTicks.CompareTo(rightTicks));
        }

        return Ordered(op, string.CompareOrdinal(left.ToString(), right.ToString()));
    }

    private static bool TryGetUtcTicks(RuleValue value, out long utcTicks)
    {
        utcTicks = 0;
        if (!value.TryGetText(out var text))
        {
            return false;
        }

        var span = text.AsSpan();
        if (span.Length < 10
            || !DateOnly.TryParseExact(span[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return false;
        }

        if (span.Length == 10)
        {
            utcTicks = new DateTime(date, TimeOnly.MinValue, DateTimeKind.Utc).Ticks;
            return true;
        }

        if (span[10] is not ('T' or 't' or ' '))
        {
            return false;
        }

        var rest = span[11..];
        if (rest.IsEmpty)
        {
            return false;
        }

        var offset = TimeSpan.Zero;
        ReadOnlySpan<char> timeSpan;
        if (rest[^1] is 'Z' or 'z')
        {
            timeSpan = rest[..^1];
        }
        else
        {
            var signIdx = -1;
            for (var i = 1; i < rest.Length; i++)
            {
                if (rest[i] is '+' or '-')
                {
                    signIdx = i;
                    break;
                }
            }

            if (signIdx >= 0)
            {
                if (!TryParseIsoOffset(rest[signIdx..], out offset))
                {
                    return false;
                }

                timeSpan = rest[..signIdx];
            }
            else
            {
                timeSpan = rest;
            }
        }

        if (!TryParseIsoTimeOfDay(timeSpan, out var timeOfDayTicks))
        {
            return false;
        }

        var dateTicks = new DateTime(date, TimeOnly.MinValue, DateTimeKind.Utc).Ticks;
        utcTicks = dateTicks + timeOfDayTicks - offset.Ticks;
        return true;
    }

    private static bool TryParseIsoOffset(ReadOnlySpan<char> span, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (span.Length < 3 || span[0] is not ('+' or '-'))
        {
            return false;
        }

        var negative = span[0] == '-';
        var digits = span[1..];
        int hours;
        int minutes = 0;
        if (digits.Length == 2)
        {
            if (!TryParseTwoDigits(digits, out hours))
            {
                return false;
            }
        }
        else if (digits.Length == 4)
        {
            if (!TryParseTwoDigits(digits[..2], out hours) || !TryParseTwoDigits(digits[2..], out minutes))
            {
                return false;
            }
        }
        else if (digits.Length == 5 && digits[2] == ':')
        {
            if (!TryParseTwoDigits(digits[..2], out hours) || !TryParseTwoDigits(digits[3..], out minutes))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        if (hours > 14 || minutes > 59 || (hours == 14 && minutes > 0))
        {
            return false;
        }

        var raw = new TimeSpan(hours, minutes, 0);
        offset = negative ? -raw : raw;
        return true;
    }

    private static bool TryParseIsoTimeOfDay(ReadOnlySpan<char> span, out long ticks)
    {
        ticks = 0;
        if (span.Length < 5 || span[2] != ':'
            || !TryParseTwoDigits(span[..2], out var hours)
            || !TryParseTwoDigits(span.Slice(3, 2), out var minutes)
            || hours > 23 || minutes > 59)
        {
            return false;
        }

        var seconds = 0;
        long subsecondTicks = 0;
        if (span.Length > 5)
        {
            if (span.Length < 8 || span[5] != ':' || !TryParseTwoDigits(span.Slice(6, 2), out seconds) || seconds > 59)
            {
                return false;
            }

            if (span.Length > 8)
            {
                if (span.Length < 10 || span[8] != '.')
                {
                    return false;
                }

                var fraction = span[9..];
                long scale = TimeSpan.TicksPerSecond / 10;
                for (var i = 0; i < fraction.Length; i++)
                {
                    var ch = fraction[i];
                    if (ch is < '0' or > '9')
                    {
                        return false;
                    }

                    if (scale > 0)
                    {
                        subsecondTicks += (ch - '0') * scale;
                        scale /= 10;
                    }
                }
            }
        }

        ticks = new TimeSpan(hours, minutes, seconds).Ticks + subsecondTicks;
        return true;
    }

    private static bool TryParseTwoDigits(ReadOnlySpan<char> span, out int value)
    {
        if (span.Length == 2 && span[0] is >= '0' and <= '9' && span[1] is >= '0' and <= '9')
        {
            value = ((span[0] - '0') * 10) + (span[1] - '0');
            return true;
        }

        value = 0;
        return false;
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
