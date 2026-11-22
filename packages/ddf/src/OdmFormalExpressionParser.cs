using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.Rules.Expressions;

namespace Cadence.Ddf;

/// <summary>
/// Parses ODM v1.3.2 <c>FormalExpression</c> strings (both canonical JSON rule expressions and infix/functional clinical
/// expressions) into typed <see cref="RuleExpression"/> syntax trees.
/// </summary>
public static partial class OdmFormalExpressionParser
{
    /// <summary>
    /// Attempts to parse <paramref name="rawExpression"/> into a <see cref="RuleExpression"/>, resolving variable tokens
    /// through <paramref name="resolveVariable"/> (and mapping <c>.</c> to <paramref name="defaultVariable"/> when present).
    /// </summary>
    public static bool TryParse(
        string? rawExpression,
        Func<string, string>? resolveVariable,
        string? defaultVariable,
        out RuleExpression? expression,
        out string? error)
    {
        expression = null;
        error = null;
        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            error = "FormalExpression is empty.";
            return false;
        }

        var trimmed = rawExpression.Trim();
        var resolver = resolveVariable ?? (static name => name);

        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (RuleExpressionJson.TryParse(doc.RootElement, out var parsedJson, out var jsonError) && parsedJson is not null)
                {
                    expression = RewriteVariables(parsedJson, resolver, defaultVariable);
                    return true;
                }

