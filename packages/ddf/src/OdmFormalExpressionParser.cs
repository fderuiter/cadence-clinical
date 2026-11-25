using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Maximum expression nesting depth accepted during ODM parsing, reserving two AST levels for
    /// <see cref="OdmDesignImporter"/> rule wrappers (<c>RangeCheck</c> and <c>ConditionDef</c>) within
    /// <see cref="RuleExpressionJson.MaxDepth"/>.
    /// </summary>
    public const int MaxDepth = RuleExpressionJson.MaxDepth - 2;

    /// <summary>Maximum AST node count accepted during ODM parsing and variable rewriting.</summary>
    public const int MaxNodes = RuleExpressionJson.MaxNodes;

    /// <summary>Maximum token count accepted when tokenizing an infix ODM <c>FormalExpression</c>.</summary>
    public const int MaxTokens = RuleExpressionJson.MaxNodes * 8;

    /// <summary>Maximum character length accepted for a single ODM <c>FormalExpression</c>.</summary>
    public const int MaxExpressionLength = 262_144;

    /// <summary>
    /// Maximum serialized JSON token depth accepted for a parsed <see cref="RuleExpression"/> so that wrapping in
    /// <see cref="OdmDesignImporter"/> (<c>+4</c> JSON levels for <c>RangeCheck</c>) inside a <c>RulePack</c> (<c>+3</c>
    /// JSON levels) stays within <see cref="JsonDocument"/>'s default 64-level depth limit.
    /// </summary>
    public const int MaxSerializedJsonDepth = 57;

    /// <summary>
    /// Attempts to parse <paramref name="rawExpression"/> into a <see cref="RuleExpression"/>, resolving variable tokens
    /// through <paramref name="resolveVariable"/> (and mapping <c>.</c> to <paramref name="defaultVariable"/> when present).
    /// </summary>
    public static bool TryParse(
        string? rawExpression,
        Func<string, string>? resolveVariable,
        string? defaultVariable,
        out RuleExpression? expression,
        out string? error) =>
        TryParse(rawExpression, resolveVariable, defaultVariable, out expression, out error, out _);

    internal static bool TryParse(
        string? rawExpression,
        Func<string, string>? resolveVariable,
        string? defaultVariable,
        out RuleExpression? expression,
        out string? error,
        out bool limitExceeded)
    {
        expression = null;
        error = null;
        limitExceeded = false;
        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            error = "FormalExpression is empty.";
            return false;
        }

        if (rawExpression.Length > MaxExpressionLength)
        {
            limitExceeded = true;
            error = $"FormalExpression exceeds the maximum length of {MaxExpressionLength} characters.";
            return false;
        }

        var trimmed = rawExpression.Trim();
        if (trimmed.Length == 0)
        {
            error = "FormalExpression is empty.";
            return false;
        }

        var resolver = resolveVariable ?? (static name => name);

        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed, new JsonDocumentOptions { MaxDepth = MaxSerializedJsonDepth });
                if (RuleExpressionJson.TryParse(doc.RootElement, out var parsedJson, out var jsonError) && parsedJson is not null)
                {
                    EnsureAstLimits(parsedJson, MaxDepth, MaxNodes, MaxSerializedJsonDepth);
                    expression = RewriteVariables(parsedJson, resolver, defaultVariable);
                    return true;
                }

                error = jsonError ?? "Invalid JSON RuleExpression.";
                limitExceeded = jsonError is not null
                    && (jsonError.Contains("nested more than", StringComparison.Ordinal)
                        || jsonError.Contains("more than", StringComparison.Ordinal)
                        || jsonError.Contains("at most", StringComparison.Ordinal));
                return false;
            }
            catch (LimitExceededException ex)
            {
                limitExceeded = true;
                error = ex.Message;
                return false;
            }
            catch (FormatException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (JsonException ex)
            {
                limitExceeded = ex.Message.Contains("maximum allowed depth", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("maximum configured depth", StringComparison.OrdinalIgnoreCase);
                error = $"Invalid JSON FormalExpression: {ex.Message}";
                return false;
            }
        }

        try
        {
            // Normalize XPath-like ItemData[@ItemOID='OID'] references before tokenizing.
            var normalized = ItemDataXPathRegex().Replace(trimmed, m => m.Groups["oid"].Value);
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

            EnsureAstLimits(result, MaxDepth, MaxNodes, MaxSerializedJsonDepth);
            expression = result;
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            limitExceeded = true;
            error = "FormalExpression timed out during normalization.";
            return false;
        }
        catch (LimitExceededException ex)
        {
            limitExceeded = true;
            error = ex.Message;
            return false;
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

        int nodes = 0;
        return RewriteVariablesCore(expression, map, defaultVariable, depth: 0, ref nodes);
    }

    private static RuleExpression RewriteVariablesCore(
        RuleExpression expression,
        Func<string, string> map,
        string? defaultVariable,
        int depth,
        ref int nodes)
    {
        if (depth > RuleExpressionJson.MaxDepth)
        {
            throw new LimitExceededException($"FormalExpression is nested more than {RuleExpressionJson.MaxDepth} levels.");
        }

        if (++nodes > MaxNodes)
        {
            throw new LimitExceededException($"FormalExpression has more than {MaxNodes} nodes.");
        }

        switch (expression)
        {
            case LiteralExpression lit:
                return lit;
            case VariableExpression v:
                return new VariableExpression(
                    v.Name == "." && !string.IsNullOrWhiteSpace(defaultVariable)
                        ? map(defaultVariable)
                        : map(v.Name));
            case NotExpression n:
                return new NotExpression(RewriteVariablesCore(n.Operand, map, defaultVariable, depth + 1, ref nodes));
            case LogicalExpression l:
                {
                    var rewrittenOperands = new List<RuleExpression>(l.Operands.Count);
                    foreach (var operand in l.Operands)
                    {
                        rewrittenOperands.Add(RewriteVariablesCore(operand, map, defaultVariable, depth + 1, ref nodes));
                    }

                    return new LogicalExpression(l.Operator, rewrittenOperands);
                }

            case ComparisonExpression c:
                return new ComparisonExpression(
                    c.Operator,
                    RewriteVariablesCore(c.Left, map, defaultVariable, depth + 1, ref nodes),
                    RewriteVariablesCore(c.Right, map, defaultVariable, depth + 1, ref nodes));
            case ArithmeticExpression a:
                return new ArithmeticExpression(
                    a.Operator,
                    RewriteVariablesCore(a.Left, map, defaultVariable, depth + 1, ref nodes),
                    RewriteVariablesCore(a.Right, map, defaultVariable, depth + 1, ref nodes));
            case FunctionExpression f:
                {
                    var rewrittenArgs = new List<RuleExpression>(f.Arguments.Count);
                    foreach (var arg in f.Arguments)
                    {
                        rewrittenArgs.Add(RewriteVariablesCore(arg, map, defaultVariable, depth + 1, ref nodes));
                    }

                    return new FunctionExpression(f.Function, rewrittenArgs);
                }

            default:
                return expression;
        }
    }

    private static void EnsureAstLimits(RuleExpression expression, int maxDepth, int maxNodes, int maxJsonDepth)
    {
        int nodes = 0;
        MeasureAst(expression, depth: 0, jsonDepth: 0, maxDepth, maxNodes, maxJsonDepth, ref nodes);
    }

    private static void MeasureAst(
        RuleExpression expression,
        int depth,
        int jsonDepth,
        int maxDepth,
        int maxNodes,
        int maxJsonDepth,
        ref int nodes)
    {
        if (depth > maxDepth)
        {
            throw new LimitExceededException($"FormalExpression is nested more than {maxDepth} levels.");
        }

        if (++nodes > maxNodes)
        {
            throw new LimitExceededException($"FormalExpression has more than {maxNodes} nodes.");
        }

        int nodeJsonCost = expression switch
        {
            LiteralExpression => 0,
            VariableExpression => 1,
            _ => 2,
        };
        int nextJsonDepth = jsonDepth + nodeJsonCost;
        if (nextJsonDepth > maxJsonDepth)
        {
            throw new LimitExceededException($"FormalExpression exceeds the maximum serialized JSON depth of {maxJsonDepth} levels.");
        }

        switch (expression)
        {
            case NotExpression n:
                MeasureAst(n.Operand, depth + 1, nextJsonDepth, maxDepth, maxNodes, maxJsonDepth, ref nodes);
                break;
            case LogicalExpression l:
                foreach (var operand in l.Operands)
                {
                    MeasureAst(operand, depth + 1, nextJsonDepth, maxDepth, maxNodes, maxJsonDepth, ref nodes);
                }

                break;
            case ComparisonExpression c:
                MeasureAst(c.Left, depth + 1, nextJsonDepth, maxDepth, maxNodes, maxJsonDepth, ref nodes);
                MeasureAst(c.Right, depth + 1, nextJsonDepth, maxDepth, maxNodes, maxJsonDepth, ref nodes);
                break;
            case ArithmeticExpression a:
                MeasureAst(a.Left, depth + 1, nextJsonDepth, maxDepth, maxNodes, maxJsonDepth, ref nodes);
                MeasureAst(a.Right, depth + 1, nextJsonDepth, maxDepth, maxNodes, maxJsonDepth, ref nodes);
                break;
            case FunctionExpression f:
                foreach (var arg in f.Arguments)
                {
                    MeasureAst(arg, depth + 1, nextJsonDepth, maxDepth, maxNodes, maxJsonDepth, ref nodes);
                }

                break;
        }
    }

    private static List<Token> Tokenize(string input)
    {
        if (input.Length > MaxExpressionLength)
        {
            throw new LimitExceededException($"FormalExpression exceeds the maximum length of {MaxExpressionLength} characters.");
        }

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

                    if (sb.Length >= RuleExpressionJson.MaxTextLength)
                    {
                        throw new LimitExceededException($"String literal in FormalExpression exceeds {RuleExpressionJson.MaxTextLength} characters.");
                    }

                    sb.Append(input[i]);
                    i++;
                }

                if (i >= input.Length)
                {
                    throw new FormatException("Unterminated string literal in FormalExpression.");
                }

                i++;
                AddToken(tokens, new Token(TokenKind.String, sb.ToString()));
                continue;
            }

            if (i + 1 < input.Length)
            {
                var two = input.Substring(i, 2);
                if (two is "==" or "!=" or "<>" or "<=" or ">=" or "&&" or "||")
                {
                    AddToken(tokens, new Token(TokenKind.Symbol, two));
                    i += 2;
                    continue;
                }
            }

            if (c is '(' or ')' or ',' or '+' or '-' or '*' or '/' or '<' or '>' or '=' or '!')
            {
                // Keep '-' inside an identifier only when we are already reading an identifier below; standalone '-' is a symbol.
                AddToken(tokens, new Token(TokenKind.Symbol, c.ToString()));
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

                AddToken(tokens, new Token(TokenKind.Number, input[start..i]));
                continue;
            }

            if (c == '$' && i + 1 < input.Length && (char.IsAsciiLetter(input[i + 1]) || input[i + 1] == '_'))
            {
                i++;
                c = input[i];
            }

            if (c == '.' && (i + 1 >= input.Length || !char.IsAsciiLetterOrDigit(input[i + 1])))
            {
                AddToken(tokens, new Token(TokenKind.Identifier, "."));
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
                        if (i - start > RuleExpressionJson.MaxTextLength)
                        {
                            throw new LimitExceededException($"Identifier in FormalExpression exceeds {RuleExpressionJson.MaxTextLength} characters.");
                        }

                        continue;
                    }

                    // Allow '-' inside identifiers such as string-length(...) or IT-SYSBP when followed by letter/digit.
                    if (ch == '-' && i + 1 < input.Length && char.IsAsciiLetterOrDigit(input[i + 1]))
                    {
                        i++;
                        if (i - start > RuleExpressionJson.MaxTextLength)
                        {
                            throw new LimitExceededException($"Identifier in FormalExpression exceeds {RuleExpressionJson.MaxTextLength} characters.");
                        }

                        continue;
                    }

                    break;
                }

                AddToken(tokens, new Token(TokenKind.Identifier, input[start..i]));
                continue;
            }

            throw new FormatException($"Unsupported character '{c}' in FormalExpression.");
        }

        return tokens;
    }

    private static void AddToken(List<Token> tokens, Token token)
    {
        if (tokens.Count >= MaxTokens)
        {
            throw new LimitExceededException($"FormalExpression exceeds the maximum of {MaxTokens} tokens.");
        }

        tokens.Add(token);
    }

    [GeneratedRegex(@"ItemData\s*\[\s*@ItemOID\s*=\s*['""](?<oid>[^'""]+)['""]\s*\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ItemDataXPathRegex();

    private sealed class LimitExceededException(string message) : FormatException(message);

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
        private int _depth = -1;
        private int _nodes;

        public bool IsAtEnd => _pos >= tokens.Count;

        public Token Current => _pos < tokens.Count ? tokens[_pos] : default;

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
            if (_depth + (++chainDepth) > MaxDepth)
            {
                throw new LimitExceededException($"FormalExpression is nested more than {MaxDepth} levels.");
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
                if (!IsAtEnd && Current.Kind == TokenKind.Number
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
                case TokenKind.Number:
                    if (!decimal.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    {
                        throw new FormatException($"Invalid numeric literal '{token.Text}'.");
                    }

                    return AddNode(new LiteralExpression(RuleValue.FromNumber(num)));

                case TokenKind.String:
                    return AddNode(new LiteralExpression(RuleValue.FromText(token.Text)));

                case TokenKind.Identifier:
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
            if (_depth >= MaxDepth)
            {
                throw new LimitExceededException($"FormalExpression is nested more than {MaxDepth} levels.");
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
            if (++_depth > MaxDepth)
            {
                _depth--;
                throw new LimitExceededException($"FormalExpression is nested more than {MaxDepth} levels.");
            }
        }

        private T AddNode<T>(T node)
            where T : RuleExpression
        {
            if (++_nodes > MaxNodes)
            {
                throw new LimitExceededException($"FormalExpression has more than {MaxNodes} nodes.");
            }

            return node;
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
