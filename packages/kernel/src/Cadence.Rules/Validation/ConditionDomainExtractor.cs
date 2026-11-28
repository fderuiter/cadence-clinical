using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Rules.Expressions;

namespace Cadence.Rules.Validation;

internal static class ConditionDomainExtractor
{
    internal static HashSet<string> CollectCandidateVariables(RuleExpression expr, string? sharedDataset) =>
        RuleExpressionJson.Variables(expr)
            .Select(v => NormalizeVariableName(v, sharedDataset))
            .ToHashSet(StringComparer.Ordinal);

    internal static string NormalizeVariableName(string name, string? sharedDataset)
    {
        if (!string.IsNullOrWhiteSpace(sharedDataset))
        {
            var prefix = sharedDataset + ".";
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return name[prefix.Length..];
            }
        }

        return name;
    }

    internal static DomainKind InferDomainKind(RuleExpression expr1, RuleExpression expr2, string variable, string? sharedDataset)
    {
        var literals = new List<RuleValue>();
        CollectComparisonLiterals(expr1, variable, sharedDataset, literals);
        CollectComparisonLiterals(expr2, variable, sharedDataset, literals);

        var hasNumber = literals.Any(l => l.TryGetNumber(out _));
        var hasNonNumericText = literals.Any(l => l.Kind == RuleValueKind.Text && !l.TryGetNumber(out _));
        var hasBoolean = literals.Any(l => l.Kind == RuleValueKind.Boolean);

        if (hasNumber && !hasNonNumericText && !hasBoolean)
        {
            return DomainKind.Number;
        }

        if (hasBoolean && !hasNumber && !hasNonNumericText)
        {
            return DomainKind.Boolean;
        }

        return DomainKind.Text;
    }

    internal static bool TryExtractAllowedDomain(
        RuleExpression expr,
        string variable,
        string? sharedDataset,
        DomainKind kind,
        out ValueDomain allowed)
    {
        var vars = CollectCandidateVariables(expr, sharedDataset);
        if (vars.Count == 1 && vars.Contains(variable))
        {
            if (TryEvaluateKleene(expr, variable, sharedDataset, kind, out var kleene))
            {
                // A rule fires only when its condition evaluates to True; False and Unknown are allowed.
                allowed = ValueDomain.Full(kind).Except(kleene.TrueSet);
                return true;
            }
        }
        else if (expr is LogicalExpression { Operator: LogicalOperator.Or } orExpr)
        {
            var combined = ValueDomain.Full(kind);
            var constrained = false;
            foreach (var branch in orExpr.Operands)
            {
                if (TryExtractAllowedDomain(branch, variable, sharedDataset, kind, out var branchAllowed))
                {
                    combined = combined.Intersect(branchAllowed);
                    constrained = true;
                }
            }

            if (constrained)
            {
                allowed = combined;
                return true;
            }
        }

        allowed = default;
        return false;
    }

    internal static OrderedInterval<string> TextPoint(string value) =>
        new(IntervalBound<string>.Of(value), true, IntervalBound<string>.Of(value), true);

    private static void CollectComparisonLiterals(
        RuleExpression expr,
        string variable,
        string? sharedDataset,
        List<RuleValue> literals)
    {
        switch (expr)
        {
            case ComparisonExpression comp when TryNormalizeComparison(comp, variable, sharedDataset, out _, out var lit):
                literals.Add(lit);
                break;
            case NotExpression not:
                CollectComparisonLiterals(not.Operand, variable, sharedDataset, literals);
                break;
            case LogicalExpression logical:
                foreach (var op in logical.Operands)
                {
                    CollectComparisonLiterals(op, variable, sharedDataset, literals);
                }

                break;
            case FunctionExpression { Function: RuleFunction.In or RuleFunction.NotIn } fn
                when fn.Arguments.Count >= 2
                     && fn.Arguments[0] is VariableExpression v
                     && string.Equals(NormalizeVariableName(v.Name, sharedDataset), variable, StringComparison.Ordinal):
                foreach (var arg in fn.Arguments.Skip(1))
                {
                    if (arg is LiteralExpression litArg)
                    {
                        literals.Add(litArg.Value);
                    }
                }

                break;
        }
    }

    private static bool TryEvaluateKleene(
        RuleExpression expr,
        string variable,
        string? sharedDataset,
        DomainKind kind,
        out (ValueDomain TrueSet, ValueDomain FalseSet) result)
    {
        var universe = ValueDomain.Full(kind);
        switch (expr)
        {
            case ComparisonExpression comp when TryNormalizeComparison(comp, variable, sharedDataset, out var op, out var lit):
                var trueSet = EvaluateComparisonTrueSet(op, lit, kind);
                result = (trueSet, universe.Except(trueSet));
                return true;

            case FunctionExpression { Function: RuleFunction.IsEmpty } fn
                when fn.Arguments.Count == 1
                     && fn.Arguments[0] is VariableExpression ve1
                     && string.Equals(NormalizeVariableName(ve1.Name, sharedDataset), variable, StringComparison.Ordinal):
                var emptyTrue = ValueDomain.EmptyOrNull(kind);
                result = (emptyTrue, universe.Except(emptyTrue));
                return true;

            case FunctionExpression { Function: RuleFunction.IsNotEmpty } fn
                when fn.Arguments.Count == 1
                     && fn.Arguments[0] is VariableExpression ve2
                     && string.Equals(NormalizeVariableName(ve2.Name, sharedDataset), variable, StringComparison.Ordinal):
                var notEmptyTrue = universe.Except(ValueDomain.EmptyOrNull(kind));
                result = (notEmptyTrue, ValueDomain.EmptyOrNull(kind));
                return true;

            case FunctionExpression { Function: RuleFunction.In or RuleFunction.NotIn } fn
                when fn.Arguments.Count >= 2
                     && fn.Arguments[0] is VariableExpression ve3
                     && string.Equals(NormalizeVariableName(ve3.Name, sharedDataset), variable, StringComparison.Ordinal)
                     && fn.Arguments.Skip(1).All(a => a is LiteralExpression):
                var inTrue = ValueDomain.Empty(kind);
                foreach (var litArg in fn.Arguments.Skip(1).Cast<LiteralExpression>())
                {
                    inTrue = inTrue.Union(EvaluateComparisonTrueSet(ComparisonOperator.Equal, litArg.Value, kind));
                }

                result = fn.Function == RuleFunction.In
                    ? (inTrue, universe.Except(inTrue))
                    : (universe.Except(inTrue), inTrue);
                return true;

            case NotExpression notExpr when TryEvaluateKleene(notExpr.Operand, variable, sharedDataset, kind, out var inner):
                result = (inner.FalseSet, inner.TrueSet);
                return true;

            case LogicalExpression { Operator: LogicalOperator.And } andExpr:
                var andTrue = universe;
                var andFalse = ValueDomain.Empty(kind);
                foreach (var operand in andExpr.Operands)
                {
                    if (!TryEvaluateKleene(operand, variable, sharedDataset, kind, out var sub))
                    {
                        result = default;
                        return false;
                    }

                    andTrue = andTrue.Intersect(sub.TrueSet);
                    andFalse = andFalse.Union(sub.FalseSet);
                }

                result = (andTrue, andFalse);
                return true;

            case LogicalExpression { Operator: LogicalOperator.Or } orExpr:
                var orTrue = ValueDomain.Empty(kind);
                var orFalse = universe;
                foreach (var operand in orExpr.Operands)
                {
                    if (!TryEvaluateKleene(operand, variable, sharedDataset, kind, out var sub))
                    {
                        result = default;
                        return false;
                    }

                    orTrue = orTrue.Union(sub.TrueSet);
                    orFalse = orFalse.Intersect(sub.FalseSet);
                }

                result = (orTrue, orFalse);
                return true;

            default:
                result = default;
                return false;
        }
    }

    private static bool TryNormalizeComparison(
        ComparisonExpression comp,
        string variable,
        string? sharedDataset,
        out ComparisonOperator op,
        out RuleValue literal)
    {
        if (comp.Left is VariableExpression leftVar
            && string.Equals(NormalizeVariableName(leftVar.Name, sharedDataset), variable, StringComparison.Ordinal)
            && comp.Right is LiteralExpression rightLit)
        {
            op = comp.Operator;
            literal = rightLit.Value;
            return true;
        }

        if (comp.Left is LiteralExpression leftLit
            && comp.Right is VariableExpression rightVar
            && string.Equals(NormalizeVariableName(rightVar.Name, sharedDataset), variable, StringComparison.Ordinal))
        {
            op = FlipComparison(comp.Operator);
            literal = leftLit.Value;
            return true;
        }

        op = default;
        literal = default;
        return false;
    }

    private static ComparisonOperator FlipComparison(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Less => ComparisonOperator.Greater,
        ComparisonOperator.LessOrEqual => ComparisonOperator.GreaterOrEqual,
        ComparisonOperator.Greater => ComparisonOperator.Less,
        ComparisonOperator.GreaterOrEqual => ComparisonOperator.LessOrEqual,
        _ => op,
    };

    private static ValueDomain EvaluateComparisonTrueSet(ComparisonOperator op, RuleValue lit, DomainKind kind)
    {
        var universe = ValueDomain.Full(kind);
        if (lit.IsNull)
        {
            return op switch
            {
                ComparisonOperator.Equal => ValueDomain.NullOnly(kind),
                ComparisonOperator.NotEqual => universe.Except(ValueDomain.NullOnly(kind)),
                _ => ValueDomain.Empty(kind),
            };
        }

        return kind switch
        {
            DomainKind.Number when lit.TryGetNumber(out var number) => op switch
            {
                ComparisonOperator.Equal => ValueDomain.FromNumbers([NumberPoint(number)]),
                ComparisonOperator.NotEqual => new ValueDomain(
                    DomainKind.Number,
                    AllowsNull: true,
                    AllowsTrue: false,
                    AllowsFalse: false,
                    AllowsBlankText: false,
                    [NumberUpper(number, inclusive: false), NumberLower(number, inclusive: false)],
                    []),
                ComparisonOperator.Less => ValueDomain.FromNumbers([NumberUpper(number, inclusive: false)]),
                ComparisonOperator.LessOrEqual => ValueDomain.FromNumbers([NumberUpper(number, inclusive: true)]),
                ComparisonOperator.Greater => ValueDomain.FromNumbers([NumberLower(number, inclusive: false)]),
                _ => ValueDomain.FromNumbers([NumberLower(number, inclusive: true)]),
            },
            DomainKind.Boolean when lit.TryGetBoolean(out var boolean) => op switch
            {
                ComparisonOperator.Equal => new ValueDomain(DomainKind.Boolean, false, boolean, !boolean, false, [], []),
                ComparisonOperator.NotEqual => new ValueDomain(DomainKind.Boolean, true, !boolean, boolean, false, [], []),
                _ => ValueDomain.Empty(DomainKind.Boolean),
            },
            _ => EvaluateTextComparisonTrueSet(op, lit.ToString()),
        };
    }

    private static ValueDomain EvaluateTextComparisonTrueSet(ComparisonOperator op, string text)
    {
        var isBlank = string.IsNullOrWhiteSpace(text);
        return op switch
        {
            ComparisonOperator.Equal => new ValueDomain(
                DomainKind.Text,
                AllowsNull: false,
                AllowsTrue: false,
                AllowsFalse: false,
                AllowsBlankText: isBlank && text.Length == 0,
                [],
                [TextPoint(text)]),
            ComparisonOperator.NotEqual => ValueDomain.Full(DomainKind.Text)
                .Except(EvaluateTextComparisonTrueSet(ComparisonOperator.Equal, text)),
            ComparisonOperator.Less => ValueDomain.FromTexts(
                allowsBlankText: string.CompareOrdinal(string.Empty, text) < 0,
                [TextUpper(text, inclusive: false)]),
            ComparisonOperator.LessOrEqual => ValueDomain.FromTexts(
                allowsBlankText: string.CompareOrdinal(string.Empty, text) <= 0,
                [TextUpper(text, inclusive: true)]),
            ComparisonOperator.Greater => ValueDomain.FromTexts(
                allowsBlankText: false,
                [TextLower(text, inclusive: false)]),
            _ => ValueDomain.FromTexts(
                allowsBlankText: text.Length == 0,
                [TextLower(text, inclusive: true)]),
        };
    }

    private static OrderedInterval<decimal> NumberPoint(decimal value) =>
        new(IntervalBound<decimal>.Of(value), true, IntervalBound<decimal>.Of(value), true);

    private static OrderedInterval<decimal> NumberLower(decimal min, bool inclusive) =>
        new(IntervalBound<decimal>.Of(min), inclusive, IntervalBound<decimal>.Unbounded, false);

    private static OrderedInterval<decimal> NumberUpper(decimal max, bool inclusive) =>
        new(IntervalBound<decimal>.Unbounded, false, IntervalBound<decimal>.Of(max), inclusive);

    private static OrderedInterval<string> TextLower(string min, bool inclusive) =>
        new(IntervalBound<string>.Of(min), inclusive, IntervalBound<string>.Unbounded, false);

    private static OrderedInterval<string> TextUpper(string max, bool inclusive) =>
        new(IntervalBound<string>.Unbounded, false, IntervalBound<string>.Of(max), inclusive);
}
