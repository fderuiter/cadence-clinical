using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.Rules.Expressions;

namespace Cadence.Ddf;

/// <summary>
/// Parses ODM v1.3.2 <c>FormalExpression</c> strings (both canonical JSON rule expressions and infix/functional clinical
/// expressions) into typed <see cref="RuleExpression"/> syntax trees.
/// </summary>
public static class OdmFormalExpressionParser
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
            catch (OdmExpressionLimitExceededException ex)
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
            var normalized = OdmFormalExpressionTokenizer.NormalizeItemDataXPath(trimmed);
            var tokens = OdmFormalExpressionTokenizer.Tokenize(normalized);
            if (tokens.Count == 0)
            {
                error = "FormalExpression contains no tokens.";
                return false;
            }

            var parser = new OdmFormalExpressionInfixParser(tokens, resolver, defaultVariable);
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
        catch (OdmExpressionLimitExceededException ex)
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
            throw new OdmExpressionLimitExceededException($"FormalExpression is nested more than {RuleExpressionJson.MaxDepth} levels.");
        }

        if (++nodes > MaxNodes)
        {
            throw new OdmExpressionLimitExceededException($"FormalExpression has more than {MaxNodes} nodes.");
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
            throw new OdmExpressionLimitExceededException($"FormalExpression is nested more than {maxDepth} levels.");
        }

        if (++nodes > maxNodes)
        {
            throw new OdmExpressionLimitExceededException($"FormalExpression has more than {maxNodes} nodes.");
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
            throw new OdmExpressionLimitExceededException(
                $"FormalExpression exceeds the maximum serialized JSON depth of {maxJsonDepth} levels.");
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
}
