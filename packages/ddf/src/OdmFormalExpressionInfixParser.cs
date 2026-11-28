using System;
using System.Collections.Generic;
using System.Globalization;

using Cadence.Rules.Expressions;

namespace Cadence.Ddf;

internal sealed class OdmFormalExpressionInfixParser(
    IReadOnlyList<OdmExpressionToken> tokens,
    Func<string, string> resolveVariable,
    string? defaultVariable)
{
    private int _pos;
    private int _depth = -1;
    private int _nodes;

    public bool IsAtEnd => _pos >= tokens.Count;

    public OdmExpressionToken Current => _pos < tokens.Count ? tokens[_pos] : default;

    public RuleExpression ParseOr()
    {
        EnterDepth();
        try
        {
            var operands = new List<RuleExpression> { ParseAnd() };
            while (MatchKeyword("or") || MatchSymbol("||"))
            {
                operands.Add(ParseAnd());
            }

            return operands.Count == 1 ? operands[0] : AddNode(new LogicalExpression(LogicalOperator.Or, operands));
        }
        finally
        {
            _depth--;
        }
    }

    private RuleExpression ParseAnd()
    {
        var operands = new List<RuleExpression> { ParseUnary() };
        while (MatchKeyword("and") || MatchSymbol("&&"))
        {
            operands.Add(ParseUnary());
        }

        return operands.Count == 1 ? operands[0] : AddNode(new LogicalExpression(LogicalOperator.And, operands));
    }

    private RuleExpression ParseUnary()
    {
        if (MatchSymbol("!"))
        {
            EnterDepth();
            try
            {
                return AddNode(new NotExpression(ParseUnary()));
            }
            finally
            {
                _depth--;
            }
        }

        if (PeekKeyword("not"))
        {
            _pos++;
            if (MatchSymbol("("))
            {
                var inner = ParseOr();
                ExpectSymbol(")");
                return AddNode(new NotExpression(inner));
            }

            EnterDepth();
            try
            {
                return AddNode(new NotExpression(ParseUnary()));
            }
            finally
            {
                _depth--;
            }
        }

        return ParseComparison();
    }

    private RuleExpression ParseComparison()
    {
        var left = ParseAdditive();

        if (MatchKeyword("is"))
        {
            bool negated = MatchKeyword("not");
            if (MatchKeyword("null") || MatchKeyword("empty") || MatchKeyword("missing"))
            {
                return AddNode(new FunctionExpression(negated ? RuleFunction.IsNotEmpty : RuleFunction.IsEmpty, [left]));
            }

            throw new FormatException("Expected 'null' or 'empty' after 'is'.");
        }

        if (PeekKeyword("not") && PeekKeywordAt(1, "in"))
        {
            _pos += 2;
            var candidates = ParseInList();
            return AddNode(new FunctionExpression(RuleFunction.NotIn, [left, .. candidates]));
        }

        if (MatchKeyword("in"))
        {
            var candidates = ParseInList();
            return AddNode(new FunctionExpression(RuleFunction.In, [left, .. candidates]));
        }

        ComparisonOperator? op = null;
        if (MatchSymbol("==") || MatchSymbol("="))
        {
            op = ComparisonOperator.Equal;
        }
        else if (MatchSymbol("!=") || MatchSymbol("<>"))
        {
            op = ComparisonOperator.NotEqual;
        }
        else if (MatchSymbol("<="))
        {
            op = ComparisonOperator.LessOrEqual;
        }
        else if (MatchSymbol("<"))
        {
            op = ComparisonOperator.Less;
        }
        else if (MatchSymbol(">="))
        {
            op = ComparisonOperator.GreaterOrEqual;
        }
        else if (MatchSymbol(">"))
        {
            op = ComparisonOperator.Greater;
        }

        if (op is null)
        {
            return left;
        }

        var right = ParseAdditive();
        if (IsNullOrEmptyLiteral(right))
        {
            if (op == ComparisonOperator.Equal)
            {
                _nodes--;
                return AddNode(new FunctionExpression(RuleFunction.IsEmpty, [left]));
            }

            if (op == ComparisonOperator.NotEqual)
            {
                _nodes--;
                return AddNode(new FunctionExpression(RuleFunction.IsNotEmpty, [left]));
            }
        }

        if (IsNullOrEmptyLiteral(left))
        {
            if (op == ComparisonOperator.Equal)
            {
                _nodes--;
                return AddNode(new FunctionExpression(RuleFunction.IsEmpty, [right]));
            }

            if (op == ComparisonOperator.NotEqual)
            {
                _nodes--;
                return AddNode(new FunctionExpression(RuleFunction.IsNotEmpty, [right]));
            }
        }

        return AddNode(new ComparisonExpression(op.Value, left, right));
    }

    private static bool IsNullOrEmptyLiteral(RuleExpression expression) =>
        expression is LiteralExpression { Value: var v }
        && (v.IsNull || (v.TryGetText(out var text) && text.Length == 0));

    private List<RuleExpression> ParseInList()
    {
        EnterDepth();
        try
        {
            ExpectSymbol("(");
            var list = new List<RuleExpression> { ParseAdditive() };
            while (MatchSymbol(","))
            {
                list.Add(ParseAdditive());
            }

            ExpectSymbol(")");
            return list;
        }
        finally
        {
            _depth--;
        }
    }

    private RuleExpression ParseAdditive()
    {
        var left = ParseMultiplicative();
        int chainDepth = 0;
        while (true)
        {
            if (MatchSymbol("+"))
            {
                CheckChainDepth(ref chainDepth);
                left = AddNode(new ArithmeticExpression(ArithmeticOperator.Add, left, ParseMultiplicative()));
            }
            else if (MatchSymbol("-"))
            {
                CheckChainDepth(ref chainDepth);
                left = AddNode(new ArithmeticExpression(ArithmeticOperator.Subtract, left, ParseMultiplicative()));
            }
            else
            {
                break;
            }
        }

        return left;
    }

    private RuleExpression ParseMultiplicative()
    {
        var left = ParsePrimary();
        int chainDepth = 0;
        while (true)
        {
            if (MatchSymbol("*"))
            {
                CheckChainDepth(ref chainDepth);
                left = AddNode(new ArithmeticExpression(ArithmeticOperator.Multiply, left, ParsePrimary()));
            }
            else if (MatchSymbol("/"))
            {
                CheckChainDepth(ref chainDepth);
                left = AddNode(new ArithmeticExpression(ArithmeticOperator.Divide, left, ParsePrimary()));
            }
            else
            {
                break;
            }
        }

        return left;
    }

    private void CheckChainDepth(ref int chainDepth)
    {
        if (_depth + (++chainDepth) > OdmFormalExpressionParser.MaxDepth)
        {
            throw new OdmExpressionLimitExceededException(
                $"FormalExpression is nested more than {OdmFormalExpressionParser.MaxDepth} levels.");
        }
    }

    private RuleExpression ParsePrimary()
    {
        if (IsAtEnd)
        {
            throw new FormatException("Unexpected end of FormalExpression.");
        }

        if (MatchSymbol("("))
        {
            var expr = ParseOr();
            ExpectSymbol(")");
            return expr;
        }

        if (MatchSymbol("-"))
        {
            if (!IsAtEnd && Current.Kind == OdmExpressionTokenKind.Number
                && decimal.TryParse("-" + Current.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var neg))
            {
                _pos++;
                return AddNode(new LiteralExpression(RuleValue.FromNumber(neg)));
            }

            EnterDepth();
            try
            {
                var zero = AddNode(new LiteralExpression(RuleValue.FromNumber(0m)));
                var operand = ParsePrimary();
                return AddNode(new ArithmeticExpression(ArithmeticOperator.Subtract, zero, operand));
            }
            finally
            {
                _depth--;
            }
        }

        var token = tokens[_pos++];
        switch (token.Kind)
        {
            case OdmExpressionTokenKind.Number:
                if (!decimal.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                {
                    throw new FormatException($"Invalid numeric literal '{token.Text}'.");
                }

                return AddNode(new LiteralExpression(RuleValue.FromNumber(num)));

            case OdmExpressionTokenKind.String:
                return AddNode(new LiteralExpression(RuleValue.FromText(token.Text)));

            case OdmExpressionTokenKind.Identifier:
                if (MatchSymbol("("))
                {
                    return ParseFunctionCall(token.Text);
                }

                if (string.Equals(token.Text, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return AddNode(new LiteralExpression(RuleValue.True));
                }

                if (string.Equals(token.Text, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return AddNode(new LiteralExpression(RuleValue.False));
                }

                if (string.Equals(token.Text, "null", StringComparison.OrdinalIgnoreCase))
                {
                    return AddNode(new LiteralExpression(RuleValue.Null));
                }

                var rawName = token.Text == "." && !string.IsNullOrWhiteSpace(defaultVariable)
                    ? defaultVariable
                    : token.Text;
                return AddNode(new VariableExpression(resolveVariable(rawName)));

            default:
                throw new FormatException($"Unexpected token '{token.Text}'.");
        }
    }

    private RuleExpression ParseFunctionCall(string name)
    {
        if (_depth >= OdmFormalExpressionParser.MaxDepth)
        {
            throw new OdmExpressionLimitExceededException(
                $"FormalExpression is nested more than {OdmFormalExpressionParser.MaxDepth} levels.");
        }

        var args = new List<RuleExpression>();
        if (!MatchSymbol(")"))
        {
            args.Add(ParseOr());
            while (MatchSymbol(","))
            {
                args.Add(ParseOr());
            }

            ExpectSymbol(")");
        }

        var normalized = name.ToLowerInvariant();
        return normalized switch
        {
            "true" when args.Count == 0 => AddNode(new LiteralExpression(RuleValue.True)),
            "false" when args.Count == 0 => AddNode(new LiteralExpression(RuleValue.False)),
            "is_empty" or "isempty" or "empty" or "missing" or "is_null" or "isnull" when args.Count == 1 =>
                AddNode(new FunctionExpression(RuleFunction.IsEmpty, args)),
            "is_not_empty" or "isnotempty" or "not_empty" or "notempty" or "present" or "not_null" or "notnull" when args.Count == 1 =>
                AddNode(new FunctionExpression(RuleFunction.IsNotEmpty, args)),
            "length" or "string-length" or "strlen" when args.Count == 1 =>
                AddNode(new FunctionExpression(RuleFunction.Length, args)),
            "in" when args.Count >= 2 =>
                AddNode(new FunctionExpression(RuleFunction.In, args)),
            "not_in" or "notin" when args.Count >= 2 =>
                AddNode(new FunctionExpression(RuleFunction.NotIn, args)),
            _ => throw new FormatException($"Unsupported function '{name}' with {args.Count} argument(s) in FormalExpression."),
        };
    }

    private void EnterDepth()
    {
        if (++_depth > OdmFormalExpressionParser.MaxDepth)
        {
            _depth--;
            throw new OdmExpressionLimitExceededException(
                $"FormalExpression is nested more than {OdmFormalExpressionParser.MaxDepth} levels.");
        }
    }

    private T AddNode<T>(T node)
        where T : RuleExpression
    {
        if (++_nodes > OdmFormalExpressionParser.MaxNodes)
        {
            throw new OdmExpressionLimitExceededException(
                $"FormalExpression has more than {OdmFormalExpressionParser.MaxNodes} nodes.");
        }

        return node;
    }

    private bool MatchSymbol(string text)
    {
        if (!IsAtEnd && Current.Kind == OdmExpressionTokenKind.Symbol && string.Equals(Current.Text, text, StringComparison.Ordinal))
        {
            _pos++;
            return true;
        }

        return false;
    }

    private void ExpectSymbol(string text)
    {
        if (!MatchSymbol(text))
        {
            throw new FormatException($"Expected '{text}' in FormalExpression.");
        }
    }

    private bool PeekKeyword(string keyword) => PeekKeywordAt(0, keyword);

    private bool PeekKeywordAt(int offset, string keyword) =>
        _pos + offset < tokens.Count
        && tokens[_pos + offset].Kind == OdmExpressionTokenKind.Identifier
        && string.Equals(tokens[_pos + offset].Text, keyword, StringComparison.OrdinalIgnoreCase);

    private bool MatchKeyword(string keyword)
    {
        if (PeekKeyword(keyword))
        {
            _pos++;
            return true;
        }

        return false;
    }
}