                error = jsonError ?? "Invalid JSON RuleExpression.";
                return false;
            }
            catch (JsonException ex)
            {
                error = $"Invalid JSON FormalExpression: {ex.Message}";
                return false;
            }
        }

        // Normalize XPath-like ItemData[@ItemOID='OID'] references before tokenizing.
        var normalized = ItemDataXPathRegex().Replace(trimmed, m => m.Groups["oid"].Value);

        try
        {
            var tokens = Tokenize(normalized);
            if (tokens.Count == 0)
            {
                error = "FormalExpression contains no tokens.";
                return false;
            }

            var parser = new Parser(tokens, resolver, defaultVariable);
            var result = parser.ParseOr();
            if (!parser.IsAtEnd)
            {
                error = $"Unexpected token '{parser.Current.Text}' in FormalExpression.";
                return false;
            }

            expression = result;
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Rewrites variable names in <paramref name="expression"/> using <paramref name="map"/>.</summary>
    public static RuleExpression RewriteVariables(
        RuleExpression expression,
        Func<string, string> map,
        string? defaultVariable = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(map);

        return expression switch
        {
            LiteralExpression lit => lit,
            VariableExpression v => new VariableExpression(
                v.Name == "." && !string.IsNullOrWhiteSpace(defaultVariable)
                    ? map(defaultVariable)
                    : map(v.Name)),
            NotExpression n => new NotExpression(RewriteVariables(n.Operand, map, defaultVariable)),
            LogicalExpression l => new LogicalExpression(
                l.Operator,
                [.. l.Operands.Select(o => RewriteVariables(o, map, defaultVariable))]),
            ComparisonExpression c => new ComparisonExpression(
                c.Operator,
                RewriteVariables(c.Left, map, defaultVariable),
                RewriteVariables(c.Right, map, defaultVariable)),
            ArithmeticExpression a => new ArithmeticExpression(
                a.Operator,
                RewriteVariables(a.Left, map, defaultVariable),
                RewriteVariables(a.Right, map, defaultVariable)),
            FunctionExpression f => new FunctionExpression(
                f.Function,
                [.. f.Arguments.Select(o => RewriteVariables(o, map, defaultVariable))]),
            _ => expression,
        };
    }

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c is '\'' or '"')
            {
                char quote = c;
                i++;
                var sb = new StringBuilder();
                while (i < input.Length && input[i] != quote)
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        i++;
                    }

                    sb.Append(input[i]);
                    i++;
                }

                if (i >= input.Length)
                {
                    throw new FormatException("Unterminated string literal in FormalExpression.");
                }

                i++;
                tokens.Add(new Token(TokenKind.String, sb.ToString()));
                continue;
            }

            if (i + 1 < input.Length)
            {
                var two = input.Substring(i, 2);
                if (two is "==" or "!=" or "<>" or "<=" or ">=" or "&&" or "||")
                {
                    tokens.Add(new Token(TokenKind.Symbol, two));
                    i += 2;
                    continue;
                }
            }

            if (c is '(' or ')' or ',' or '+' or '-' or '*' or '/' or '<' or '>' or '=' or '!')
            {
                // Keep '-' inside an identifier only when we are already reading an identifier below; standalone '-' is a symbol.
                tokens.Add(new Token(TokenKind.Symbol, c.ToString()));
                i++;
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                int start = i;
                while (i < input.Length && char.IsAsciiDigit(input[i]))
                {
                    i++;
                }

                if (i < input.Length && input[i] == '.' && i + 1 < input.Length && char.IsAsciiDigit(input[i + 1]))
                {
                    i++;
                    while (i < input.Length && char.IsAsciiDigit(input[i]))
                    {
                        i++;
                    }
                }

                tokens.Add(new Token(TokenKind.Number, input[start..i]));
                continue;
            }

            if (c == '$' && i + 1 < input.Length && (char.IsAsciiLetter(input[i + 1]) || input[i + 1] == '_'))
            {
                i++;
                c = input[i];
            }

            if (c == '.' && (i + 1 >= input.Length || !char.IsAsciiLetterOrDigit(input[i + 1])))
            {
                tokens.Add(new Token(TokenKind.Identifier, "."));
                i++;
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = i;
                while (i < input.Length)
                {
                    char ch = input[i];
                    if (char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or ':' or '/')
                    {
                        i++;
                        continue;
                    }

                    // Allow '-' inside identifiers such as string-length(...) or IT-SYSBP when followed by letter/digit.
                    if (ch == '-' && i + 1 < input.Length && char.IsAsciiLetterOrDigit(input[i + 1]))
                    {
                        i++;
                        continue;
                    }

                    break;
                }

                tokens.Add(new Token(TokenKind.Identifier, input[start..i]));
                continue;
            }

            throw new FormatException($"Unsupported character '{c}' in FormalExpression.");
        }

        return tokens;
    }

    [GeneratedRegex(@"ItemData\s*\[\s*@ItemOID\s*=\s*['""](?<oid>[^'""]+)['""]\s*\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ItemDataXPathRegex();

    private enum TokenKind
    {
        Identifier,
        Number,
        String,
        Symbol,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private sealed class Parser(IReadOnlyList<Token> tokens, Func<string, string> resolveVariable, string? defaultVariable)
    {
        private int _pos;

        public bool IsAtEnd => _pos >= tokens.Count;

        public Token Current => _pos < tokens.Count ? tokens[_pos] : default;

        public RuleExpression ParseOr()
        {
            var operands = new List<RuleExpression> { ParseAnd() };
            while (MatchKeyword("or") || MatchSymbol("||"))
            {
                operands.Add(ParseAnd());
            }

            return operands.Count == 1 ? operands[0] : new LogicalExpression(LogicalOperator.Or, operands);
        }

        private RuleExpression ParseAnd()
        {
            var operands = new List<RuleExpression> { ParseUnary() };
            while (MatchKeyword("and") || MatchSymbol("&&"))
            {
                operands.Add(ParseUnary());
            }

            return operands.Count == 1 ? operands[0] : new LogicalExpression(LogicalOperator.And, operands);
        }

        private RuleExpression ParseUnary()
        {
            if (MatchSymbol("!"))
            {
                return new NotExpression(ParseUnary());
            }

            if (PeekKeyword("not"))
            {
                _pos++;
                if (MatchSymbol("("))
                {
                    var inner = ParseOr();
                    ExpectSymbol(")");
                    return new NotExpression(inner);
                }

                return new NotExpression(ParseUnary());
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
                    return new FunctionExpression(negated ? RuleFunction.IsNotEmpty : RuleFunction.IsEmpty, [left]);
                }

                throw new FormatException("Expected 'null' or 'empty' after 'is'.");
            }

            if (PeekKeyword("not") && PeekKeywordAt(1, "in"))
            {
                _pos += 2;
                var candidates = ParseInList();
                return new FunctionExpression(RuleFunction.NotIn, [left, .. candidates]);
            }

            if (MatchKeyword("in"))
            {
                var candidates = ParseInList();
                return new FunctionExpression(RuleFunction.In, [left, .. candidates]);
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
                    return new FunctionExpression(RuleFunction.IsEmpty, [left]);
                }

                if (op == ComparisonOperator.NotEqual)
                {
                    return new FunctionExpression(RuleFunction.IsNotEmpty, [left]);
                }
            }

            if (IsNullOrEmptyLiteral(left))
            {
                if (op == ComparisonOperator.Equal)
                {
                    return new FunctionExpression(RuleFunction.IsEmpty, [right]);
                }

                if (op == ComparisonOperator.NotEqual)
                {
                    return new FunctionExpression(RuleFunction.IsNotEmpty, [right]);
                }
            }

            return new ComparisonExpression(op.Value, left, right);
        }

        private static bool IsNullOrEmptyLiteral(RuleExpression expression) =>
            expression is LiteralExpression { Value: var v }
            && (v.IsNull || (v.TryGetText(out var text) && text.Length == 0));

        private List<RuleExpression> ParseInList()
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

        private RuleExpression ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                if (MatchSymbol("+"))
                {
                    left = new ArithmeticExpression(ArithmeticOperator.Add, left, ParseMultiplicative());
                }
                else if (MatchSymbol("-"))
                {
                    left = new ArithmeticExpression(ArithmeticOperator.Subtract, left, ParseMultiplicative());
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
            while (true)
            {
                if (MatchSymbol("*"))
                {
                    left = new ArithmeticExpression(ArithmeticOperator.Multiply, left, ParsePrimary());
                }
                else if (MatchSymbol("/"))
                {
                    left = new ArithmeticExpression(ArithmeticOperator.Divide, left, ParsePrimary());
                }
                else
                {
                    break;
                }
            }

            return left;
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
                if (!IsAtEnd && Current.Kind == TokenKind.Number
                    && decimal.TryParse("-" + Current.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var neg))
                {
                    _pos++;
                    return new LiteralExpression(RuleValue.FromNumber(neg));
                }

                return new ArithmeticExpression(
                    ArithmeticOperator.Subtract,
                    new LiteralExpression(RuleValue.FromNumber(0m)),
                    ParsePrimary());
            }

            var token = tokens[_pos++];
            switch (token.Kind)
            {
                case TokenKind.Number:
                    if (!decimal.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    {
                        throw new FormatException($"Invalid numeric literal '{token.Text}'.");
                    }

                    return new LiteralExpression(RuleValue.FromNumber(num));

                case TokenKind.String:
                    return new LiteralExpression(RuleValue.FromText(token.Text));

                case TokenKind.Identifier:
                    if (MatchSymbol("("))
                    {
                        return ParseFunctionCall(token.Text);
                    }

                    if (string.Equals(token.Text, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LiteralExpression(RuleValue.True);
                    }

                    if (string.Equals(token.Text, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LiteralExpression(RuleValue.False);
                    }

                    if (string.Equals(token.Text, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LiteralExpression(RuleValue.Null);
                    }

                    var rawName = token.Text == "." && !string.IsNullOrWhiteSpace(defaultVariable)
                        ? defaultVariable
                        : token.Text;
                    return new VariableExpression(resolveVariable(rawName));

                default:
                    throw new FormatException($"Unexpected token '{token.Text}'.");
            }
        }

        private RuleExpression ParseFunctionCall(string name)
        {
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
                "true" when args.Count == 0 => new LiteralExpression(RuleValue.True),
                "false" when args.Count == 0 => new LiteralExpression(RuleValue.False),
                "is_empty" or "isempty" or "empty" or "missing" or "is_null" or "isnull" when args.Count == 1 =>
                    new FunctionExpression(RuleFunction.IsEmpty, args),
                "is_not_empty" or "isnotempty" or "not_empty" or "notempty" or "present" or "not_null" or "notnull" when args.Count == 1 =>
                    new FunctionExpression(RuleFunction.IsNotEmpty, args),
                "length" or "string-length" or "strlen" when args.Count == 1 =>
                    new FunctionExpression(RuleFunction.Length, args),
                "in" when args.Count >= 2 =>
                    new FunctionExpression(RuleFunction.In, args),
                "not_in" or "notin" when args.Count >= 2 =>
                    new FunctionExpression(RuleFunction.NotIn, args),
                _ => throw new FormatException($"Unsupported function '{name}' with {args.Count} argument(s) in FormalExpression."),
            };
        }

        private bool MatchSymbol(string text)
        {
            if (!IsAtEnd && Current.Kind == TokenKind.Symbol && string.Equals(Current.Text, text, StringComparison.Ordinal))
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
            && tokens[_pos + offset].Kind == TokenKind.Identifier
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
}
