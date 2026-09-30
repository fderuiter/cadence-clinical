using System.Collections.Generic;

namespace Cadence.Rules.Expressions;

/// <summary>
/// One node of the rule expression language (spec §10.1, K-RULE-3). Every rule, whether an EDC edit check, a
/// standards conformance check or a quality indicator, is written in this one language. The grammar grew out of the
/// EDC <c>AstEditCheckEngine</c> (its JSON shapes still parse, see <see cref="RuleExpressionJson"/>); evaluation uses
/// Kleene three-valued logic for <c>and</c>, <c>or</c> and <c>not</c>.
/// </summary>
public abstract record RuleExpression
{
    /// <summary>The canonical JSON form (<see cref="RuleExpressionJson"/>).</summary>
    public sealed override string ToString() => RuleExpressionJson.ToJson(this).GetRawText();
}

/// <summary>A constant.</summary>
public sealed record LiteralExpression(RuleValue Value) : RuleExpression;

/// <summary>The value of a variable of the record being checked; null when the record has no such variable.</summary>
public sealed record VariableExpression(string Name) : RuleExpression;

/// <summary>Logical negation: true and false swap, anything else is unknown (null).</summary>
public sealed record NotExpression(RuleExpression Operand) : RuleExpression;

/// <summary>Kleene <c>and</c> or <c>or</c> over one or more operands.</summary>
public sealed record LogicalExpression(LogicalOperator Operator, IReadOnlyList<RuleExpression> Operands) : RuleExpression;

/// <summary>A comparison. It is always true or false, never unknown (see <see cref="RuleExpressionJson"/>).</summary>
public sealed record ComparisonExpression(ComparisonOperator Operator, RuleExpression Left, RuleExpression Right) : RuleExpression;

/// <summary>Decimal arithmetic; unknown when an operand is not a number, on overflow and on division by zero.</summary>
public sealed record ArithmeticExpression(ArithmeticOperator Operator, RuleExpression Left, RuleExpression Right) : RuleExpression;

/// <summary>A built-in function.</summary>
public sealed record FunctionExpression(RuleFunction Function, IReadOnlyList<RuleExpression> Arguments) : RuleExpression;

/// <summary>The operator of a <see cref="LogicalExpression"/>.</summary>
public enum LogicalOperator
{
    /// <summary>Kleene conjunction: false if any operand is false, true if all are true, otherwise unknown.</summary>
    And,
    /// <summary>Kleene disjunction: true if any operand is true, false if all are false, otherwise unknown.</summary>
    Or,
}

/// <summary>The operator of a <see cref="ComparisonExpression"/>.</summary>
public enum ComparisonOperator
{
    /// <summary><c>==</c>: equal values; null equals only null.</summary>
    Equal,
    /// <summary><c>!=</c>: the negation of <see cref="Equal"/>.</summary>
    NotEqual,
    /// <summary><c>&lt;</c>: false when either side is null or a boolean.</summary>
    Less,
    /// <summary><c>&lt;=</c>: false when either side is null or a boolean.</summary>
    LessOrEqual,
    /// <summary><c>&gt;</c>: false when either side is null or a boolean.</summary>
    Greater,
    /// <summary><c>&gt;=</c>: false when either side is null or a boolean.</summary>
    GreaterOrEqual,
}

/// <summary>The operator of an <see cref="ArithmeticExpression"/>.</summary>
public enum ArithmeticOperator
{
    /// <summary><c>+</c></summary>
    Add,
    /// <summary><c>-</c></summary>
    Subtract,
    /// <summary><c>*</c></summary>
    Multiply,
    /// <summary><c>/</c>; unknown (null) on division by zero.</summary>
    Divide,
}

/// <summary>The built-in functions. The set is closed: a rule pack cannot add code (K-RULE-1).</summary>
public enum RuleFunction
{
    /// <summary><c>is_empty(x)</c>: true when x is null or blank text.</summary>
    IsEmpty,

    /// <summary><c>is_not_empty(x)</c>: the negation of <c>is_empty</c>.</summary>
    IsNotEmpty,

    /// <summary><c>in(x, a, b, …)</c>: true when x equals one of the candidates (by <c>==</c>).</summary>
    In,

    /// <summary><c>not_in(x, a, b, …)</c>: the negation of <c>in</c>.</summary>
    NotIn,

    /// <summary><c>length(x)</c>: the number of characters of text x; null for anything else.</summary>
    Length,
}
